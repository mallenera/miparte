using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Cliente tipado de Core.Api. De momento solo cubre usuario y hogares; el resto se añadirá con cada pantalla.</summary>
public sealed class CoreApiClient
{
    private readonly HttpClient _http;

    /// <summary>Crea el cliente.</summary>
    /// <param name="http">Cliente HTTP con la URL base de Core.Api y el <see cref="ManejadorCoreApi"/>.</param>
    public CoreApiClient(HttpClient http) => _http = http;

    /// <summary>Usuario autenticado, sus hogares y el hogar actual (<c>GET /api/yo</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<YoResponse> YoAsync(CancellationToken ct = default) => ObtenerAsync<YoResponse>("api/yo", ct);

    /// <summary>Crea un hogar del que el usuario es admin (<c>POST /api/hogares</c>).</summary>
    /// <param name="peticion">Nombre del hogar y del miembro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> CrearHogarAsync(CrearHogarRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<HogarResumen>(HttpMethod.Post, "api/hogares", peticion, ct);

    /// <summary>Acepta una invitación y devuelve el hogar al que se une (<c>POST /api/invitaciones/aceptar</c>).</summary>
    /// <param name="peticion">Token de la invitación y, si hace falta, el nombre del nuevo miembro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> AceptarInvitacionAsync(AceptarInvitacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<HogarResumen>(HttpMethod.Post, "api/invitaciones/aceptar", peticion, ct);

    private Task<T> ObtenerAsync<T>(string ruta, CancellationToken ct) => EnviarAsync<T>(HttpMethod.Get, ruta, null, ct);

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct)
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

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode) throw new ApiException(respuesta.StatusCode, await LeerErrorAsync(respuesta, ct));
            return await respuesta.Content.ReadFromJsonAsync<T>(ct)
                   ?? throw new ApiException(HttpStatusCode.BadGateway, "Respuesta vacía del servidor.");
        }
    }

    private static async Task<string> LeerErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (cuerpo.ValueKind == JsonValueKind.Object && cuerpo.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } texto)
                return texto;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Sin cuerpo JSON: se usa el mensaje por código.
        }

        return respuesta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión ha caducado. Vuelve a iniciar sesión.",
            HttpStatusCode.Forbidden => "No tienes permiso para hacer esto.",
            HttpStatusCode.NotFound => "No se ha encontrado lo que buscas.",
            HttpStatusCode.Conflict => "La operación no se puede realizar en el estado actual.",
            _ => "Ha ocurrido un error en el servidor.",
        };
    }
}
