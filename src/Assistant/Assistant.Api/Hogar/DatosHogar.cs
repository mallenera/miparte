using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MiParte.Contracts;

namespace MiParte.Assistant.Api.Hogar;

/// <summary>
/// Datos del hogar que el asistente puede consultar. Todo pasa por Core.Api con el JWT de la persona y su
/// <c>X-Hogar-Id</c>, de modo que la multitenencia (RLS y filtro por hogar) la sigue aplicando Core y el asistente
/// nunca ve datos de otro hogar ni tiene credenciales propias sobre la base de datos.
/// </summary>
public interface IDatosHogar
{
    /// <summary>Miembros del hogar. Sirve también para comprobar que la persona pertenece al hogar.</summary>
    Task<IReadOnlyList<MiembroDto>> MiembrosAsync(CancellationToken ct);

    /// <summary>Categorías del hogar.</summary>
    Task<IReadOnlyList<CategoriaDto>> CategoriasAsync(CancellationToken ct);

    /// <summary>Gastos de un mes (<c>YYYY-MM</c>) con su reparto.</summary>
    Task<IReadOnlyList<GastoResponse>> GastosAsync(string mes, CancellationToken ct);

    /// <summary>Resumen mensual por miembro y categoría.</summary>
    Task<ResumenMensualResponse> ResumenAsync(string mes, CancellationToken ct);

    /// <summary>Saldos y transferencias sugeridas del mes.</summary>
    Task<LiquidacionResponse> LiquidacionAsync(string mes, CancellationToken ct);
}

/// <summary>Crea el acceso a los datos de un hogar en nombre de una persona autenticada.</summary>
public interface IFabricaDatosHogar
{
    /// <summary>Acceso a Core.Api con el JWT y el hogar indicados.</summary>
    /// <param name="jwt">Token de acceso de Supabase de la persona (se reenvía tal cual a Core.Api).</param>
    /// <param name="hogarId">Hogar sobre el que se pregunta.</param>
    IDatosHogar Crear(string jwt, Guid hogarId);
}

/// <summary>Core.Api rechazó el acceso (401, 403 o 409): la persona no puede consultar ese hogar.</summary>
/// <param name="codigo">Código HTTP devuelto por Core.Api.</param>
public sealed class HogarNoAccesibleException(HttpStatusCode codigo)
    : Exception($"Core.Api rechazó el acceso al hogar ({(int)codigo}).")
{
    /// <summary>Código HTTP devuelto por Core.Api.</summary>
    public HttpStatusCode Codigo { get; } = codigo;
}

/// <summary>Core.Api no pudo atender la consulta (caída, 5xx, 429 o respuesta inesperada).</summary>
public sealed class DatosNoDisponiblesException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>Fábrica que habla con Core.Api a través del cliente HTTP con nombre <see cref="NombreCliente"/>.</summary>
/// <param name="http">Fábrica de clientes HTTP.</param>
public sealed class FabricaDatosHogarCore(IHttpClientFactory http) : IFabricaDatosHogar
{
    /// <summary>Nombre del cliente HTTP hacia Core.Api.</summary>
    public const string NombreCliente = "core";

    /// <inheritdoc />
    public IDatosHogar Crear(string jwt, Guid hogarId) => new DatosHogarCore(http.CreateClient(NombreCliente), jwt, hogarId);
}

/// <summary>Implementación de <see cref="IDatosHogar"/> sobre los endpoints de lectura de Core.Api.</summary>
public sealed class DatosHogarCore(HttpClient http, string jwt, Guid hogarId) : IDatosHogar
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MiembroDto>> MiembrosAsync(CancellationToken ct)
        => await LeerAsync<List<MiembroDto>>("/api/miembros", ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoriaDto>> CategoriasAsync(CancellationToken ct)
        => await LeerAsync<List<CategoriaDto>>("/api/categorias", ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GastoResponse>> GastosAsync(string mes, CancellationToken ct)
        => await LeerAsync<List<GastoResponse>>($"/api/gastos?mes={Uri.EscapeDataString(mes)}", ct);

    /// <inheritdoc />
    public Task<ResumenMensualResponse> ResumenAsync(string mes, CancellationToken ct)
        => LeerAsync<ResumenMensualResponse>($"/api/resumen?mes={Uri.EscapeDataString(mes)}", ct);

    /// <inheritdoc />
    public Task<LiquidacionResponse> LiquidacionAsync(string mes, CancellationToken ct)
        => LeerAsync<LiquidacionResponse>($"/api/liquidacion?mes={Uri.EscapeDataString(mes)}", ct);

    private async Task<T> LeerAsync<T>(string ruta, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        peticion.Headers.Add("X-Hogar-Id", hogarId.ToString());
        try
        {
            using var respuesta = await http.SendAsync(peticion, ct);
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict)
                throw new HogarNoAccesibleException(respuesta.StatusCode);
            if (!respuesta.IsSuccessStatusCode)
                throw new DatosNoDisponiblesException($"Core.Api respondió {(int)respuesta.StatusCode} en {ruta.Split('?')[0]}.");
            return await respuesta.Content.ReadFromJsonAsync<T>(ct)
                   ?? throw new DatosNoDisponiblesException("Core.Api devolvió una respuesta vacía.");
        }
        catch (HttpRequestException ex)
        {
            throw new DatosNoDisponiblesException("No se pudo contactar con Core.Api.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DatosNoDisponiblesException("Core.Api tardó demasiado en responder.", ex);
        }
    }
}
