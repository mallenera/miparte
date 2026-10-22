using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.CuentaComun;

/// <summary>Estado de la cuenta común al final de un mes, con lo que muestra la pantalla de la cuenta.</summary>
/// <param name="Mes">Primer día del mes.</param>
/// <param name="Estado">Saldos, pendientes y ahorro acumulados hasta el fin del mes.</param>
/// <param name="Partes">«Su parte» de cada persona.</param>
/// <param name="Detalle">Aportaciones, movimientos del mes y nombres de los miembros.</param>
/// <param name="Extracto">Cómo cambia el efectivo de la cuenta en el mes, como un extracto bancario.</param>
/// <param name="SaldoInicial">Saldo inicial fijado para la cuenta, o null.</param>
public sealed record EstadoCuentaDelMes(
    DateOnly Mes, EstadoCuentaComun Estado, IReadOnlyList<PartePersona> Partes, HistoricoCuentaDetallado Detalle,
    ExtractoCuentaComun Extracto, SaldoInicialCuenta? SaldoInicial);

/// <summary>Datos de una aportación mensual de un adulto.</summary>
public sealed record FijarAportacion(Guid HogarId, Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro);

/// <summary>Datos de un movimiento suelto (reembolso, depósito o retirada).</summary>
/// <param name="HogarId">Hogar al que pertenece.</param>
/// <param name="MiembroId">Miembro al que se refiere.</param>
/// <param name="Importe">Importe del movimiento.</param>
/// <param name="Fecha">Fecha; hoy si es null.</param>
/// <param name="Concepto">Nota opcional.</param>
public sealed record RegistrarMovimiento(Guid HogarId, Guid MiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);

