using MiParte.Core.Application;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Application.Periodos;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Web.Demo;

/// <summary>
/// Implementaciones en memoria de los puertos de <c>Core.Application</c> sobre el <see cref="AlmacenDemo"/>. Hacen lo mismo que los
/// repositorios EF de <c>Core.Infrastructure</c> sin base de datos: Â«guardarÂ» es aplicar los cambios pendientes a las listas.
/// </summary>
internal static class RepositoriosEnMemoria
{
    /// <summary>HistÃ³rico de la cuenta comÃºn hasta el fin del mes, con los mismos recortes que la consulta SQL agrupada.</summary>
    internal static HistoricoCuenta Historico(AlmacenDemo a, DateOnly inicio)
    {
        var fin = inicio.AddMonths(1);
        var gastos = a.Gastos.Where(g => g.ACargoCuentaComun && g.Fecha < fin).ToList();
        var gastosDelMes = gastos.Where(g => g.Fecha >= inicio).ToList();
        return new HistoricoCuenta(
            a.Aportaciones.Select(x => new AportacionVigente(x.MiembroId, x.Desde, x.Importe, x.Ahorro, a.Hogar.AportacionFrecuencia)).ToList(),
            gastos.Select(g => new GastoDeCuenta(g.PagadoPor, g.Fecha, g.Importe, g.PagadoDesdeAhorro)).ToList(),
            a.Reembolsos.Where(r => r.Fecha < fin).Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe, r.DesdeAhorro)).ToList(),
            a.Retiradas.Where(r => r.Fecha < fin).Select(r => new RetiradaDeAhorro(r.Fecha, r.Importe)).ToList(),
            a.Depositos.Where(d => d.Fecha < fin).Select(d => new DepositoDeAhorro(d.Fecha, d.Importe, d.MiembroId)).ToList(),
            a.Hogar.CuentaComunActiva,
            gastosDelMes.Sum(g => g.Importe),
            gastosDelMes.Select(g => new GastoDeCuenta(g.PagadoPor, g.Fecha, g.Importe, g.PagadoDesdeAhorro)).ToList(),
            a.Hogar.SaldoInicialFecha is { } f && a.Hogar.SaldoInicialImporte is { } i ? new SaldoInicialCuenta(f, i) : null, a.Hogar.AportacionFrecuencia,
            a.Ingresos.Where(x => x.Fecha < fin).Select(x => new IngresoDeCuenta(x.Fecha, x.Importe)).ToList(),
            a.Deudas.Where(d => d.Fecha < fin).Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro)).ToList());
    }

    /// <summary>Datos del ahorro; <paramref name="excluirGasto"/> deja fuera un gasto que se estÃ¡ editando.</summary>
    internal static DatosAhorro Ahorro(AlmacenDemo a, Guid? excluirGasto) => new(
        a.Aportaciones.Select(x => new AportacionVigente(x.MiembroId, x.Desde, x.Importe, x.Ahorro, a.Hogar.AportacionFrecuencia)).ToList(),
        a.Depositos.Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToList(),
        a.Retiradas.Sum(r => r.Importe),
        a.Gastos.Where(g => g.PagadoDesdeAhorro && g.Id != excluirGasto).Sum(g => g.Importe)
            + AhorroCuenta.UsosPorDeudas(a.Deudas.Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto))).Sum(u => u.Importe));
}

