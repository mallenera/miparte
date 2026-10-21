using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Resumen, liquidación, pagos entre miembros y cierres de mes.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Resumen del mes: pagado y asumido por miembro y total por categoría (<c>GET /api/resumen</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ResumenMensualResponse> ObtenerResumenAsync(string mes, CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<ResumenMensualResponse>($"api/resumen?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Saldos, transferencias sugeridas y pagos del mes (<c>GET /api/liquidacion</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<LiquidacionResponse> ObtenerLiquidacionAsync(string mes, CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<LiquidacionResponse>($"api/liquidacion?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>
    /// Resumen y liquidación del mes en una sola petición (<c>GET /api/resumen-liquidacion</c>): lo que pinta la pestaña Resumen, sin
    /// leer dos veces los gastos y los miembros del mes.
    /// </summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ResumenYLiquidacionResponse> ObtenerResumenYLiquidacionAsync(string mes, CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<ResumenYLiquidacionResponse>($"api/resumen-liquidacion?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Registra un pago entre dos miembros para saldar un mes (<c>POST /api/pagos-liquidacion</c>).</summary>
    /// <param name="peticion">Mes (día 1), pagador, receptor, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PagoLiquidacionDto> CrearPagoLiquidacionAsync(CrearPagoLiquidacionRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<PagoLiquidacionDto>(HttpMethod.Post, "api/pagos-liquidacion", peticion, ct);

    /// <summary>Elimina un pago de liquidación (<c>DELETE /api/pagos-liquidacion/{id}</c>).</summary>
    /// <param name="id">Pago a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarPagoLiquidacionAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/pagos-liquidacion/{id}", ct);

    /// <summary>Meses cerrados del hogar (<c>GET /api/cierres-mes</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<MesCerradoDto>> ListarCierresMesAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerListaAsync<MesCerradoDto>("api/cierres-mes", ct);

    /// <summary>Cierra un mes; puede cualquier miembro activo (<c>POST /api/cierres-mes</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MesCerradoDto> CerrarMesAsync(string mes, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<MesCerradoDto>(HttpMethod.Post, "api/cierres-mes", new CerrarMesRequest(mes), ct);

    /// <summary>Reabre un mes cerrado; solo admin (<c>DELETE /api/cierres-mes/{mes}</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ReabrirMesAsync(string mes, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cierres-mes/{Uri.EscapeDataString(mes)}", ct);
}
