using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioRecurrentes"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioRecurrentes(MiParteDbContext db) : IRepositorioRecurrentes
{
    /// <summary>SQLSTATE de PostgreSQL para una violación de unicidad.</summary>
    private const string SqlStateUnicidad = "23505";

    /// <inheritdoc />
    public Task<bool> RecurrentesActivasAsync(CancellationToken ct) => db.Hogares.AnyAsync(h => h.RecurrentesActivas, ct);

    /// <inheritdoc />
    public async Task EstablecerRecurrentesActivasAsync(bool activas, CancellationToken ct)
    {
        var h = await db.Hogares.FirstAsync(ct);
        h.RecurrentesActivas = activas;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GastoRecurrente>> ListarAsync(CancellationToken ct)
        => await db.GastosRecurrentes.AsNoTracking().OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToListAsync(ct);

    /// <inheritdoc />
    public Task<GastoRecurrente?> ObtenerAsync(Guid id, CancellationToken ct)
        => db.GastosRecurrentes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <inheritdoc />
    public Task<GastoRecurrente?> ObtenerParaEditarAsync(Guid id, CancellationToken ct)
        => db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <inheritdoc />
    public void Anadir(GastoRecurrente plantilla) => db.GastosRecurrentes.Add(plantilla);

    /// <inheritdoc />
    public void Eliminar(GastoRecurrente plantilla) => db.GastosRecurrentes.Remove(plantilla);

    /// <inheritdoc />
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public Task<bool> TieneGastosGeneradosAsync(Guid id, CancellationToken ct)
        // gasto.gasto_recurrente_id es FK sin ON DELETE: con gastos generados hay que desactivar la plantilla, no borrarla.
        => db.Gastos.AnyAsync(g => g.GastoRecurrenteId == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GastoRecurrente>> ActivasAsync(CancellationToken ct)
        => await db.GastosRecurrentes.Where(r => r.Activo).OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> GeneradasEnElMesAsync(DateOnly inicio, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        var ids = await db.Gastos
            .Where(g => g.GastoRecurrenteId != null && g.Fecha >= inicio && g.Fecha < fin)
            .Select(g => g.GastoRecurrenteId!.Value).ToListAsync(ct);
        return ids.ToHashSet();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, PerfilReparto>> PerfilesAsync(CancellationToken ct)
        => await db.PerfilesReparto.Include(p => p.Detalles).ToDictionaryAsync(p => p.Id, ct);

    /// <inheritdoc />
    public async Task<ResultadoGuardadoGenerados> GuardarGeneradosAsync(IReadOnlyList<Gasto> gastos, CancellationToken ct)
    {
        db.Gastos.AddRange(gastos);
        try
        {
            await db.SaveChangesAsync(ct);
            return ResultadoGuardadoGenerados.Guardado;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: RepositorioGastos.SqlStateMesCerrado })
        {
            db.ChangeTracker.Clear();
            return ResultadoGuardadoGenerados.MesCerrado;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: SqlStateUnicidad })
        {
            // Otra petición generó alguno antes: se descartan los cambios para recalcular con una lectura limpia.
            db.ChangeTracker.Clear();
            return ResultadoGuardadoGenerados.Duplicado;
        }
    }
}