/// <summary>Resumen, liquidaciÃ³n y pagos en memoria.</summary>
internal sealed class RepositorioLiquidacionEnMemoria(AlmacenDemo a) : IRepositorioLiquidacion
{
    public Task<IReadOnlyList<Gasto>> GastosDelMesAsync(DateOnly inicio, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Gasto>>(a.Gastos
            .Where(g => g.Fecha >= inicio && g.Fecha < inicio.AddMonths(1) && ReglasPrivacidad.EsVisible(g.EsPersonal, g.PagadoPor, a.MiembroActual))
            .ToList());

    public Task<IReadOnlyList<Miembro>> MiembrosAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Miembro>>(a.Miembros.ToList());

    public Task<IReadOnlyDictionary<Guid, string>> NombresCategoriasAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(a.Categorias.ToDictionary(c => c.Id, c => c.Nombre));

    public Task<HistoricoCuenta> HistoricoCuentaAsync(DateOnly inicio, CancellationToken ct)
        => Task.FromResult(RepositoriosEnMemoria.Historico(a, inicio));

    public Task<IReadOnlyList<PagoLiquidacionRegistro>> PagosDelMesAsync(DateOnly inicio, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PagoLiquidacionRegistro>>(
            a.Pagos.Where(p => p.Mes == inicio).OrderBy(p => p.Fecha).ThenBy(p => p.Id).ToList());

    public Task<bool> ExistenAmbosAsync(Guid uno, Guid otro, CancellationToken ct)
        => Task.FromResult(a.Miembros.Any(m => m.Id == uno) && a.Miembros.Any(m => m.Id == otro));

    public Task AnadirPagoAsync(PagoLiquidacionRegistro pago, CancellationToken ct)
    {
        a.Pagos.Add(pago);
        return Task.CompletedTask;
    }

    public Task<bool> EliminarPagoAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Pagos.RemoveAll(p => p.Id == id) > 0);

    public Task<ModoLiquidacion> ModoAsync(CancellationToken ct) => Task.FromResult(a.Hogar.LiquidacionModo);

    public Task EstablecerModoAsync(ModoLiquidacion modo, CancellationToken ct)
    {
        a.Hogar.LiquidacionModo = modo;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Gasto>> GastosPendientesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Gasto>>(a.Gastos.Where(g => g.CicloLiquidacionId is null).ToList());

    public Task<int> UltimoNumeroCicloAsync(CancellationToken ct) => Task.FromResult(a.Ciclos.Count == 0 ? 0 : a.Ciclos.Max(c => c.Numero));

    public Task CerrarCicloAsync(CicloLiquidacion ciclo, IReadOnlyList<Guid> gastos, CancellationToken ct)
    {
        a.Ciclos.Add(ciclo);
        foreach (var g in a.Gastos.Where(g => gastos.Contains(g.Id))) g.CicloLiquidacionId = ciclo.Id;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CicloConAutor>> ListarCiclosAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CicloConAutor>>(a.Ciclos.OrderByDescending(c => c.Numero)
            .Select(c => new CicloConAutor(c, a.Miembros.FirstOrDefault(m => m.Id == c.CerradoPor)?.Nombre)).ToList());

    /// <summary>En memoria no hay concurrencia que serializar: la operación se ejecuta tal cual.</summary>
    public Task<T> EnExclusionAsync<T>(Func<Task<T>> operacion, CancellationToken ct) => operacion();
}

/// <summary>Gastos en memoria.</summary>
internal sealed class RepositorioGastosEnMemoria(AlmacenDemo a) : IRepositorioGastos
{
    private readonly List<Gasto> _porAnadir = [];
    private readonly List<Gasto> _porEliminar = [];

    public Task<IReadOnlyList<Gasto>> ListarAsync(FiltroGastos f, CancellationToken ct)
    {
        var q = a.Gastos.Where(g => ReglasPrivacidad.EsVisible(g.EsPersonal, g.PagadoPor, a.MiembroActual));
        if (f.Desde is { } desde) q = q.Where(g => g.Fecha >= desde);
        if (f.Hasta is { } hasta) q = q.Where(g => g.Fecha <= hasta);
        if (f.CategoriaId is { } c) q = q.Where(g => g.CategoriaId == c);
        if (f.MiembroId is { } m) q = q.Where(g => g.PagadoPor == m || g.Repartos.Any(r => r.MiembroId == m && r.ImporteAsumido > 0m));
        if (!string.IsNullOrEmpty(f.Texto))
            q = q.Where(g => g.Concepto?.Contains(f.Texto, StringComparison.OrdinalIgnoreCase) == true);
        return Task.FromResult<IReadOnlyList<Gasto>>(q.OrderByDescending(g => g.Fecha).ThenBy(g => g.Id).ToList());
    }

    public Task<Gasto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Gastos.FirstOrDefault(g =>
        g.Id == id && ReglasPrivacidad.EsVisible(g.EsPersonal, g.PagadoPor, a.MiembroActual)));

    public Task<Gasto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

    /// <summary>En memoria no hay concurrencia que serializar: la operación se ejecuta tal cual.</summary>
    public Task<T> EnExclusionAsync<T>(Func<Task<T>> operacion, CancellationToken ct) => operacion();

    public Task<bool> CategoriaExisteAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Categorias.Any(c => c.Id == id));

    public Task<string?> NombreCategoriaAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Categorias.FirstOrDefault(c => c.Id == id)?.Nombre);

