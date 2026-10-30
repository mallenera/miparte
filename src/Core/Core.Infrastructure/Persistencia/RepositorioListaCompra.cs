using Microsoft.EntityFrameworkCore;
using MiParte.Core.Application.ListaCompra;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF Core de <see cref="IRepositorioListaCompra"/>. Filtra por el hogar que recibe cada método y no por el hogar
/// actual, de modo que sirve también al enlace público, que llega sin hogar en la petición.
/// </summary>
public sealed class RepositorioListaCompra(MiParteDbContext db) : IRepositorioListaCompra
{
    /// <inheritdoc />
    public Task<bool> ActivaAsync(Guid hogarId, CancellationToken ct)
        => db.Hogares.IgnoreQueryFilters().AnyAsync(h => h.Id == hogarId && h.ListaCompraActiva, ct);

    /// <inheritdoc />
    public async Task EstablecerActivaAsync(Guid hogarId, bool activa, CancellationToken ct)
    {
        var h = await db.Hogares.IgnoreQueryFilters().FirstAsync(x => x.Id == hogarId, ct);
        h.ListaCompraActiva = activa;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductoListaCompra>> ProductosAsync(Guid hogarId, CancellationToken ct)
        => await db.ProductosListaCompra.IgnoreQueryFilters().AsNoTracking().Where(p => p.HogarId == hogarId)
            .OrderBy(p => p.Comprado).ThenBy(p => p.CreadoEn).ThenBy(p => p.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<ProductoListaCompra?> ProductoAsync(Guid hogarId, Guid id, CancellationToken ct)
        => db.ProductosListaCompra.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.HogarId == hogarId && p.Id == id, ct);

    /// <inheritdoc />
    public async Task GuardarProductoAsync(ProductoListaCompra producto, CancellationToken ct)
    {
        db.ProductosListaCompra.Add(producto);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task ActualizarProductoAsync(ProductoListaCompra producto, CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public async Task<bool> EliminarProductoAsync(Guid hogarId, Guid id, CancellationToken ct)
    {
        var p = await ProductoAsync(hogarId, id, ct);
        if (p is null) return false;
        db.ProductosListaCompra.Remove(p);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task GuardarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct)
    {
        db.EnlacesListaCompra.Add(enlace);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<EnlaceListaCompra?> EnlaceAsync(Guid hogarId, Guid id, CancellationToken ct)
        => db.EnlacesListaCompra.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.HogarId == hogarId && e.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EnlaceListaCompra>> EnlacesAsync(Guid hogarId, CancellationToken ct)
        => await db.EnlacesListaCompra.IgnoreQueryFilters().AsNoTracking().Where(e => e.HogarId == hogarId)
            .OrderByDescending(e => e.CreadoEn).ThenBy(e => e.Id).ToListAsync(ct);

    /// <inheritdoc />
    public Task ActualizarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public Task<EnlaceListaCompra?> EnlacePorHashAsync(string tokenHash, CancellationToken ct)
        => db.EnlacesListaCompra.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.TokenHash == tokenHash, ct);
}
