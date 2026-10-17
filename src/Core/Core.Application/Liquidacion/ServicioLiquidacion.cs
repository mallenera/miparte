using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Liquidacion;

/// <summary>Datos para registrar un pago de liquidación entre dos miembros.</summary>
/// <param name="HogarId">Hogar al que pertenece el pago.</param>
/// <param name="Mes">Primer día del mes que se liquida.</param>
/// <param name="De">Miembro que paga.</param>
/// <param name="A">Miembro que recibe.</param>
/// <param name="Importe">Importe pagado.</param>
/// <param name="Fecha">Fecha del pago; hoy si es null.</param>
/// <param name="Concepto">Nota opcional.</param>
public sealed record RegistrarPago(Guid HogarId, DateOnly Mes, Guid De, Guid A, decimal Importe, DateOnly? Fecha, string? Concepto);

/// <summary>Resumen y liquidación de un mismo mes, calculados con los mismos datos.</summary>
public sealed record ResumenYLiquidacion(ResumenDelMes Resumen, LiquidacionCalculada Liquidacion);

/// <summary>Casos de uso del resumen mensual, la liquidación y los pagos entre miembros.</summary>
public sealed class ServicioLiquidacion(IRepositorioLiquidacion repositorio, TimeProvider reloj)
{
    /// <summary>Resumen del mes que empieza en <paramref name="inicio"/>.</summary>
    public async Task<ResumenDelMes> ResumenAsync(DateOnly inicio, CancellationToken ct)
    {
        var delMes = await repositorio.GastosDelMesAsync(inicio, ct);
        var miembros = await repositorio.MiembrosAsync(ct);
        var categorias = await repositorio.NombresCategoriasAsync(ct);
        var historico = await repositorio.HistoricoCuentaAsync(inicio, ct);
        return ResumenMes.Construir(inicio, delMes, miembros, categorias, historico);
    }

    /// <summary>Liquidación del mes que empieza en <paramref name="inicio"/>: saldos, transferencias y pagos registrados.</summary>
    public async Task<LiquidacionCalculada> LiquidacionAsync(DateOnly inicio, CancellationToken ct)
    {
        var delMes = await repositorio.GastosDelMesAsync(inicio, ct);
        var pagos = await repositorio.PagosDelMesAsync(inicio, ct);
        var miembros = await repositorio.MiembrosAsync(ct);
        return LiquidacionMes.Calcular(delMes, pagos, miembros);
    }

    /// <summary>
    /// Resumen y liquidación del mes con una sola lectura de gastos y miembros (ambos cálculos los necesitan), de modo que
    /// los importes coinciden exactamente con los de <see cref="ResumenAsync"/> y <see cref="LiquidacionAsync"/>.
    /// </summary>
    public async Task<ResumenYLiquidacion> ResumenYLiquidacionAsync(DateOnly inicio, CancellationToken ct)
    {
        var delMes = await repositorio.GastosDelMesAsync(inicio, ct);
        var miembros = await repositorio.MiembrosAsync(ct);
        var categorias = await repositorio.NombresCategoriasAsync(ct);
        var historico = await repositorio.HistoricoCuentaAsync(inicio, ct);
        var pagos = await repositorio.PagosDelMesAsync(inicio, ct);
        return new ResumenYLiquidacion(
            ResumenMes.Construir(inicio, delMes, miembros, categorias, historico),
            LiquidacionMes.Calcular(delMes, pagos, miembros));
    }

    /// <summary>
    /// Registra un pago entre dos miembros. Es inválido si el mes no es día 1, los miembros coinciden, el importe o el concepto
    /// no valen o algún miembro no es del hogar; es un conflicto si supera la deuda pendiente del par (solo se comprueba si el
    /// mes tiene gastos, porque si no la deuda no es calculable).
    /// </summary>
    public async Task<Resultado<PagoLiquidacionRegistro>> RegistrarPagoAsync(RegistrarPago pago, CancellationToken ct)
    {
        if (pago.Mes.Day != 1) return Resultado<PagoLiquidacionRegistro>.Invalido("El mes del pago debe ser el día 1 del mes.");
        if (pago.De == pago.A) return Resultado<PagoLiquidacionRegistro>.Invalido("Quien paga y quien recibe deben ser distintos.");
        var errorCampos = ReglasMovimiento.ValidarImporte(pago.Importe) ?? ReglasMovimiento.ValidarConcepto(pago.Concepto);
        if (errorCampos is not null) return Resultado<PagoLiquidacionRegistro>.Invalido(errorCampos);
        if (!await repositorio.ExistenAmbosAsync(pago.De, pago.A, ct))
            return Resultado<PagoLiquidacionRegistro>.Invalido("Los miembros del pago deben pertenecer al hogar.");

        var liquidacion = await LiquidacionAsync(pago.Mes, ct);
        if (liquidacion.HayGastos)
        {
            var pendiente = liquidacion.PendienteEntre(pago.De, pago.A);
            if (pago.Importe > pendiente)
                return Resultado<PagoLiquidacionRegistro>.Conflicto(
                    $"El importe supera la deuda pendiente entre ambos miembros ({pendiente:0.00}).",
                    new Dictionary<string, object> { ["pendiente"] = pendiente });
        }

        var registro = new PagoLiquidacionRegistro
        {
            Id = Guid.NewGuid(), HogarId = pago.HogarId, Mes = pago.Mes, DeMiembroId = pago.De, AMiembroId = pago.A,
            Importe = pago.Importe, Fecha = pago.Fecha ?? DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime),
            Concepto = ReglasMovimiento.NormalizarConcepto(pago.Concepto),
        };
        await repositorio.AnadirPagoAsync(registro, ct);
        return Resultado<PagoLiquidacionRegistro>.Ok(registro);
    }

    /// <summary>Borra un pago de liquidación; no encontrado si no existe en el hogar.</summary>
    public async Task<Resultado<bool>> EliminarPagoAsync(Guid id, CancellationToken ct)
        => await repositorio.EliminarPagoAsync(id, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();
}