    public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Miembros.Any(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto));

    public Task<PerfilReparto?> ObtenerPerfilAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Perfiles.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Miembro>> AdultosActivosAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Miembro>>(a.AdultosActivos());

    public Task<IReadOnlyList<Miembro>> MiembrosActivosAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Miembro>>(a.Miembros.Where(m => m.Activo).OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToList());

    public Task<bool> CuentaComunActivaAsync(CancellationToken ct) => Task.FromResult(a.Hogar.CuentaComunActiva);

    public Task<bool> AhorroActivoAsync(CancellationToken ct) => Task.FromResult(a.Hogar.AhorroActivo);

    public Task<DatosAhorro> DatosAhorroAsync(Guid? excluirGasto, CancellationToken ct)
        => Task.FromResult(RepositoriosEnMemoria.Ahorro(a, excluirGasto));

    public Task<bool> MesCerradoAsync(DateOnly fecha, CancellationToken ct)
    {
        var inicio = CuentaComun.InicioMes(fecha);
        return Task.FromResult(a.Cierres.Any(c => c.Mes == inicio));
    }

    /// <inheritdoc />
    public Task<bool> FechaEnPeriodoCerradoAsync(DateOnly fecha, CancellationToken ct)
        => Task.FromResult(a.Hogar.PeriodoCerradoEn is not null && a.Hogar.PeriodoDesde is { } desde && a.Hogar.PeriodoHasta is { } hasta
            && desde <= fecha && fecha <= hasta);

    public void Anadir(Gasto gasto) => _porAnadir.Add(gasto);

    public void Eliminar(Gasto gasto) => _porEliminar.Add(gasto);

    public Task<bool> GuardarAsync(CancellationToken ct)
    {
        a.Gastos.AddRange(_porAnadir);
        foreach (var g in _porEliminar) a.Gastos.Remove(g);
        _porAnadir.Clear();
        _porEliminar.Clear();
        return Task.FromResult(true);
    }
}

/// <summary>Cuenta comÃºn y ahorro en memoria.</summary>
internal sealed class RepositorioCuentaComunEnMemoria(AlmacenDemo a) : IRepositorioCuentaComun
{
    public Task<bool> CuentaActivaAsync(CancellationToken ct) => Task.FromResult(a.Hogar.CuentaComunActiva);

    public Task<bool> AhorroActivoAsync(CancellationToken ct) => Task.FromResult(a.Hogar.AhorroActivo);

    public Task EstablecerCuentaActivaAsync(bool activa, CancellationToken ct)
    {
        a.Hogar.CuentaComunActiva = activa;
        return Task.CompletedTask;
    }

    public Task EstablecerAhorroActivoAsync(bool activo, CancellationToken ct)
    {
        a.Hogar.AhorroActivo = activo;
        return Task.CompletedTask;
    }

