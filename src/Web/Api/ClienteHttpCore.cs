using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MiParte.Web.Api;

/// <summary>
/// Plomería HTTP de <see cref="CoreApiClient"/>: envía las peticiones, traduce los errores a <see cref="ApiException"/> y mantiene
/// la caché de lecturas al día tras cada escritura. Las áreas de la API la usan; no la llama ninguna pantalla directamente.
/// </summary>
internal sealed class ClienteHttpCore
{
    /// <summary>Rutas cuyas lecturas se guardan en <see cref="CacheLecturas"/>.</summary>
    private static readonly string[] RutasEnCache = ["api/miembros", "api/categorias", "api/perfiles", "api/cierres-mes"];

    private readonly HttpClient _http;
    private readonly CacheLecturas? _cache;

    /// <summary>Crea la plomería sobre un cliente HTTP y, opcionalmente, la caché de lecturas.</summary>
    /// <param name="http">Cliente HTTP con la URL base de Core.Api y el <see cref="ManejadorCoreApi"/>.</param>
    /// <param name="cache">Caché de lecturas compartida; sin ella cada lectura va a la API.</param>
    internal ClienteHttpCore(HttpClient http, CacheLecturas? cache)
    {
        _http = http;
        _cache = cache;
    }

    /// <summary>Lectura directa de una ruta, sin caché.</summary>
    /// <param name="ruta">Ruta relativa a la URL base de Core.Api, con su query si la tiene.</param>
    /// <param name="ct">Token de cancelación.</param>
    internal Task<T> ObtenerAsync<T>(string ruta, CancellationToken ct) => EnviarAsync<T>(HttpMethod.Get, ruta, null, ct);

    /// <summary>Lectura de una lista que casi no cambia: pasa por la caché, que la reutiliza entre pantallas durante <see cref="CacheLecturas.Vigencia"/>.</summary>
    /// <param name="ruta">Ruta relativa a la URL base de Core.Api.</param>
    /// <param name="ct">Token de cancelación.</param>
    internal Task<List<T>> ObtenerListaAsync<T>(string ruta, CancellationToken ct) =>
        _cache is null
            ? ObtenerAsync<List<T>>(ruta, ct)
            : _cache.ListaAsync(ruta, () => ObtenerAsync<List<T>>(ruta, CancellationToken.None), ct);

    /// <summary>
    /// Tras cualquier escritura (también si falla: un 409 o un 404 suelen indicar que lo cacheado ya no es cierto) se olvidan las
    /// listas guardadas que ese cambio puede afectar; sin esto la pantalla siguiente enseñaría la categoría borrada o el miembro nuevo sin él.
    /// </summary>
    private void InvalidarTrasEscritura(HttpMethod metodo, string ruta)
    {
        if (_cache is null || metodo == HttpMethod.Get) return;
        // Aceptar una invitación o unirse por enlace añade o vincula un miembro; crear o borrar un hogar cambia de contexto por completo.
        if (ruta.StartsWith("api/invitaciones", StringComparison.Ordinal) || ruta.StartsWith("api/enlaces-grupo", StringComparison.Ordinal)
            || ruta.StartsWith("api/hogar", StringComparison.Ordinal))
        {
            _cache.Limpiar();
            return;
        }

        var afectadas = RutasEnCache.Where(r => ruta.StartsWith(r, StringComparison.Ordinal)).ToList();
        // Borrar un perfil en uso pasa sus categorías a otro perfil (?moverA=): la lista de categorías guardada apuntaría a un perfil
        // que ya no existe y el formulario de gastos lo propondría como reparto por defecto.
        if (metodo == HttpMethod.Delete && ruta.StartsWith("api/perfiles", StringComparison.Ordinal) && !afectadas.Contains("api/categorias"))
            afectadas.Add("api/categorias");
        if (afectadas.Count > 0) _cache.Invalidar(afectadas.ToArray());
    }

    /// <summary>Envía una escritura que no devuelve cuerpo; lanza <see cref="ApiException"/> si la API responde con error.</summary>
    /// <param name="metodo">Verbo HTTP.</param>
    /// <param name="ruta">Ruta relativa a la URL base de Core.Api.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <param name="cuerpo">Cuerpo JSON de la petición, si lo lleva.</param>
    internal async Task EnviarSinRespuestaAsync(HttpMethod metodo, string ruta, CancellationToken ct, object? cuerpo = null)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) peticion.Content = JsonContent.Create(cuerpo, cuerpo.GetType());
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "No se pudo conectar con el servidor. Comprueba tu conexión.");
        }
        finally
        {
            InvalidarTrasEscritura(metodo, ruta);
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                var (mensaje, enUso) = await LeerErrorAsync(respuesta, ct);
                throw new ApiException(respuesta.StatusCode, mensaje) { EnUso = enUso };
            }
        }
    }

    /// <summary>Envía una petición y deserializa la respuesta; lanza <see cref="ApiException"/> si la API responde con error o con datos no válidos.</summary>
    /// <param name="metodo">Verbo HTTP.</param>
    /// <param name="ruta">Ruta relativa a la URL base de Core.Api.</param>
    /// <param name="cuerpo">Cuerpo JSON de la petición, si lo lleva.</param>
    /// <param name="ct">Token de cancelación.</param>
    internal async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct, string? cabeceraEnlace = null)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) peticion.Content = JsonContent.Create(cuerpo, cuerpo.GetType());
        // Token del enlace QR de la lista de la compra: lo usan las rutas públicas, que no tienen sesión.
        if (cabeceraEnlace is not null) peticion.Headers.TryAddWithoutValidation("X-Enlace-Lista", cabeceraEnlace);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "No se pudo conectar con el servidor. Comprueba tu conexión.");
        }
        finally
        {
            InvalidarTrasEscritura(metodo, ruta);
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                var (mensaje, enUso) = await LeerErrorAsync(respuesta, ct);
                throw new ApiException(respuesta.StatusCode, mensaje) { EnUso = enUso };
            }

            try
            {
                return await respuesta.Content.ReadFromJsonAsync<T>(ct)
                       ?? throw new ApiException(HttpStatusCode.BadGateway, "Respuesta vacía del servidor.");
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                // Típico de un Api:CoreUrl que apunta a otra cosa (p. ej. al propio front, que responde con index.html).
                throw new ApiException(HttpStatusCode.BadGateway,
                    "El servidor no ha respondido con datos válidos. Comprueba que Api:CoreUrl apunta a Core.Api y que está en marcha.");
            }
        }
    }

    /// <summary>Mensaje del cuerpo <c>{ "error": ... }</c> (o uno por código) y si trae <c>enUso: true</c>, en una sola lectura del cuerpo.</summary>
    private static async Task<(string Mensaje, bool EnUso)> LeerErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (cuerpo.ValueKind == JsonValueKind.Object && cuerpo.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } texto)
                return (texto, cuerpo.TryGetProperty("enUso", out var u) && u.ValueKind == JsonValueKind.True);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Sin cuerpo JSON: se usa el mensaje por código.
        }

        return (MensajePorCodigo(respuesta.StatusCode), false);
    }

    private static string MensajePorCodigo(HttpStatusCode codigo) =>
        codigo switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión ha caducado. Vuelve a iniciar sesión.",
            HttpStatusCode.Forbidden => "No tienes permiso para hacer esto.",
            HttpStatusCode.NotFound => "No se ha encontrado lo que buscas.",
            HttpStatusCode.Conflict => "La operación no se puede realizar en el estado actual.",
            _ => "Ha ocurrido un error en el servidor.",
        };
}
