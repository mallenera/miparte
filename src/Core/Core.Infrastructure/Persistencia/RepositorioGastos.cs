using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioGastos"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioGastos(MiParteDbContext db) : IRepositorioGastos
{
    /// <summary>SQLSTATE con el que los triggers de la base de datos rechazan una escritura de gasto en un mes cerrado.</summary>
    public const string SqlStateMesCerrado = "MP409";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Gasto>> ListarAsync(FiltroGastos filtro, CancellationToken ct)
    {
        var q = db.Gastos.AsNoTracking().Include(g => g.Repartos).AsQueryable();
        if (filtro.Desde is { } desde) q = q.Where(g => g.Fecha >= desde);
        if (filtro.Hasta is { } hasta) q = q.Where(g => g.Fecha <= hasta);
        if (filtro.CategoriaId is { } c) q = q.Where(g => g.CategoriaId == c);
        if (filtro.MiembroId is { } m) q = q.Where(g => g.PagadoPor == m || g.Repartos.Any(r => r.MiembroId == m && r.ImporteAsumido > 0m));
        if (!string.IsNullOrEmpty(filtro.Texto))
        {
            var patron = filtro.Texto.ToLowerInvariant();
            q = q.Where(g => g.Concepto != null && g.Concepto.ToLower().Contains(patron));
        }
        return await q.OrderByDescending(g => g.Fecha).ThenBy(g => g.Id).ToListAsync(ct);
    }

    /// <inheritdoc />
    public Task<Gasto?> ObtenerAsync(Guid id, CancellationToken ct)
        => db.Gastos.AsNoTracking().Include(g => g.Repartos).FirstOrDefaultAsync(g => g.Id == id, ct);

    /// <inheritdoc />
    public Task<Gasto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct)
        => db.Gastos.Include(g => g.Repartos).FirstOrDefaultAsync(g => g.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> CategoriaExisteAsync(Guid id, CancellationToken ct) => db.Categorias.AnyAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
        => db.Miembros.AnyAsync(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto, ct);

    /// <inheritdoc />
    public Task<PerfilReparto?> ObtenerPerfilAsync(Guid id, CancellationToken ct)
        => db.PerfilesReparto.Include(p => p.Detalles).FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Miembro>> AdultosActivosAsync(CancellationToken ct)
        => await db.Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto)
            .OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToListAsync(ct);

    /// <inheritdoc />
    public Task<bool> FechaEnPeriodoCerradoAsync(DateOnly fecha, CancellationToken ct)
        => db.Hogares.AnyAsync(h => h.PeriodoCerradoEn != null && h.PeriodoDesde != null && h.PeriodoHasta != null
            && h.PeriodoDesde <= fecha && fecha <= h.PeriodoHasta, ct);

    /// <inheritdoc />
    public Task<bool> CuentaComunActivaAsync(CancellationToken ct) => db.Hogares.AnyAsync(h => h.CuentaComunActiva, ct);

    /// <inheritdoc />
    public Task<bool> AhorroActivoAsync(CancellationToken ct) => db.Hogares.AnyAsync(h => h.AhorroActivo, ct);

    /// <inheritdoc />
    public Task<DatosAhorro> DatosAhorroAsync(Guid? excluirGasto, CancellationToken ct) => ConsultasAhorro.LeerAsync(db, excluirGasto, ct);

    /// <inheritdoc />
    public Task<bool> MesCerradoAsync(DateOnly fecha, CancellationToken ct)
    {
        var inicio = CuentaComun.InicioMes(fecha);
        return db.MesesCerrados.AnyAsync(c => c.Mes == inicio, ct);
    }

    /// <inheritdoc />
    public void Anadir(Gasto gasto) => db.Gastos.Add(gasto);

    /// <inheritdoc />
    public void Eliminar(Gasto gasto)
    {
        db.GastosReparto.RemoveRange(gasto.Repartos);
        db.Gastos.Remove(gasto);
    }

    /// <inheritdoc />
    public async Task<bool> GuardarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: SqlStateMesCerrado })
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<T> EnExclusionAsync<T>(Func<Task<T>> operacion, CancellationToken ct)
    {
        // Sin base relacional (EF en memoria de las pruebas HTTP) no hay transacciones ni bloqueos que pedir.
        if (!db.Database.IsRelational()) return await operacion();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Mismo bloqueo que el cierre de ciclo: la fila del hogar queda bloqueada hasta el COMMIT.
        var hogarId = await db.Hogares.Select(h => h.Id).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"select 1 from public.hogar where id = {hogarId} for update", ct);
        var resultado = await operacion();
        await tx.CommitAsync(ct);
        return resultado;
    }
}
