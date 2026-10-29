using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.ListaCompra;

/// <summary>
/// Acceso a datos de la lista de la compra. Todos los métodos reciben el hogar de forma explícita (también los que usa el enlace
/// público, que aún no tiene hogar actual), así que el implementador no depende del filtro por hogar del contexto.
/// </summary>
public interface IRepositorioListaCompra
{
    /// <summary>Si el hogar tiene activada la lista de la compra.</summary>
    Task<bool> ActivaAsync(Guid hogarId, CancellationToken ct);

    /// <summary>Activa o desactiva la lista de la compra del hogar (no borra productos).</summary>
    Task EstablecerActivaAsync(Guid hogarId, bool activa, CancellationToken ct);

    /// <summary>Productos del hogar: los pendientes primero y, dentro de cada grupo, por fecha de alta.</summary>
    Task<IReadOnlyList<ProductoListaCompra>> ProductosAsync(Guid hogarId, CancellationToken ct);

    /// <summary>Un producto del hogar, o null si no es de ese hogar.</summary>
    Task<ProductoListaCompra?> ProductoAsync(Guid hogarId, Guid id, CancellationToken ct);

    /// <summary>Guarda un producto nuevo.</summary>
    Task GuardarProductoAsync(ProductoListaCompra producto, CancellationToken ct);

    /// <summary>Guarda los cambios de un producto ya leído.</summary>
    Task ActualizarProductoAsync(ProductoListaCompra producto, CancellationToken ct);

    /// <summary>Borra un producto del hogar; false si no existe en ese hogar.</summary>
    Task<bool> EliminarProductoAsync(Guid hogarId, Guid id, CancellationToken ct);

    /// <summary>Guarda un enlace nuevo.</summary>
    Task GuardarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct);

    /// <summary>Un enlace del hogar, o null si no es de ese hogar.</summary>
    Task<EnlaceListaCompra?> EnlaceAsync(Guid hogarId, Guid id, CancellationToken ct);

    /// <summary>Todos los enlaces del hogar, del más reciente al más antiguo (con su hash, nunca el token).</summary>
    Task<IReadOnlyList<EnlaceListaCompra>> EnlacesAsync(Guid hogarId, CancellationToken ct);

    /// <summary>Guarda los cambios de un enlace ya leído (p. ej. al revocarlo).</summary>
    Task ActualizarEnlaceAsync(EnlaceListaCompra enlace, CancellationToken ct);

    /// <summary>Busca un enlace por el hash de su token, sin filtrar por hogar (lo usa el acceso público).</summary>
    Task<EnlaceListaCompra?> EnlacePorHashAsync(string tokenHash, CancellationToken ct);
}