    public Task<HistoricoCuentaDetallado> HistoricoDetalladoAsync(DateOnly inicio, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        static MovimientoCuenta M(Guid id, Guid miembro, DateOnly fecha, decimal importe, string? concepto)
            => new(id, miembro, fecha, importe, concepto);

        return Task.FromResult(new HistoricoCuentaDetallado(
            RepositoriosEnMemoria.Historico(a, inicio), a.Hogar.AhorroActivo,
            a.Aportaciones.OrderBy(x => x.MiembroId).ThenBy(x => x.Desde)
                .Select(x => new AportacionRegistrada(x.Id, x.MiembroId, x.Desde, x.Importe, x.Ahorro)).ToList(),
            a.Reembolsos.Where(r => r.Fecha >= inicio && r.Fecha < fin).OrderBy(r => r.Fecha).ThenBy(r => r.Id)
                .Select(r => M(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList(),
            a.Retiradas.Where(r => r.Fecha >= inicio && r.Fecha < fin).OrderBy(r => r.Fecha).ThenBy(r => r.Id)
                .Select(r => M(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList(),
            a.Depositos.Where(d => d.Fecha >= inicio && d.Fecha < fin).OrderBy(d => d.Fecha).ThenBy(d => d.Id)
                .Select(d => M(d.Id, d.MiembroId, d.Fecha, d.Importe, d.Concepto)).ToList(),
            a.Nombres(),
            a.Deudas.Where(d => d.Fecha >= inicio && d.Fecha < fin).OrderBy(d => d.Fecha).ThenBy(d => d.Tipo).ThenBy(d => d.Id).Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto)).ToList()));
    }

    public async Task<DatosMovimientosBanco> MovimientosBancoAsync(DateOnly hasta, CancellationToken ct)
    {
        static MovimientoCuenta M(Guid id, Guid miembro, DateOnly fecha, decimal importe, string? concepto)
            => new(id, miembro, fecha, importe, concepto);

        return new DatosMovimientosBanco(
            await AportacionesAsync(ct), await SaldoInicialAsync(ct),
            a.Gastos.Where(g => g.PagadoPor is null && g.Fecha <= hasta)
                .Select(g => new GastoPagadoPorCuenta(g.Id, g.Fecha, g.Importe, g.CategoriaId, g.Concepto, g.PagadoDesdeAhorro)).ToList(),
            a.Reembolsos.Where(x => x.Fecha <= hasta).Select(x => M(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToList(),
            a.Ingresos.Where(x => x.Fecha <= hasta).Select(x => M(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToList(),
            a.Depositos.Where(x => x.Fecha <= hasta).Select(x => M(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToList(),
            a.Retiradas.Where(x => x.Fecha <= hasta).Select(x => M(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToList(),
            a.Gastos.Where(g => g.ACargoCuentaComun && g.PagadoPor is not null && g.PagadoDesdeAhorro && g.Fecha <= hasta)
                .Select(g => new GastoPagadoPorCuenta(g.Id, g.Fecha, g.Importe, g.CategoriaId, g.Concepto, true, g.PagadoPor)).ToList(),
            Deudas: a.Deudas.Where(d => d.Fecha <= hasta).Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto)).ToList());
    }

    public Task<IReadOnlyList<MovimientoDeuda>> DeudasAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MovimientoDeuda>>(
            a.Deudas.Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto)).ToList());

    public Task<MovimientoDeuda?> DeudaAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Deudas.Where(d => d.Id == id).Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto)).FirstOrDefault());

    public Task<bool> EliminarDeudaAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Deudas.RemoveAll(d => d.Id == id) > 0);

    public Task GuardarDeudaAsync(MovimientoDeudaCuenta deuda, CancellationToken ct)
    {
        a.Deudas.Add(deuda);
        return Task.CompletedTask;
    }

    public Task<SaldoInicialCuenta?> SaldoInicialAsync(CancellationToken ct)
        => Task.FromResult(a.Hogar.SaldoInicialFecha is { } f && a.Hogar.SaldoInicialImporte is { } i ? new SaldoInicialCuenta(f, i) : null);

    public Task EstablecerSaldoInicialAsync(SaldoInicialCuenta? saldo, CancellationToken ct)
    {
        a.Hogar.SaldoInicialFecha = saldo?.Fecha;
        a.Hogar.SaldoInicialImporte = saldo?.Importe;
        return Task.CompletedTask;
    }

    public Task<bool> ExisteMiembroAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Miembros.Any(m => m.Id == id));

    public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Miembros.Any(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto));

    public Task<IReadOnlyList<AportacionVigente>> AportacionesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AportacionVigente>>(
            a.Aportaciones.Select(x => new AportacionVigente(x.MiembroId, x.Desde, x.Importe, x.Ahorro, a.Hogar.AportacionFrecuencia)).ToList());

    public Task<FrecuenciaAportacion> FrecuenciaAsync(CancellationToken ct) => Task.FromResult(a.Hogar.AportacionFrecuencia);

