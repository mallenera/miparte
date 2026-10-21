using MiParte.Core.Application.Notificaciones;
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
public sealed class ServicioLiquidacion(IRepositorioLiquidacion repositorio, TimeProvider reloj, IPublicadorNotificaciones? notificaciones = null)
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
        var miembros = await repositorio.MiembrosAsync(ct);
        return await LiquidarAsync(inicio, delMes, miembros, ct);
    }

    /// <summary>
    /// Liquidación que ve el hogar según su modo. En mensual, la del mes con sus pagos. En ciclos, los gastos pendientes desde el
    /// último cierre (el mes no importa) y sin pagos, porque en ciclos no se registran pagos (ver <see cref="RegistrarPagoAsync"/>).
    /// </summary>
    private async Task<LiquidacionCalculada> LiquidarAsync(
        DateOnly inicio, IReadOnlyList<Gasto> delMes, IReadOnlyList<Miembro> miembros, CancellationToken ct)
    {
        if (await repositorio.ModoAsync(ct) == ModoLiquidacion.Ciclos)
            return LiquidacionMes.Calcular(await repositorio.GastosPendientesAsync(ct), [], miembros);
        var pagos = await repositorio.PagosDelMesAsync(inicio, ct);
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
        return new ResumenYLiquidacion(
            ResumenMes.Construir(inicio, delMes, miembros, categorias, historico),
            await LiquidarAsync(inicio, delMes, miembros, ct));
    }

    /// <summary>
    /// Cambia el modo de liquidación del hogar. No altera los gastos ni los ciclos ya cerrados. Pasar a mensual es un conflicto si
    /// el hogar tiene ciclos cerrados: sus gastos ya están liquidados y el cálculo mensual no los contaría. Va bajo exclusión para
    /// que no se cruce con un cierre de ciclo en curso.
    /// </summary>
    public Task<Resultado<ModoLiquidacion>> CambiarModoAsync(ModoLiquidacion modo, CancellationToken ct)
        => repositorio.EnExclusionAsync(async () =>
        {
            if (modo == ModoLiquidacion.Mensual && await repositorio.UltimoNumeroCicloAsync(ct) > 0)
                return Resultado<ModoLiquidacion>.Conflicto(Mensajes.CiclosCerradosSinMensual);
            await repositorio.EstablecerModoAsync(modo, ct);
            return Resultado<ModoLiquidacion>.Ok(modo);
        }, ct);

    /// <summary>Ciclos cerrados del hogar, del más reciente al más antiguo.</summary>
    public Task<IReadOnlyList<CicloConAutor>> ListarCiclosAsync(CancellationToken ct) => repositorio.ListarCiclosAsync(ct);

    /// <summary>
    /// Cierra el ciclo actual: congela los gastos pendientes (los marca como liquidados) y guarda el ciclo con su total y fechas.
    /// Cerrar no bloquea los gastos, que siguen consultables. El siguiente ciclo empieza con el primer gasto que se registre después.
    /// Conflicto si el hogar liquida por meses o si no hay gastos pendientes.
    /// </summary>
    public async Task<Resultado<CicloConAutor>> CerrarCicloAsync(Miembro yo, CancellationToken ct)
    {
        try
        {
            return await repositorio.EnExclusionAsync(() => CerrarCicloBajoExclusionAsync(yo, ct), ct);
        }
        catch (CierreConcurrenteException)
        {
            return Resultado<CicloConAutor>.Conflicto(Mensajes.CierreConcurrente);
        }
    }

    private async Task<Resultado<CicloConAutor>> CerrarCicloBajoExclusionAsync(Miembro yo, CancellationToken ct)
    {
        if (await repositorio.ModoAsync(ct) != ModoLiquidacion.Ciclos)
            return Resultado<CicloConAutor>.Conflicto(Mensajes.HogarNoEsCiclos);

        var pendientes = CalculoCiclo.Pendientes(await repositorio.GastosPendientesAsync(ct));
        if (pendientes.Count == 0) return Resultado<CicloConAutor>.Conflicto(Mensajes.SinGastosPendientes);

        var (total, desde, hasta) = CalculoCiclo.Resumir(pendientes);
        var ciclo = new CicloLiquidacion
        {
            Id = Guid.NewGuid(), HogarId = yo.HogarId, Numero = await repositorio.UltimoNumeroCicloAsync(ct) + 1,
            CerradoEn = reloj.GetUtcNow(), CerradoPor = yo.Id, DesdeGastoFecha = desde, HastaFecha = hasta, Total = total,
        };
        await repositorio.CerrarCicloAsync(ciclo, pendientes.Select(g => g.Id).ToList(), ct);
        notificaciones?.Publicar(AvisoCicloCerrado(ciclo, yo.Id));
        return Resultado<CicloConAutor>.Ok(new CicloConAutor(ciclo, yo.Nombre));
    }

    /// <summary>Aviso del cierre de un ciclo: total y número, sin quien lo cerró (no lo recibe él).</summary>
    internal static NotificacionPush AvisoCicloCerrado(CicloLiquidacion ciclo, Guid cerradoPor)
        => new(ciclo.HogarId, cerradoPor, "Liquidación cerrada", $"Ciclo {ciclo.Numero} · {FormatoAvisos.Importe(ciclo.Total)} €", "/");

    /// <summary>Importe con separador decimal español, como en el resto de avisos.</summary>

    /// <summary>
    /// Registra un pago entre dos miembros. Es inválido si el mes no es día 1, los miembros coinciden, el importe o el concepto
    /// no valen o algún miembro no es del hogar; es un conflicto si supera la deuda pendiente del par (solo se comprueba si el
    /// mes tiene gastos, porque si no la deuda no es calculable).
    /// </summary>
    public async Task<Resultado<PagoLiquidacionRegistro>> RegistrarPagoAsync(RegistrarPago pago, CancellationToken ct)
    {
        if (await repositorio.ModoAsync(ct) == ModoLiquidacion.Ciclos) return Resultado<PagoLiquidacionRegistro>.Conflicto(Mensajes.PagosEnCiclos);
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
        if (notificaciones is not null)
            notificaciones.Publicar(await AvisoPagoAsync(registro, ct));
        return Resultado<PagoLiquidacionRegistro>.Ok(registro);
    }

    /// <summary>
    /// Aviso de un pago registrado, con los nombres de quien paga y quien recibe. No se excluye a nadie: la API no sabe quién lo
    /// anota, así que lo reciben también los dos implicados.
    /// </summary>
    private async Task<NotificacionPush> AvisoPagoAsync(PagoLiquidacionRegistro registro, CancellationToken ct)
    {
        var miembros = await repositorio.MiembrosAsync(ct);
        var de = miembros.FirstOrDefault(m => m.Id == registro.DeMiembroId)?.Nombre ?? "Alguien";
        var a = miembros.FirstOrDefault(m => m.Id == registro.AMiembroId)?.Nombre ?? "alguien";
        return new NotificacionPush(
            registro.HogarId, null, "Pago registrado", $"{de} pagó {FormatoAvisos.Importe(registro.Importe)} € a {a}", "/");
    }

    /// <summary>Borra un pago de liquidación; no encontrado si no existe en el hogar.</summary>
    public async Task<Resultado<bool>> EliminarPagoAsync(Guid id, CancellationToken ct)
        => await repositorio.EliminarPagoAsync(id, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();
}
