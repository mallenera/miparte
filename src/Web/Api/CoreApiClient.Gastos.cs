using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Gastos del hogar (alta, edición, borrado, listados y filtros) y plantillas recurrentes.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Gastos de un mes del hogar con su reparto (<c>GET /api/gastos</c>); la API exige acotar por mes o por fechas.</summary>
    /// <param name="mes">Mes en formato <c>YYYY-MM</c>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosAsync(string mes, CancellationToken ct = default) =>
        ListarGastosAsync(mes, null, null, null, ct);

    /// <summary>Gastos de un mes filtrados (<c>GET /api/gastos</c>); los filtros nulos o vacíos no se envían.</summary>
    /// <param name="mes">Mes en formato <c>YYYY-MM</c>.</param>
    /// <param name="categoriaId">Solo gastos de esta categoría.</param>
    /// <param name="miembroId">Solo gastos que paga o en cuyo reparto asume algo este miembro.</param>
    /// <param name="buscar">Texto que debe contener el concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosAsync(string mes, Guid? categoriaId, Guid? miembroId, string? buscar, CancellationToken ct = default)
    {
        var filtros = new List<string> { $"mes={Uri.EscapeDataString(mes)}" };
        if (categoriaId is { } c) filtros.Add($"categoriaId={c}");
        if (miembroId is { } m) filtros.Add($"miembroId={m}");
        if (!string.IsNullOrWhiteSpace(buscar)) filtros.Add($"buscar={Uri.EscapeDataString(buscar.Trim())}");
        return _nucleo.ObtenerAsync<List<GastoResponse>>("api/gastos?" + string.Join('&', filtros), ct);
    }

    /// <summary>Gastos del hogar con fecha entre dos días, ambos incluidos (<c>GET /api/gastos?desde=&amp;hasta=</c>).</summary>
    /// <param name="desde">Primer día del periodo.</param>
    /// <param name="hasta">Último día del periodo.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosPorRangoAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        ListarGastosPorRangoAsync(desde, hasta, null, null, ct);

    /// <summary>Gastos entre dos días, ambos incluidos, con filtros opcionales de quién paga o asume y de texto.</summary>
    /// <param name="desde">Primer día del periodo.</param>
    /// <param name="hasta">Último día del periodo.</param>
    /// <param name="miembroId">Solo gastos que paga o en cuyo reparto asume algo este miembro.</param>
    /// <param name="buscar">Texto que debe contener el concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosPorRangoAsync(DateOnly desde, DateOnly hasta, Guid? miembroId, string? buscar, CancellationToken ct = default)
    {
        var url = $"api/gastos?desde={desde.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&hasta={hasta.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";
        if (miembroId is { } m) url += $"&miembroId={m}";
        if (!string.IsNullOrWhiteSpace(buscar)) url += $"&buscar={Uri.EscapeDataString(buscar.Trim())}";
        return _nucleo.ObtenerAsync<List<GastoResponse>>(url, ct);
    }

    /// <summary>Crea un gasto; el servidor calcula y guarda el reparto (<c>POST /api/gastos</c>).</summary>
    /// <param name="peticion">Datos del gasto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoResponse> CrearGastoAsync(GastoRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<GastoResponse>(HttpMethod.Post, "api/gastos", peticion, ct);

    /// <summary>Edita un gasto y recalcula su reparto (<c>PUT /api/gastos/{id}</c>).</summary>
    /// <param name="id">Gasto a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoResponse> GuardarGastoAsync(Guid id, GastoRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<GastoResponse>(HttpMethod.Put, $"api/gastos/{id}", peticion, ct);

    /// <summary>Elimina un gasto y su reparto (<c>DELETE /api/gastos/{id}</c>).</summary>
    /// <param name="id">Gasto a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarGastoAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/gastos/{id}", ct);

    /// <summary>Plantillas de gasto mensual del hogar, por día del mes (<c>GET /api/gastos-recurrentes</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoRecurrenteResponse>> ListarRecurrentesAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<List<GastoRecurrenteResponse>>("api/gastos-recurrentes", ct);

    /// <summary>Crea una plantilla de gasto mensual (<c>POST /api/gastos-recurrentes</c>).</summary>
    /// <param name="peticion">Importe, categoría, pagador, perfil, día del mes y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoRecurrenteResponse> CrearRecurrenteAsync(GastoRecurrenteRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<GastoRecurrenteResponse>(HttpMethod.Post, "api/gastos-recurrentes", peticion, ct);

    /// <summary>Edita una plantilla sin tocar los gastos ya generados (<c>PUT /api/gastos-recurrentes/{id}</c>).</summary>
    /// <param name="id">Plantilla a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoRecurrenteResponse> GuardarRecurrenteAsync(Guid id, GastoRecurrenteRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<GastoRecurrenteResponse>(HttpMethod.Put, $"api/gastos-recurrentes/{id}", peticion, ct);

    /// <summary>Borra una plantilla sin gastos generados (<c>DELETE /api/gastos-recurrentes/{id}</c>).</summary>
    /// <param name="id">Plantilla a borrar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarRecurrenteAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/gastos-recurrentes/{id}", ct);

    /// <summary>Crea los gastos del mes de las plantillas activas que aún no lo tienen; es idempotente (<c>POST /api/gastos-recurrentes/generar</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GenerarRecurrentesResponse> GenerarRecurrentesAsync(string mes, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<GenerarRecurrentesResponse>(HttpMethod.Post, $"api/gastos-recurrentes/generar?mes={Uri.EscapeDataString(mes)}", null, ct);
}