    public Task EstablecerFrecuenciaAsync(FrecuenciaAportacion frecuencia, CancellationToken ct)
    {
        a.Hogar.AportacionFrecuencia = frecuencia;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DepositoDeAhorro>> DepositosAsync(Guid? excluir, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DepositoDeAhorro>>(
            a.Depositos.Where(d => d.Id != excluir).Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToList());

    public Task<IReadOnlyList<UsoDeAhorro>> UsosDeAhorroAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<UsoDeAhorro>>(
            a.Retiradas.Select(r => new UsoDeAhorro(r.Fecha, r.Importe))
                .Concat(a.Gastos.Where(g => g.PagadoDesdeAhorro).Select(g => new UsoDeAhorro(g.Fecha, g.Importe)))
                .Concat(AhorroCuenta.UsosPorDeudas(a.Deudas.Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto)))).ToList());

    public Task<DatosAhorro> DatosAhorroAsync(CancellationToken ct) => Task.FromResult(RepositoriosEnMemoria.Ahorro(a, null));

    public Task<decimal> GastadoACargoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct)
        => Task.FromResult(a.Gastos.Where(g => g.ACargoCuentaComun && g.PagadoPor == miembroId && g.PagadoDesdeAhorro == desdeAhorro)
            .Sum(g => g.Importe));

    public Task<decimal> ReembolsadoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct)
        => Task.FromResult(a.Reembolsos.Where(r => r.MiembroId == miembroId && r.DesdeAhorro == desdeAhorro).Sum(r => r.Importe));

    public Task<AportacionCuenta> FijarAportacionAsync(
        Guid hogarId, Guid miembroId, DateOnly desde, decimal importe, decimal ahorro, CancellationToken ct)
    {
        var x = a.Aportaciones.FirstOrDefault(p => p.MiembroId == miembroId && p.Desde == desde);
        if (x is null)
        {
            x = new AportacionCuenta { Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = miembroId, Desde = desde };
            a.Aportaciones.Add(x);
        }

        x.Importe = importe;
        x.Ahorro = ahorro;
        return Task.FromResult(x);
    }

    public Task GuardarReembolsoAsync(ReembolsoCuenta reembolso, CancellationToken ct)
    {
        a.Reembolsos.Add(reembolso);
        return Task.CompletedTask;
    }

    public Task<bool> EliminarReembolsoAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Reembolsos.RemoveAll(r => r.Id == id) > 0);

    public Task GuardarIngresoAsync(IngresoCuentaComun ingreso, CancellationToken ct)
    {
        a.Ingresos.Add(ingreso);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IngresoCuentaComun>> IngresosAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<IngresoCuentaComun>>(a.Ingresos.OrderByDescending(i => i.Fecha).ThenBy(i => i.Id).ToList());

    public Task<bool> EliminarIngresoAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Ingresos.RemoveAll(i => i.Id == id) > 0);

    public Task GuardarDepositoAsync(DepositoAhorro deposito, CancellationToken ct)
    {
        a.Depositos.Add(deposito);
        return Task.CompletedTask;
    }

    public Task<bool> ExisteDepositoAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Depositos.Any(d => d.Id == id));

    public Task<bool> EliminarDepositoAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Depositos.RemoveAll(d => d.Id == id) > 0);

    public Task GuardarRetiradaAsync(RetiradaAhorro retirada, CancellationToken ct)
    {
        a.Retiradas.Add(retirada);
        return Task.CompletedTask;
    }

    public Task<bool> EliminarRetiradaAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Retiradas.RemoveAll(r => r.Id == id) > 0);
}

/// <summary>Miembros e invitaciones en memoria. Aceptar una invitaciÃ³n no estÃ¡ disponible en la demo.</summary>
internal sealed class RepositorioMiembrosEnMemoria(AlmacenDemo a) : IRepositorioMiembros
{
    public Task<IReadOnlyList<Miembro>> ListarAsync(bool incluirInactivos, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Miembro>>(
            a.Miembros.Where(m => incluirInactivos || m.Activo).OrderBy(m => m.Nombre).ToList());

    public Task<Miembro?> MiembroActivoDeUsuarioAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(a.Miembros.FirstOrDefault(m => m.UserId == userId && m.Activo));

    public Task<Miembro?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Miembros.FirstOrDefault(m => m.Id == id));

