using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioCuentaComun"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioCuentaComun(MiParteDbContext db) : IRepositorioCuentaComun
{
    /// <inheritdoc />
    public Task<bool> CuentaActivaAsync(CancellationToken ct) => db.Hogares.AnyAsync(h => h.CuentaComunActiva, ct);

    /// <inheritdoc />
    public Task<bool> AhorroActivoAsync(CancellationToken ct) => db.Hogares.AnyAsync(h => h.AhorroActivo, ct);

    /// <inheritdoc />
    public async Task EstablecerCuentaActivaAsync(bool activa, CancellationToken ct)
    {
        var h = await db.Hogares.FirstAsync(ct);
        h.CuentaComunActiva = activa;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<SaldoInicialCuenta?> SaldoInicialAsync(CancellationToken ct)
    {
        var h = await db.Hogares.Select(x => new { x.SaldoInicialFecha, x.SaldoInicialImporte }).FirstOrDefaultAsync(ct);
        return h?.SaldoInicialFecha is { } fecha && h.SaldoInicialImporte is { } importe ? new SaldoInicialCuenta(fecha, importe) : null;
    }

    /// <inheritdoc />
    public async Task EstablecerSaldoInicialAsync(SaldoInicialCuenta? saldo, CancellationToken ct)
    {
        var h = await db.Hogares.FirstAsync(ct);
        h.SaldoInicialFecha = saldo?.Fecha;
        h.SaldoInicialImporte = saldo?.Importe;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task EstablecerAhorroActivoAsync(bool activo, CancellationToken ct)
    {
        var h = await db.Hogares.FirstAsync(ct);
        h.AhorroActivo = activo;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<HistoricoCuentaDetallado> HistoricoDetalladoAsync(DateOnly inicio, CancellationToken ct)
    {
        // Todo lo que necesita el cálculo en una sola consulta y ya agrupado: el histórico no se trae fila a fila.
        var h = await HistoricoCuentaComun.LeerAsync(db, inicio, detalleDelMes: true, ct);
        return new HistoricoCuentaDetallado(
            new HistoricoCuenta(
                h.Vigentes, h.Gastos, h.Reembolsos, h.Retiradas, h.Depositos, h.CuentaActiva, h.GastadoMes, h.GastosDelMes, h.SaldoInicial,
                h.Frecuencia, h.Ingresos, h.Deudas),
            h.AhorroActivo,
            h.Aportaciones.Select(a => new AportacionRegistrada(a.Id, a.MiembroId, a.Fecha, a.Importe, a.Ahorro)).ToList(),
            h.ReembolsosDelMes, h.RetiradasDelMes, h.DepositosDelMes, h.Nombres);
    }

    /// <inheritdoc />
    public async Task<DatosMovimientosBanco> MovimientosBancoAsync(DateOnly hasta, CancellationToken ct)
    {
        var gastos = await db.Gastos.AsNoTracking().Where(g => g.PagadoPor == null && g.Fecha <= hasta)
            .Select(g => new GastoPagadoPorCuenta(g.Id, g.Fecha, g.Importe, g.CategoriaId, g.Concepto, g.PagadoDesdeAhorro)).ToListAsync(ct);
        var reembolsos = await db.ReembolsosCuenta.AsNoTracking().Where(x => x.Fecha <= hasta)
            .Select(x => new MovimientoCuenta(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToListAsync(ct);
        var ingresos = await db.IngresosCuentaComun.AsNoTracking().Where(x => x.Fecha <= hasta)
            .Select(x => new MovimientoCuenta(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToListAsync(ct);
        var depositos = await db.DepositosAhorro.AsNoTracking().Where(x => x.Fecha <= hasta)
            .Select(x => new MovimientoCuenta(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToListAsync(ct);
        var retiradas = await db.RetiradasAhorro.AsNoTracking().Where(x => x.Fecha <= hasta)
            .Select(x => new MovimientoCuenta(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToListAsync(ct);
        var adelantados = await db.Gastos.AsNoTracking()
            .Where(g => g.ACargoCuentaComun && g.PagadoPor != null && g.PagadoDesdeAhorro && g.Fecha <= hasta)
            .Select(g => new GastoPagadoPorCuenta(g.Id, g.Fecha, g.Importe, g.CategoriaId, g.Concepto, true, g.PagadoPor)).ToListAsync(ct);
        var deudas = await db.MovimientosDeudaCuenta.AsNoTracking().Where(x => x.Fecha <= hasta)
            .Select(x => new MovimientoDeuda(x.MiembroId, x.Fecha, x.Importe, x.Tipo, x.DesdeAhorro)).ToListAsync(ct);
        return new DatosMovimientosBanco(
            await AportacionesAsync(ct), await SaldoInicialAsync(ct), gastos, reembolsos, ingresos, depositos, retiradas, adelantados,
            Deudas: deudas);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MovimientoDeuda>> DeudasAsync(CancellationToken ct)
        => await db.MovimientosDeudaCuenta.AsNoTracking()
            .Select(x => new MovimientoDeuda(x.MiembroId, x.Fecha, x.Importe, x.Tipo, x.DesdeAhorro)).ToListAsync(ct);

    /// <inheritdoc />
    public async Task GuardarDeudaAsync(MovimientoDeudaCuenta deuda, CancellationToken ct)
    {
        db.MovimientosDeudaCuenta.Add(deuda);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<bool> ExisteMiembroAsync(Guid id, CancellationToken ct) => db.Miembros.AnyAsync(m => m.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
        => db.Miembros.AnyAsync(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AportacionVigente>> AportacionesAsync(CancellationToken ct)
    {
        var frecuencia = await FrecuenciaAsync(ct);
        return (await db.AportacionesCuenta.AsNoTracking().ToListAsync(ct))
            .Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe, a.Ahorro, frecuencia)).ToList();
    }

    /// <inheritdoc />
    public Task<FrecuenciaAportacion> FrecuenciaAsync(CancellationToken ct)
        => db.Hogares.Select(h => h.AportacionFrecuencia).SingleAsync(ct);

    /// <inheritdoc />
    public async Task EstablecerFrecuenciaAsync(FrecuenciaAportacion frecuencia, CancellationToken ct)
    {
        var h = await db.Hogares.FirstAsync(ct);
        h.AportacionFrecuencia = frecuencia;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DepositoDeAhorro>> DepositosAsync(Guid? excluir, CancellationToken ct)
        => await db.DepositosAhorro.Where(d => d.Id != excluir)
            .Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UsoDeAhorro>> UsosDeAhorroAsync(CancellationToken ct)
    {
        var retiradas = await db.RetiradasAhorro.Select(r => new UsoDeAhorro(r.Fecha, r.Importe)).ToListAsync(ct);
        var gastos = await db.Gastos.Where(g => g.PagadoDesdeAhorro).Select(g => new UsoDeAhorro(g.Fecha, g.Importe)).ToListAsync(ct);
        return [.. retiradas, .. gastos];
    }

    /// <inheritdoc />
    public Task<DatosAhorro> DatosAhorroAsync(CancellationToken ct) => ConsultasAhorro.LeerAsync(db, null, ct);

    /// <inheritdoc />
    public async Task<decimal> GastadoACargoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct)
        => await db.Gastos.Where(g => g.ACargoCuentaComun && g.PagadoPor == miembroId && g.PagadoDesdeAhorro == desdeAhorro)
            .SumAsync(g => g.Importe, ct);

    /// <inheritdoc />
    public async Task<decimal> ReembolsadoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct)
        => await db.ReembolsosCuenta.Where(r => r.MiembroId == miembroId && r.DesdeAhorro == desdeAhorro)
            .SumAsync(r => r.Importe, ct);

    /// <inheritdoc />
    public async Task<AportacionCuenta> FijarAportacionAsync(
        Guid hogarId, Guid miembroId, DateOnly desde, decimal importe, decimal ahorro, CancellationToken ct)
    {
        var a = await db.AportacionesCuenta.FirstOrDefaultAsync(x => x.MiembroId == miembroId && x.Desde == desde, ct);
        if (a is null)
        {
            a = new AportacionCuenta { Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = miembroId, Desde = desde };
            db.AportacionesCuenta.Add(a);
        }
        a.Importe = importe;
        a.Ahorro = ahorro;
        await db.SaveChangesAsync(ct);
        return a;
    }

    /// <inheritdoc />
    public async Task GuardarReembolsoAsync(ReembolsoCuenta reembolso, CancellationToken ct)
    {
        db.ReembolsosCuenta.Add(reembolso);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> EliminarReembolsoAsync(Guid id, CancellationToken ct)
    {
        var r = await db.ReembolsosCuenta.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        db.ReembolsosCuenta.Remove(r);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task GuardarIngresoAsync(IngresoCuentaComun ingreso, CancellationToken ct)
    {
        db.IngresosCuentaComun.Add(ingreso);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngresoCuentaComun>> IngresosAsync(CancellationToken ct)
        => await db.IngresosCuentaComun.OrderByDescending(i => i.Fecha).ThenBy(i => i.Id).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<bool> EliminarIngresoAsync(Guid id, CancellationToken ct)
    {
        var i = await db.IngresosCuentaComun.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return false;
        db.IngresosCuentaComun.Remove(i);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task GuardarDepositoAsync(DepositoAhorro deposito, CancellationToken ct)
    {
        db.DepositosAhorro.Add(deposito);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<bool> ExisteDepositoAsync(Guid id, CancellationToken ct) => db.DepositosAhorro.AnyAsync(d => d.Id == id, ct);

    /// <inheritdoc />
    public async Task<bool> EliminarDepositoAsync(Guid id, CancellationToken ct)
    {
        var d = await db.DepositosAhorro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return false;
        db.DepositosAhorro.Remove(d);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task GuardarRetiradaAsync(RetiradaAhorro retirada, CancellationToken ct)
    {
        db.RetiradasAhorro.Add(retirada);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> EliminarRetiradaAsync(Guid id, CancellationToken ct)
    {
        var r = await db.RetiradasAhorro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        db.RetiradasAhorro.Remove(r);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>Lectura de los datos del ahorro compartida por los repositorios que calculan el ahorro disponible.</summary>
internal static class ConsultasAhorro
{
    /// <summary>Aportaciones, depósitos y lo ya usado desde el ahorro; <paramref name="excluirGasto"/> deja fuera un gasto que se edita.</summary>
    public static async Task<DatosAhorro> LeerAsync(MiParteDbContext db, Guid? excluirGasto, CancellationToken ct)
    {
        var frecuencia = await db.Hogares.Select(h => h.AportacionFrecuencia).SingleAsync(ct);
        var aportaciones = (await db.AportacionesCuenta.ToListAsync(ct))
            .Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe, a.Ahorro, frecuencia)).ToList();
        var depositos = await db.DepositosAhorro.Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToListAsync(ct);
        var retirado = await db.RetiradasAhorro.SumAsync(r => r.Importe, ct);
        var gastado = await db.Gastos.Where(g => g.PagadoDesdeAhorro && g.Id != excluirGasto).SumAsync(g => g.Importe, ct);
        return new DatosAhorro(aportaciones, depositos, retirado, gastado);
    }
}
