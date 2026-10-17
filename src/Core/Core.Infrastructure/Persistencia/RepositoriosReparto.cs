using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioPerfiles"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioPerfiles(MiParteDbContext db) : IRepositorioPerfiles
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PerfilReparto>> ListarAsync(CancellationToken ct)
        => await db.PerfilesReparto.AsNoTracking().Include(p => p.Detalles).OrderBy(p => p.Nombre).ToListAsync(ct);

    /// <inheritdoc />
    public Task<PerfilReparto?> ObtenerAsync(Guid id, CancellationToken ct)
        => db.PerfilesReparto.Include(x => x.Detalles).FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <inheritdoc />
    public Task<PerfilReparto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

    /// <inheritdoc />
    public Task<int> ContarAdultosActivosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => db.Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto && ids.Contains(m.Id)).CountAsync(ct);

    /// <inheritdoc />
    public Task<bool> NombreEnUsoAsync(string nombre, Guid? excluirId, CancellationToken ct)
        => db.PerfilesReparto.AnyAsync(p => p.Nombre == nombre && p.Id != excluirId, ct);

    /// <inheritdoc />
    public async Task<bool> EnUsoAsync(Guid id, CancellationToken ct)
        => await db.Categorias.AnyAsync(c => c.PerfilRepartoId == id, ct)
            || await db.Gastos.AnyAsync(g => g.PerfilRepartoId == id, ct)
            || await db.GastosRecurrentes.AnyAsync(g => g.PerfilRepartoId == id, ct);

    /// <inheritdoc />
    public async Task ReasignarUsoAsync(Guid origen, Guid destino, CancellationToken ct)
    {
        foreach (var c in await db.Categorias.Where(c => c.PerfilRepartoId == origen).ToListAsync(ct)) c.PerfilRepartoId = destino;
        foreach (var g in await db.Gastos.Where(g => g.PerfilRepartoId == origen).ToListAsync(ct)) g.PerfilRepartoId = destino;
        foreach (var r in await db.GastosRecurrentes.Where(r => r.PerfilRepartoId == origen).ToListAsync(ct)) r.PerfilRepartoId = destino;
    }

    /// <inheritdoc />
    public void Anadir(PerfilReparto perfil) => db.PerfilesReparto.Add(perfil);

    /// <inheritdoc />
    public void ReemplazarDetalle(PerfilReparto perfil, IReadOnlyList<PerfilRepartoDetalle> nuevo)
    {
        db.PerfilesRepartoDetalle.RemoveRange(perfil.Detalles);
        perfil.Detalles.Clear();
        foreach (var d in nuevo)
        {
            perfil.Detalles.Add(d);
            db.PerfilesRepartoDetalle.Add(d);
        }
    }

    /// <inheritdoc />
    public void Eliminar(PerfilReparto perfil)
    {
        db.PerfilesRepartoDetalle.RemoveRange(perfil.Detalles);
        db.PerfilesReparto.Remove(perfil);
    }

    /// <inheritdoc />
    public async Task<bool> GuardarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" or "23503" })
        {
            // Nombre repetido (23505) o referencia a algo que otra petición acaba de borrar (23503): lo esperable. Cualquier otro
            // error de la base de datos llega al manejador de 500 en lugar de disfrazarse de «nombre repetido».
            return false;
        }
    }
}

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioCategorias"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioCategorias(MiParteDbContext db) : IRepositorioCategorias
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken ct)
        => await db.Categorias.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync(ct);

    /// <inheritdoc />
    public Task<Categoria?> ObtenerParaEditarAsync(Guid id, CancellationToken ct)
        => db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public Task<PerfilReparto?> PerfilAsync(Guid id, CancellationToken ct)
        => db.PerfilesReparto.FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public Task<PerfilReparto?> PerfilDeCuentaComunAsync(CancellationToken ct)
        => db.PerfilesReparto.Where(p => p.Modo == ModoReparto.CuentaComun).OrderBy(p => p.Nombre).ThenBy(p => p.Id).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<bool> EstaACargoDeLaCuentaAsync(Guid id, CancellationToken ct)
        => db.Categorias.AnyAsync(c => c.Id == id && c.ACargoCuentaComun, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, Guid?>> PadresAsync(CancellationToken ct)
        => await db.Categorias.ToDictionaryAsync(c => c.Id, c => c.CategoriaPadreId, ct);

    /// <inheritdoc />
    public Task<bool> NombreEnNivelAsync(Guid? padreId, string nombre, Guid? excluirId, CancellationToken ct)
        => db.Categorias.AnyAsync(c => c.CategoriaPadreId == padreId && c.Nombre == nombre && c.Id != excluirId, ct);

    /// <inheritdoc />
    public Task<bool> TieneSubcategoriasAsync(Guid id, CancellationToken ct) => db.Categorias.AnyAsync(c => c.CategoriaPadreId == id, ct);

    /// <inheritdoc />
    public async Task<bool> TieneGastosAsync(Guid id, CancellationToken ct)
        => await db.Gastos.AnyAsync(g => g.CategoriaId == id, ct) || await db.GastosRecurrentes.AnyAsync(g => g.CategoriaId == id, ct);

    /// <inheritdoc />
    public async Task ReasignarGastosAsync(Guid origen, Guid destino, CancellationToken ct)
    {
        foreach (var g in await db.Gastos.Where(g => g.CategoriaId == origen).ToListAsync(ct)) g.CategoriaId = destino;
        foreach (var r in await db.GastosRecurrentes.Where(r => r.CategoriaId == origen).ToListAsync(ct)) r.CategoriaId = destino;
    }

    /// <inheritdoc />
    public void Anadir(Categoria categoria) => db.Categorias.Add(categoria);

    /// <inheritdoc />
    public void Eliminar(Categoria categoria) => db.Categorias.Remove(categoria);

    /// <inheritdoc />
    public async Task<bool> GuardarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" or "23503" })
        {
            // Nombre repetido (23505) o referencia a algo que otra petición acaba de borrar (23503): lo esperable. Cualquier otro
            // error de la base de datos llega al manejador de 500 en lugar de disfrazarse de «nombre repetido».
            return false;
        }
    }
}

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioCierres"/>. El <see cref="MiParteDbContext"/> ya filtra por el hogar
/// actual, así que ninguna consulta repite el filtro.
/// </summary>
public sealed class RepositorioCierres(MiParteDbContext db) : IRepositorioCierres
{
    /// <summary>SQLSTATE de PostgreSQL para una violación de unicidad.</summary>
    private const string SqlStateUnicidad = "23505";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MesCerrado>> ListarAsync(CancellationToken ct)
        => await db.MesesCerrados.AsNoTracking().OrderByDescending(c => c.Mes).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> NombresPorUsuarioAsync(CancellationToken ct)
        => await db.Miembros.Where(m => m.UserId != null).ToDictionaryAsync(m => m.UserId!.Value, m => m.Nombre, ct);

    /// <inheritdoc />
    public Task<bool> EstaCerradoAsync(DateOnly inicio, CancellationToken ct) => db.MesesCerrados.AnyAsync(c => c.Mes == inicio, ct);

    /// <inheritdoc />
    public async Task<bool> GuardarAsync(MesCerrado cierre, CancellationToken ct)
    {
        db.MesesCerrados.Add(cierre);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: SqlStateUnicidad })
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> EliminarAsync(DateOnly inicio, CancellationToken ct)
    {
        var cierre = await db.MesesCerrados.FirstOrDefaultAsync(c => c.Mes == inicio, ct);
        if (cierre is null) return false;
        db.MesesCerrados.Remove(cierre);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