    public Task AnadirAsync(Miembro miembro, CancellationToken ct)
    {
        a.Miembros.Add(miembro);
        return Task.CompletedTask;
    }

    public Task<bool> HayOtroAdminActivoAsync(Guid excluirId, CancellationToken ct)
        => Task.FromResult(a.Miembros.Any(x => x.Id != excluirId && x.Rol == RolMiembro.Admin && x.Activo && x.UserId != null));

    public Task<IReadOnlyList<Miembro>> OtrosConCuentaActivosAsync(Guid excluirId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Miembro>>(a.Miembros.Where(x => x.Id != excluirId && x.Activo && x.UserId != null).ToList());

    public Task<bool> TieneACargoActivosAsync(Guid responsableId, CancellationToken ct)
        => Task.FromResult(a.Miembros.Any(x => x.ResponsableId == responsableId && x.Activo));

    public Task GuardarAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<Resultado<T>> EjecutarExclusivoAsync<T>(Guid hogarId, Func<Task<Resultado<T>>> operacion, CancellationToken ct)
        => operacion();

    public Task GuardarInvitacionAsync(InvitacionHogar invitacion, CancellationToken ct)
    {
        a.Invitaciones.Add(invitacion);
        return Task.CompletedTask;
    }

    public Task<InvitacionHogar?> InvitacionPorHashAsync(string hash, CancellationToken ct)
        => Task.FromResult(a.Invitaciones.FirstOrDefault(i => i.TokenHash == hash));

    public Task<bool> UsuarioEnHogarAsync(Guid hogarId, Guid userId, CancellationToken ct)
        => Task.FromResult(a.Miembros.Any(m => m.UserId == userId));

    public Task<Miembro?> MiembroDeHogarAsync(Guid hogarId, Guid miembroId, CancellationToken ct) => ObtenerAsync(miembroId, ct);

    public Task<ResultadoConsumo> ConsumirInvitacionAsync(
        InvitacionHogar invitacion, Guid userId, Miembro? nuevo, DateTimeOffset ahora, CancellationToken ct)
        => throw new NotSupportedException("En el modo demo no se pueden aceptar invitaciones.");

    public Task<Hogar> HogarAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Hogar);
}

/// <summary>Plantillas de gasto recurrente en memoria.</summary>
internal sealed class RepositorioRecurrentesEnMemoria(AlmacenDemo a) : IRepositorioRecurrentes
{
    private readonly List<GastoRecurrente> _porAnadir = [];
    private readonly List<GastoRecurrente> _porEliminar = [];

    public Task<bool> RecurrentesActivasAsync(CancellationToken ct) => Task.FromResult(a.Hogar.RecurrentesActivas);

    public Task EstablecerRecurrentesActivasAsync(bool activas, CancellationToken ct)
    {
        a.Hogar.RecurrentesActivas = activas;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GastoRecurrente>> ListarAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<GastoRecurrente>>(a.Recurrentes
            .Where(r => ReglasPrivacidad.EsVisible(r.EsPersonal, r.PagadoPor, a.MiembroActual))
            .OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToList());

    public Task<GastoRecurrente?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Recurrentes.FirstOrDefault(r =>
        r.Id == id && ReglasPrivacidad.EsVisible(r.EsPersonal, r.PagadoPor, a.MiembroActual)));

    public Task<GastoRecurrente?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

    public void Anadir(GastoRecurrente plantilla) => _porAnadir.Add(plantilla);

    public void Eliminar(GastoRecurrente plantilla) => _porEliminar.Add(plantilla);

    public Task GuardarAsync(CancellationToken ct)
    {
        a.Recurrentes.AddRange(_porAnadir);
        foreach (var r in _porEliminar) a.Recurrentes.Remove(r);
        _porAnadir.Clear();
        _porEliminar.Clear();
        return Task.CompletedTask;
    }

    public Task<bool> TieneGastosGeneradosAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Gastos.Any(g => g.GastoRecurrenteId == id));

    public Task<IReadOnlyList<GastoRecurrente>> ActivasAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<GastoRecurrente>>(a.Recurrentes.Where(r => r.Activo).OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToList());

    public Task<IReadOnlySet<Guid>> GeneradasEnElMesAsync(DateOnly inicio, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        IReadOnlySet<Guid> ids = a.Gastos.Where(g => g.GastoRecurrenteId != null && g.Fecha >= inicio && g.Fecha < fin)
            .Select(g => g.GastoRecurrenteId!.Value).ToHashSet();
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyDictionary<Guid, PerfilReparto>> PerfilesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<Guid, PerfilReparto>>(a.Perfiles.ToDictionary(p => p.Id));

    public Task<ResultadoGuardadoGenerados> GuardarGeneradosAsync(IReadOnlyList<Gasto> gastos, CancellationToken ct)
    {
        a.Gastos.AddRange(gastos);
        return Task.FromResult(ResultadoGuardadoGenerados.Guardado);
    }
}

/// <summary>Perfiles de reparto en memoria.</summary>
internal sealed class RepositorioPerfilesEnMemoria(AlmacenDemo a) : IRepositorioPerfiles
{
    private readonly List<PerfilReparto> _porAnadir = [];
    private readonly List<PerfilReparto> _porEliminar = [];

    public Task<IReadOnlyList<PerfilReparto>> ListarAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PerfilReparto>>(a.Perfiles.OrderBy(p => p.Nombre).ToList());

    public Task<PerfilReparto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Perfiles.FirstOrDefault(p => p.Id == id));

    public Task<PerfilReparto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

    public Task<int> ContarParticipantesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var adultos = a.Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).Select(m => m.Id).ToHashSet();
        return Task.FromResult(a.Miembros.Count(m => m.Activo && ids.Contains(m.Id)
            && (m.Tipo == TipoMiembro.Adulto || (m.ResponsableId is { } r && adultos.Contains(r)))));
    }

    public Task<bool> NombreEnUsoAsync(string nombre, Guid? excluirId, CancellationToken ct)
        => Task.FromResult(a.Perfiles.Any(p => p.Nombre == nombre && p.Id != excluirId));

    public Task<bool> OtroPredeterminadoGastosAsync(Guid? excluirId, CancellationToken ct)
        => Task.FromResult(a.Perfiles.Any(p => p.PredeterminadoGastos && p.Id != excluirId));

    public Task<bool> EnUsoAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Categorias.Any(c => c.PerfilRepartoId == id) || a.Gastos.Any(g => g.PerfilRepartoId == id)
            || a.Recurrentes.Any(r => r.PerfilRepartoId == id));

    public Task ReasignarUsoAsync(Guid origen, Guid destino, CancellationToken ct)
    {
        foreach (var c in a.Categorias.Where(c => c.PerfilRepartoId == origen)) c.PerfilRepartoId = destino;
        foreach (var g in a.Gastos.Where(g => g.PerfilRepartoId == origen)) g.PerfilRepartoId = destino;
        foreach (var r in a.Recurrentes.Where(r => r.PerfilRepartoId == origen)) r.PerfilRepartoId = destino;
        return Task.CompletedTask;
    }

    public void Anadir(PerfilReparto perfil) => _porAnadir.Add(perfil);

    public void ReemplazarDetalle(PerfilReparto perfil, IReadOnlyList<PerfilRepartoDetalle> nuevo)
    {
        perfil.Detalles.Clear();
        perfil.Detalles.AddRange(nuevo);
    }

    public void Eliminar(PerfilReparto perfil) => _porEliminar.Add(perfil);

    public Task<bool> GuardarAsync(CancellationToken ct)
    {
        a.Perfiles.AddRange(_porAnadir);
        foreach (var p in _porEliminar) a.Perfiles.Remove(p);
        _porAnadir.Clear();
        _porEliminar.Clear();
        return Task.FromResult(true);
    }
}

/// <summary>CategorÃ­as en memoria.</summary>
internal sealed class RepositorioCategoriasEnMemoria(AlmacenDemo a) : IRepositorioCategorias
{
    private readonly List<Categoria> _porAnadir = [];
    private readonly List<Categoria> _porEliminar = [];

    public Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Categoria>>(a.Categorias.OrderBy(c => c.Nombre).ToList());

