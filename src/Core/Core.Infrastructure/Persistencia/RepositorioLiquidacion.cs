using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioLiquidacion"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioLiquidacion(MiParteDbContext db) : IRepositorioLiquidacion
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Gasto>> GastosDelMesAsync(DateOnly inicio, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        return await db.Gastos.AsNoTracking().Include(g => g.Repartos).Where(g => g.Fecha >= inicio && g.Fecha < fin).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Miembro>> MiembrosAsync(CancellationToken ct)
        => await db.Miembros.AsNoTracking().ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> NombresCategoriasAsync(CancellationToken ct)
        => await db.Categorias.ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);

    /// <inheritdoc />
    public async Task<HistoricoCuenta> HistoricoCuentaAsync(DateOnly inicio, CancellationToken ct)
    {
        // Una sola consulta con el histórico de la cuenta ya agrupado (en vez de cinco tablas completas).
        var h = await HistoricoCuentaComun.LeerAsync(db, inicio, detalleDelMes: false, ct);
        return new HistoricoCuenta(h.Vigentes, h.Gastos, h.Reembolsos, h.Retiradas, h.Depositos, h.CuentaActiva, h.GastadoMes, h.GastosDelMes, h.SaldoInicial, h.Frecuencia);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PagoLiquidacionRegistro>> PagosDelMesAsync(DateOnly inicio, CancellationToken ct)
        => await db.PagosLiquidacion.AsNoTracking().Where(p => p.Mes == inicio).OrderBy(p => p.Fecha).ThenBy(p => p.Id).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<bool> ExistenAmbosAsync(Guid uno, Guid otro, CancellationToken ct)
        => await db.Miembros.CountAsync(m => m.Id == uno || m.Id == otro, ct) == 2;

    /// <inheritdoc />
    public async Task AnadirPagoAsync(PagoLiquidacionRegistro pago, CancellationToken ct)
    {
        db.PagosLiquidacion.Add(pago);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> EliminarPagoAsync(Guid id, CancellationToken ct)
    {
        var pago = await db.PagosLiquidacion.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (pago is null) return false;
        db.PagosLiquidacion.Remove(pago);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<ModoLiquidacion> ModoAsync(CancellationToken ct)
        => await db.Hogares.Select(h => h.LiquidacionModo).SingleAsync(ct);

    /// <inheritdoc />
    public async Task EstablecerModoAsync(ModoLiquidacion modo, CancellationToken ct)
    {
        var h = await db.Hogares.FirstAsync(ct);
        h.LiquidacionModo = modo;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Gasto>> GastosPendientesAsync(CancellationToken ct)
        => await db.Gastos.AsNoTracking().Include(g => g.Repartos).Where(g => g.CicloLiquidacionId == null).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<int> UltimoNumeroCicloAsync(CancellationToken ct)
        => await db.CiclosLiquidacion.Select(c => (int?)c.Numero).MaxAsync(ct) ?? 0;

    /// <inheritdoc />
    public async Task CerrarCicloAsync(CicloLiquidacion ciclo, IReadOnlyList<Guid> gastos, CancellationToken ct)
    {
        var ids = gastos.ToArray();
        db.CiclosLiquidacion.Add(ciclo);
        await db.SaveChangesAsync(ct); // primero el ciclo: los gastos lo referencian por clave foránea y la auditoría lo registra aquí
        // Sin seguimiento de cambios: el cierre queda en la auditoría como un solo evento (el del ciclo), no uno por gasto.
        var marcados = await db.Gastos.Where(x => ids.Contains(x.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CicloLiquidacionId, ciclo.Id), ct);
        // Un gasto borrado a la vez por otra petición deja el total desfasado: se aborta (rollback) en vez de cerrar a medias.
        if (marcados != ids.Length) throw new CierreConcurrenteException();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CicloConAutor>> ListarCiclosAsync(CancellationToken ct)
    {
        var ciclos = await db.CiclosLiquidacion.AsNoTracking().OrderByDescending(c => c.Numero).ToListAsync(ct);
        var nombres = await db.Miembros.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);
        return ciclos.Select(c => new CicloConAutor(c, nombres.GetValueOrDefault(c.CerradoPor))).ToList();
    }

    /// <inheritdoc />
    public async Task<T> EnExclusionAsync<T>(Func<Task<T>> operacion, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Bloquea la fila del hogar hasta el COMMIT: dos cierres del mismo hogar no pueden leer los mismos gastos pendientes.
        var hogarId = await db.Hogares.Select(h => h.Id).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"select 1 from public.hogar where id = {hogarId} for update", ct);
        var resultado = await operacion();
        await tx.CommitAsync(ct);
        return resultado;
    }
}