/// <summary>Casos de uso de la cuenta común: aportaciones, saldo, reembolsos y movimientos del ahorro.</summary>
public sealed class ServicioCuentaComun(IRepositorioCuentaComun repositorio, TimeProvider reloj)
{
    /// <summary>Estado de la cuenta al final del mes que empieza en <paramref name="inicio"/>; se devuelve aunque la cuenta no esté activada.</summary>
    public async Task<EstadoCuentaDelMes> EstadoAsync(DateOnly inicio, CancellationToken ct)
    {
        var detalle = await repositorio.HistoricoDetalladoAsync(inicio, ct);
        var h = detalle.Historico;
        var saldoInicial = h.SaldoInicial;
        var estado = Domain.CuentaComun.Calcular(inicio, h.Vigentes, h.Gastos, h.Reembolsos, h.Retiradas, h.Depositos, saldoInicial, h.Ingresos);
        var partes = Domain.CuentaComun.PartesPorPersona(inicio, h.Vigentes, estado, h.Depositos);
        var reembolsos = detalle.ReembolsosDelMes.Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe)).ToList();
        var extracto = Domain.CuentaComun.Extracto(inicio, h.Vigentes, h.GastosDelMes ?? [], reembolsos, saldoInicial, estado, h.Ingresos);
        return new EstadoCuentaDelMes(inicio, estado, partes, detalle, extracto, saldoInicial);
    }

    /// <summary>
    /// Extracto bancario de la cuenta (gastos y ahorro juntos) entre dos fechas incluidas, línea a línea y con el saldo tras cada
    /// movimiento; se devuelve aunque la cuenta no esté activada. Inválido si <paramref name="desde"/> es posterior a <paramref name="hasta"/>.
    /// </summary>
    public async Task<Resultado<ExtractoBanco>> MovimientosBancoAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        if (desde > hasta) return Resultado<ExtractoBanco>.Invalido("La fecha «desde» no puede ser posterior a «hasta».");
        var datos = await repositorio.MovimientosBancoAsync(hasta, ct);
        return Resultado<ExtractoBanco>.Ok(MovimientosBanco.Calcular(desde, hasta, datos));
    }

    /// <summary>
    /// Fija el saldo inicial de la cuenta para gastos a partir de una fecha, sustituyendo el que hubiera. Es dinero que ya estaba en la
    /// cuenta, así que no puede ser negativo. Es un conflicto si la cuenta común no está activada.
    /// </summary>
    public async Task<Resultado<SaldoInicialCuenta>> FijarSaldoInicialAsync(DateOnly fecha, decimal importe, CancellationToken ct)
    {
        if (await ExigirActivaAsync<SaldoInicialCuenta>(ct) is { } inactiva) return inactiva;
        if (ReglasCuentaComun.ValidarSaldoInicial(importe) is { } error) return Resultado<SaldoInicialCuenta>.Invalido(error);

        var saldo = new SaldoInicialCuenta(fecha, importe);
        await repositorio.EstablecerSaldoInicialAsync(saldo, ct);
        return Resultado<SaldoInicialCuenta>.Ok(saldo);
    }

    /// <summary>Quita el saldo inicial de la cuenta; no encontrado si no hay ninguno.</summary>
    public async Task<Resultado<bool>> EliminarSaldoInicialAsync(CancellationToken ct)
    {
        if (await repositorio.SaldoInicialAsync(ct) is null) return Resultado<bool>.NoEncontrado();
        await repositorio.EstablecerSaldoInicialAsync(null, ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>Activa o desactiva la cuenta común; desactivarla no borra datos, solo bloquea nuevas escrituras.</summary>
    public async Task<bool> ActivarCuentaAsync(bool activa, CancellationToken ct)
    {
        await repositorio.EstablecerCuentaActivaAsync(activa, ct);
        return activa;
    }

    /// <summary>
    /// Cambia con qué frecuencia aportan los miembros. Afecta también a los cobros ya pasados (cada aportación hereda la frecuencia del
    /// hogar), así que es un conflicto si con la nueva frecuencia el ahorro ya retirado o gastado dejaría de tener respaldo.
    /// </summary>
    public async Task<Resultado<FrecuenciaAportacion>> CambiarFrecuenciaAsync(FrecuenciaAportacion frecuencia, CancellationToken ct)
    {
        var con = (await repositorio.AportacionesAsync(ct)).Select(a => a with { Frecuencia = frecuencia }).ToList();
        var falta = await HolguraAsync(con, await repositorio.DepositosAsync(null, ct), ct);
        if (falta is { } f) return SinRespaldo<FrecuenciaAportacion>("cambiar la frecuencia de las aportaciones", f);

        await repositorio.EstablecerFrecuenciaAsync(frecuencia, ct);
        return Resultado<FrecuenciaAportacion>.Ok(frecuencia);
    }

    /// <summary>Activa o desactiva el ahorro; exige la cuenta común activada.</summary>
    public async Task<Resultado<bool>> ActivarAhorroAsync(bool activo, CancellationToken ct)
    {
        if (await ExigirActivaAsync<bool>(ct) is { } inactiva) return inactiva;
        await repositorio.EstablecerAhorroActivoAsync(activo, ct);
        return Resultado<bool>.Ok(activo);
    }

    /// <summary>
    /// Fija la aportación de un adulto desde un mes, con la parte que va a ahorro (si ya había una de ese mes, la sustituye).
    /// Es inválida si el mes no es día 1, el importe o el ahorro no valen o el miembro no es adulto activo; es un conflicto si
    /// la rebaja del ahorro dejaría sin respaldo lo ya retirado o gastado desde el ahorro.
    /// </summary>
    public async Task<Resultado<AportacionCuenta>> FijarAportacionAsync(FijarAportacion a, CancellationToken ct)
    {
        if (await ExigirActivaAsync<AportacionCuenta>(ct) is { } inactiva) return inactiva;
        if (a.Desde.Day != 1) return Resultado<AportacionCuenta>.Invalido("El mes de la aportación debe ser el día 1 del mes.");
        // Una aportación de 0 es válida: deja de aportar desde ese mes.
        var error = ReglasCuentaComun.ValidarImporteAportacion(a.Importe) ?? ReglasCuentaComun.ValidarAhorroAportacion(a.Ahorro, a.Importe);
        if (error is not null) return Resultado<AportacionCuenta>.Invalido(error);
        if (a.Ahorro > 0 && await ExigirAhorroAsync<AportacionCuenta>(ct) is { } sinAhorro) return sinAhorro;
        if (!await repositorio.EsAdultoActivoAsync(a.MiembroId, ct))
            return Resultado<AportacionCuenta>.Invalido("Solo los adultos activos del hogar aportan a la cuenta común.");

        // Rebajar el ahorro no puede dejar sin respaldo lo que ya se retiró o se gastó desde el ahorro.
        var existentes = await repositorio.AportacionesAsync(ct);
        var tras = existentes.Where(x => !(x.MiembroId == a.MiembroId && x.Desde == a.Desde))
            .Append(new AportacionVigente(a.MiembroId, a.Desde, a.Importe, a.Ahorro, await repositorio.FrecuenciaAsync(ct))).ToList();
        var falta = await HolguraAsync(tras, await repositorio.DepositosAsync(null, ct), ct);
        if (falta is { } f) return SinRespaldo<AportacionCuenta>("rebajar el ahorro de esta aportación", f);

        return Resultado<AportacionCuenta>.Ok(
            await repositorio.FijarAportacionAsync(a.HogarId, a.MiembroId, a.Desde, a.Importe, a.Ahorro, ct));
    }

    /// <summary>
    /// Registra un pago de la cuenta a quien adelantó gastos. Inválido si el importe no vale o el miembro no es del hogar;
    /// conflicto si supera lo pendiente de reembolsarle.
    /// </summary>
    public async Task<Resultado<ReembolsoCuenta>> CrearReembolsoAsync(RegistrarMovimiento m, CancellationToken ct)
    {
        if (await ExigirActivaAsync<ReembolsoCuenta>(ct) is { } inactiva) return inactiva;
        var error = ReglasMovimiento.ValidarImporte(m.Importe) ?? ReglasMovimiento.ValidarConcepto(m.Concepto);
        if (error is not null) return Resultado<ReembolsoCuenta>.Invalido(error);
        if (!await repositorio.ExisteMiembroAsync(m.MiembroId, ct))
            return Resultado<ReembolsoCuenta>.Invalido("El miembro del reembolso debe pertenecer al hogar.");

        var pendiente = await repositorio.GastadoACargoAsync(m.MiembroId, ct) - await repositorio.ReembolsadoAsync(m.MiembroId, ct);
        if (m.Importe > pendiente)
        {
            var visible = Math.Max(0m, pendiente);
            return Resultado<ReembolsoCuenta>.Conflicto(
                $"El importe supera lo pendiente de reembolsar al miembro ({visible:0.00}).",
                new Dictionary<string, object> { ["pendiente"] = visible });
        }

        var r = new ReembolsoCuenta
        {
            Id = Guid.NewGuid(), HogarId = m.HogarId, MiembroId = m.MiembroId, Importe = m.Importe,
            Fecha = m.Fecha ?? Hoy, Concepto = ReglasMovimiento.NormalizarConcepto(m.Concepto),
        };
        await repositorio.GuardarReembolsoAsync(r, ct);
        return Resultado<ReembolsoCuenta>.Ok(r);
    }

    /// <summary>Borra un reembolso; no encontrado si no existe.</summary>
    public async Task<Resultado<bool>> EliminarReembolsoAsync(Guid id, CancellationToken ct)
        => await repositorio.EliminarReembolsoAsync(id, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();

    /// <summary>
    /// Registra dinero que entra a la cuenta común fuera de aportaciones y ahorro. Solo es histórico: no entra en los cálculos de saldo,
    /// ahorro ni reembolsos. Inválido si el importe no vale o el miembro no es del hogar; conflicto si la cuenta está inactiva.
    /// </summary>
    public async Task<Resultado<IngresoCuentaComun>> CrearIngresoAsync(RegistrarMovimiento m, CancellationToken ct)
    {
        if (await ExigirActivaAsync<IngresoCuentaComun>(ct) is { } inactiva) return inactiva;
        var error = ReglasMovimiento.ValidarImporte(m.Importe) ?? ReglasMovimiento.ValidarConcepto(m.Concepto);
        if (error is not null) return Resultado<IngresoCuentaComun>.Invalido(error);
        if (!await repositorio.ExisteMiembroAsync(m.MiembroId, ct))
            return Resultado<IngresoCuentaComun>.Invalido("El miembro del ingreso debe pertenecer al hogar.");

        var i = new IngresoCuentaComun
        {
            Id = Guid.NewGuid(), HogarId = m.HogarId, MiembroId = m.MiembroId, Importe = m.Importe,
            Fecha = m.Fecha ?? Hoy, Concepto = ReglasMovimiento.NormalizarConcepto(m.Concepto),
        };
        await repositorio.GuardarIngresoAsync(i, ct);
        return Resultado<IngresoCuentaComun>.Ok(i);
    }

    /// <summary>Ingresos a la cuenta común del hogar, del más reciente al más antiguo.</summary>
    public Task<IReadOnlyList<IngresoCuentaComun>> ListarIngresosAsync(CancellationToken ct) => repositorio.IngresosAsync(ct);

    /// <summary>Borra un ingreso a la cuenta común; no encontrado si no existe.</summary>
    public async Task<Resultado<bool>> EliminarIngresoAsync(Guid id, CancellationToken ct)
        => await repositorio.EliminarIngresoAsync(id, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();

    /// <summary>Registra dinero que entra al ahorro fuera de la aportación mensual. Inválido si el importe no vale o el miembro no es del hogar.</summary>
    public async Task<Resultado<DepositoAhorro>> CrearDepositoAsync(RegistrarMovimiento m, CancellationToken ct)
    {
        if (await ExigirAhorroAsync<DepositoAhorro>(ct) is { } inactivo) return inactivo;
        var error = ReglasMovimiento.ValidarImporte(m.Importe) ?? ReglasMovimiento.ValidarConcepto(m.Concepto);
        if (error is not null) return Resultado<DepositoAhorro>.Invalido(error);
        if (!await repositorio.ExisteMiembroAsync(m.MiembroId, ct))
            return Resultado<DepositoAhorro>.Invalido("El miembro del depósito debe pertenecer al hogar.");

        var d = new DepositoAhorro
        {
            Id = Guid.NewGuid(), HogarId = m.HogarId, MiembroId = m.MiembroId, Importe = m.Importe,
            Fecha = m.Fecha ?? Hoy, Concepto = ReglasMovimiento.NormalizarConcepto(m.Concepto),
        };
        await repositorio.GuardarDepositoAsync(d, ct);
        return Resultado<DepositoAhorro>.Ok(d);
    }

    /// <summary>Borra un depósito; no encontrado si no existe; conflicto si dejaría sin respaldo lo ya retirado o gastado desde el ahorro.</summary>
    public async Task<Resultado<bool>> EliminarDepositoAsync(Guid id, CancellationToken ct)
    {
        if (!await repositorio.ExisteDepositoAsync(id, ct)) return Resultado<bool>.NoEncontrado();

        // Sin este ingreso el ahorro no puede quedar por debajo de lo ya retirado o gastado desde él.
        var falta = await HolguraAsync(await repositorio.AportacionesAsync(ct), await repositorio.DepositosAsync(id, ct), ct);
        if (falta is { } f) return SinRespaldo<bool>("eliminar este ingreso", f);

        await repositorio.EliminarDepositoAsync(id, ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>Registra dinero que sale del ahorro. Inválido si el importe no vale o el miembro no es del hogar; conflicto si supera el ahorro disponible.</summary>
    public async Task<Resultado<RetiradaAhorro>> CrearRetiradaAsync(RegistrarMovimiento m, CancellationToken ct)
    {
        if (await ExigirAhorroAsync<RetiradaAhorro>(ct) is { } inactivo) return inactivo;
        var error = ReglasMovimiento.ValidarImporte(m.Importe) ?? ReglasMovimiento.ValidarConcepto(m.Concepto);
        if (error is not null) return Resultado<RetiradaAhorro>.Invalido(error);
        if (!await repositorio.ExisteMiembroAsync(m.MiembroId, ct))
            return Resultado<RetiradaAhorro>.Invalido("El miembro de la retirada debe pertenecer al hogar.");

        var fecha = m.Fecha ?? Hoy;
        // Lo ahorrado hasta el mes de la retirada, menos lo ya retirado o gastado desde el ahorro: nunca se saca más de lo que hay.
        var disponible = AhorroCuenta.Disponible(fecha, await repositorio.DatosAhorroAsync(ct));
        if (m.Importe > disponible)
        {
            var visible = Math.Max(0m, disponible);
            return Resultado<RetiradaAhorro>.Conflicto(
                $"El importe supera el ahorro disponible ({visible:0.00}).",
                new Dictionary<string, object> { ["disponible"] = visible });
        }

        var r = new RetiradaAhorro
        {
            Id = Guid.NewGuid(), HogarId = m.HogarId, MiembroId = m.MiembroId, Importe = m.Importe,
            Fecha = fecha, Concepto = ReglasMovimiento.NormalizarConcepto(m.Concepto),
        };
        await repositorio.GuardarRetiradaAsync(r, ct);
        return Resultado<RetiradaAhorro>.Ok(r);
    }

    /// <summary>Borra una retirada de ahorro; no encontrado si no existe.</summary>
    public async Task<Resultado<bool>> EliminarRetiradaAsync(Guid id, CancellationToken ct)
        => await repositorio.EliminarRetiradaAsync(id, ct) ? Resultado<bool>.Ok(true) : Resultado<bool>.NoEncontrado();

    private DateOnly Hoy => DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);

    /// <summary>Cuánto faltaría de ahorro con estas aportaciones y depósitos (null si queda cubierto todo lo ya usado).</summary>
    private async Task<decimal?> HolguraAsync(
        IReadOnlyList<AportacionVigente> aportaciones, IReadOnlyList<DepositoDeAhorro> depositos, CancellationToken ct)
    {
        var holgura = HolguraAhorro.Calcular(aportaciones, depositos, await repositorio.UsosDeAhorroAsync(ct));
        return holgura is < 0m ? -holgura : null;
    }

    /// <summary>Conflicto cuando un cambio dejaría el ahorro en negativo, con lo que faltaría.</summary>
    private static Resultado<T> SinRespaldo<T>(string accion, decimal falta)
        => Resultado<T>.Conflicto(
            $"No se puede {accion}: ya se ha retirado o gastado ese ahorro (faltarían {falta:0.00}).",
            new Dictionary<string, object> { ["falta"] = falta });

    /// <summary>El conflicto de cuenta inactiva, o null si el hogar la tiene activada.</summary>
    private async Task<Resultado<T>?> ExigirActivaAsync<T>(CancellationToken ct)
        => await repositorio.CuentaActivaAsync(ct) ? null : Resultado<T>.Conflicto(Mensajes.CuentaInactiva);

    /// <summary>El conflicto de cuenta o ahorro inactivos, o null si el hogar tiene ambos activados.</summary>
    private async Task<Resultado<T>?> ExigirAhorroAsync<T>(CancellationToken ct)
        => await ExigirActivaAsync<T>(ct)
            ?? (await repositorio.AhorroActivoAsync(ct) ? null : Resultado<T>.Conflicto(Mensajes.AhorroInactivo));
}