    public Task<Categoria?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Categorias.FirstOrDefault(c => c.Id == id));

    public Task<PerfilReparto?> PerfilAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Perfiles.FirstOrDefault(p => p.Id == id));

    public Task<PerfilReparto?> PerfilDeCuentaComunAsync(CancellationToken ct)
        => Task.FromResult(a.Perfiles.Where(p => p.Modo == ModoReparto.CuentaComun).OrderBy(p => p.Nombre).ThenBy(p => p.Id).FirstOrDefault());

    public Task<bool> EstaACargoDeLaCuentaAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Categorias.Any(c => c.Id == id && c.ACargoCuentaComun));

    public Task<IReadOnlyDictionary<Guid, Guid?>> PadresAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(a.Categorias.ToDictionary(c => c.Id, c => c.CategoriaPadreId));

    public Task<bool> NombreEnNivelAsync(Guid? padreId, string nombre, Guid? excluirId, CancellationToken ct)
        => Task.FromResult(a.Categorias.Any(c => c.CategoriaPadreId == padreId && c.Nombre == nombre && c.Id != excluirId));

    public Task<bool> OtraPredeterminadaGastosAsync(Guid? excluirId, CancellationToken ct)
        => Task.FromResult(a.Categorias.Any(c => c.PredeterminadoGastos && c.Id != excluirId));

    public Task<bool> TieneSubcategoriasAsync(Guid id, CancellationToken ct) => Task.FromResult(a.Categorias.Any(c => c.CategoriaPadreId == id));

    public Task<bool> TieneGastosAsync(Guid id, CancellationToken ct)
        => Task.FromResult(a.Gastos.Any(g => g.CategoriaId == id) || a.Recurrentes.Any(r => r.CategoriaId == id));

    public Task ReasignarGastosAsync(Guid origen, Guid destino, CancellationToken ct)
    {
        foreach (var g in a.Gastos.Where(g => g.CategoriaId == origen)) g.CategoriaId = destino;
        foreach (var r in a.Recurrentes.Where(r => r.CategoriaId == origen)) r.CategoriaId = destino;
        return Task.CompletedTask;
    }

    public void Anadir(Categoria categoria) => _porAnadir.Add(categoria);

    public void Eliminar(Categoria categoria) => _porEliminar.Add(categoria);

    public Task<bool> GuardarAsync(CancellationToken ct)
    {
        a.Categorias.AddRange(_porAnadir);
        foreach (var c in _porEliminar) a.Categorias.Remove(c);
        _porAnadir.Clear();
        _porEliminar.Clear();
        return Task.FromResult(true);
    }
}

/// <summary>Meses cerrados en memoria.</summary>
internal sealed class RepositorioCierresEnMemoria(AlmacenDemo a) : IRepositorioCierres
{
    public Task<IReadOnlyList<MesCerrado>> ListarAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MesCerrado>>(a.Cierres.OrderByDescending(c => c.Mes).ToList());

    public Task<IReadOnlyDictionary<Guid, string>> NombresPorUsuarioAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            a.Miembros.Where(m => m.UserId != null).ToDictionary(m => m.UserId!.Value, m => m.Nombre));

    public Task<bool> EstaCerradoAsync(DateOnly inicio, CancellationToken ct) => Task.FromResult(a.Cierres.Any(c => c.Mes == inicio));

    public Task<bool> GuardarAsync(MesCerrado cierre, CancellationToken ct)
    {
        a.Cierres.Add(cierre);
        return Task.FromResult(true);
    }

    public Task<bool> EliminarAsync(DateOnly inicio, CancellationToken ct) => Task.FromResult(a.Cierres.RemoveAll(c => c.Mes == inicio) > 0);
}

/// <summary>Periodo propio del hogar en memoria, para cerrarlo y reabrirlo en la demo.</summary>
internal sealed class RepositorioPeriodosEnMemoria(AlmacenDemo a) : IRepositorioPeriodos
{
    /// <inheritdoc />
    public Task<Hogar?> HogarAsync(CancellationToken ct) => Task.FromResult<Hogar?>(a.Hogar);

    /// <inheritdoc />
    public Task GuardarAsync(CancellationToken ct) => Task.CompletedTask;
}
