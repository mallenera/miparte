using MiParte.Core.Application.ListaCompra;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Web.Demo;

/// <summary>Lista de la compra del modo demo: productos y enlaces en memoria, con la misma lógica que la base de datos.</summary>
internal sealed class RepositorioListaCompraEnMemoria(AlmacenDemo a) : IRepositorioListaCompra
{
    public Task<bool> ActivaAsync(Guid hogarId, CancellationToken ct) => Task.FromResult(a.Hogar.ListaCompraActiva);

    public Task EstablecerActivaAsync(Guid hogarId, bool activa, CancellationToken ct)
    {
        a.Hogar.ListaCompraActiva = activa;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProductoListaCompra>> ProductosAsync(Guid hogarId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProductoListaCompra>>(
            a.Productos.Where(p => p.HogarId == hogarId).OrderBy(p => p.Comprado).ThenBy(p => p.CreadoEn).ThenBy(p => p.Id).ToList());

    public Task<ProductoListaCompra?> ProductoAsync(Guid hogarId, Guid id, CancellationToken ct)
        => Task.FromResult(a.Productos.FirstOrDefault(p => p.HogarId == hogarId && p.Id == id));

    public Task GuardarProductoAsync(ProductoListaCompra producto, CancellationToken ct)
    {
        a.Productos.Add(producto);
        return Task.CompletedTask;
    }

    public Task ActualizarProductoAsync(ProductoListaCompra producto, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> EliminarProductoAsync(Guid hogarId, Guid id, CancellationToken ct)
        => Task.FromResult(a.Productos.RemoveAll(p => p.HogarId == hogarId && p.Id == id) > 0);

    public Task GuardarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct)
    {
        a.EnlacesListaCompra.Add(enlace);
        return Task.CompletedTask;
    }

    public Task<EnlaceListaCompra?> EnlaceAsync(Guid hogarId, Guid id, CancellationToken ct)
        => Task.FromResult(a.EnlacesListaCompra.FirstOrDefault(e => e.HogarId == hogarId && e.Id == id));

    public Task ActualizarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct) => Task.CompletedTask;

    public Task<EnlaceListaCompra?> EnlacePorHashAsync(string tokenHash, CancellationToken ct)
        => Task.FromResult(a.EnlacesListaCompra.FirstOrDefault(e => e.TokenHash == tokenHash));
}
