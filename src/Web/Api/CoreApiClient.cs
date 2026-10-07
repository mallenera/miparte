using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Cliente tipado de Core.Api. Cubre usuario, hogares, miembros, invitaciones, categorías, perfiles y gastos; el resto se añadirá con cada pantalla.</summary>
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

    /// <summary>Miembros activos del hogar actual (<c>GET /api/miembros</c>); <c>EsYo</c> marca al usuario autenticado.</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<MiembroDto>> ListarMiembrosAsync(CancellationToken ct = default) =>
        ObtenerAsync<List<MiembroDto>>("api/miembros", ct);

    /// <summary>Añade una persona sin cuenta (<c>POST /api/miembros</c>, solo admin).</summary>
    /// <param name="peticion">Nombre, tipo y responsable (si es a cargo).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MiembroDto> CrearMiembroAsync(CrearMiembroRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<MiembroDto>(HttpMethod.Post, "api/miembros", peticion, ct);

    /// <summary>Edita un miembro; los campos null no cambian (<c>PUT /api/miembros/{id}</c>).</summary>
    /// <param name="id">Miembro a editar.</param>
    /// <param name="peticion">Cambios.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MiembroDto> ActualizarMiembroAsync(Guid id, ActualizarMiembroRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<MiembroDto>(HttpMethod.Put, $"api/miembros/{id}", peticion, ct);

    /// <summary>Crea una invitación (<c>POST /api/invitaciones</c>, solo admin); el token solo se devuelve aquí.</summary>
    /// <param name="peticion">Miembro existente al que vincular, o null para un nuevo adulto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<InvitacionCreada> CrearInvitacionAsync(CrearInvitacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<InvitacionCreada>(HttpMethod.Post, "api/invitaciones", peticion, ct);

    /// <summary>Categorías del hogar actual (<c>GET /api/categorias</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<CategoriaDto>> ListarCategoriasAsync(CancellationToken ct = default) =>
        ObtenerAsync<List<CategoriaDto>>("api/categorias", ct);

    /// <summary>Crea una categoría (<c>POST /api/categorias</c>).</summary>
    /// <param name="peticion">Nombre, categoría padre y perfil por defecto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CategoriaDto> CrearCategoriaAsync(GuardarCategoriaRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<CategoriaDto>(HttpMethod.Post, "api/categorias", peticion, ct);

    /// <summary>Reemplaza todos los campos de una categoría (<c>PUT /api/categorias/{id}</c>).</summary>
    /// <param name="id">Categoría a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CategoriaDto> GuardarCategoriaAsync(Guid id, GuardarCategoriaRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<CategoriaDto>(HttpMethod.Put, $"api/categorias/{id}", peticion, ct);

    /// <summary>Elimina una categoría sin subcategorías ni gastos (<c>DELETE /api/categorias/{id}</c>).</summary>
    /// <param name="id">Categoría a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarCategoriaAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/categorias/{id}", ct);

    /// <summary>Perfiles de reparto con su detalle (<c>GET /api/perfiles</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<PerfilRepartoDto>> ListarPerfilesAsync(CancellationToken ct = default) =>
        ObtenerAsync<List<PerfilRepartoDto>>("api/perfiles", ct);

    /// <summary>Crea un perfil de reparto (<c>POST /api/perfiles</c>).</summary>
    /// <param name="peticion">Nombre, modo y detalle.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PerfilRepartoDto> CrearPerfilAsync(GuardarPerfilRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<PerfilRepartoDto>(HttpMethod.Post, "api/perfiles", peticion, ct);

    /// <summary>Reemplaza nombre, modo y detalle de un perfil (<c>PUT /api/perfiles/{id}</c>).</summary>
    /// <param name="id">Perfil a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PerfilRepartoDto> GuardarPerfilAsync(Guid id, GuardarPerfilRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<PerfilRepartoDto>(HttpMethod.Put, $"api/perfiles/{id}", peticion, ct);

    /// <summary>Elimina un perfil que no esté en uso (<c>DELETE /api/perfiles/{id}</c>).</summary>
    /// <param name="id">Perfil a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarPerfilAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/perfiles/{id}", ct);

    /// <summary>Gastos del hogar con su reparto (<c>GET /api/gastos</c>).</summary>
    /// <param name="mes">Mes en formato <c>YYYY-MM</c>, o null para todos.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosAsync(string? mes = null, CancellationToken ct = default) =>
        ObtenerAsync<List<GastoResponse>>(mes is null ? "api/gastos" : $"api/gastos?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Crea un gasto; el servidor calcula y guarda el reparto (<c>POST /api/gastos</c>).</summary>
    /// <param name="peticion">Datos del gasto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoResponse> CrearGastoAsync(GastoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<GastoResponse>(HttpMethod.Post, "api/gastos", peticion, ct);

    /// <summary>Edita un gasto y recalcula su reparto (<c>PUT /api/gastos/{id}</c>).</summary>
    /// <param name="id">Gasto a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoResponse> GuardarGastoAsync(Guid id, GastoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<GastoResponse>(HttpMethod.Put, $"api/gastos/{id}", peticion, ct);

    /// <summary>Elimina un gasto y su reparto (<c>DELETE /api/gastos/{id}</c>).</summary>
    /// <param name="id">Gasto a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarGastoAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/gastos/{id}", ct);

    /// <summary>Estado de la cuenta común al final de un mes (<c>GET /api/cuenta-comun</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CuentaComunResponse> ObtenerCuentaComunAsync(string mes, CancellationToken ct = default) =>
        ObtenerAsync<CuentaComunResponse>($"api/cuenta-comun?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Fija lo que aporta un adulto a la cuenta común desde un mes (<c>PUT /api/cuenta-comun/aportaciones</c>).</summary>
    /// <param name="peticion">Adulto, mes de inicio (día 1) e importe.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<AportacionCuentaDto> FijarAportacionAsync(FijarAportacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<AportacionCuentaDto>(HttpMethod.Put, "api/cuenta-comun/aportaciones", peticion, ct);

    /// <summary>Registra un reembolso de la cuenta común a quien adelantó gastos (<c>POST /api/cuenta-comun/reembolsos</c>).</summary>
    /// <param name="peticion">Miembro, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ReembolsoCuentaDto> CrearReembolsoAsync(CrearReembolsoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<ReembolsoCuentaDto>(HttpMethod.Post, "api/cuenta-comun/reembolsos", peticion, ct);

    /// <summary>Elimina un reembolso (<c>DELETE /api/cuenta-comun/reembolsos/{id}</c>).</summary>
    /// <param name="id">Reembolso a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarReembolsoAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/reembolsos/{id}", ct);

    private Task<T> ObtenerAsync<T>(string ruta, CancellationToken ct) => EnviarAsync<T>(HttpMethod.Get, ruta, null, ct);

    private async Task EnviarSinRespuestaAsync(HttpMethod metodo, string ruta, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
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
        }
    }

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
