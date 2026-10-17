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
        return new HistoricoCuenta(h.Vigentes, h.Gastos, h.Reembolsos, h.Retiradas, h.Depositos, h.CuentaActiva, h.GastadoMes, h.GastosDelMes, h.SaldoInicial);
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
}
