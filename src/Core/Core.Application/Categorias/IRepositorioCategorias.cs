using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Categorias;

/// <summary>Acceso a datos de las categorías de gasto del hogar actual (el implementador aplica el aislamiento por hogar).</summary>
public interface IRepositorioCategorias
{
    /// <summary>Categorías ordenadas por nombre.</summary>
    Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken ct);

    /// <summary>Una categoría para modificarla o borrarla con <see cref="GuardarAsync"/>; null si no existe en el hogar.</summary>
    Task<Categoria?> ObtenerParaEditarAsync(Guid id, CancellationToken ct);

    /// <summary>El perfil de reparto; null si no existe en el hogar.</summary>
    Task<PerfilReparto?> PerfilAsync(Guid id, CancellationToken ct);

    /// <summary>El perfil de cuenta común del hogar (el primero por nombre); null si no hay ninguno.</summary>
    Task<PerfilReparto?> PerfilDeCuentaComunAsync(CancellationToken ct);

    /// <summary>Si la categoría ya está marcada «a cargo de la cuenta común».</summary>
    Task<bool> EstaACargoDeLaCuentaAsync(Guid id, CancellationToken ct);

    /// <summary>Cada categoría con su categoría padre (null en las de primer nivel).</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> PadresAsync(CancellationToken ct);

    /// <summary>Si otra categoría (distinta de <paramref name="excluirId"/>) tiene ese nombre bajo ese padre.</summary>
    Task<bool> NombreEnNivelAsync(Guid? padreId, string nombre, Guid? excluirId, CancellationToken ct);

    /// <summary>Si la categoría tiene subcategorías.</summary>
    Task<bool> TieneSubcategoriasAsync(Guid id, CancellationToken ct);

    /// <summary>Si la categoría tiene gastos o gastos recurrentes.</summary>
    Task<bool> TieneGastosAsync(Guid id, CancellationToken ct);

    /// <summary>Marca los gastos y gastos recurrentes de <paramref name="origen"/> para pasarlos a <paramref name="destino"/> con <see cref="GuardarAsync"/>.</summary>
    Task ReasignarGastosAsync(Guid origen, Guid destino, CancellationToken ct);

    /// <summary>Marca una categoría nueva para guardarla con <see cref="GuardarAsync"/>.</summary>
    void Anadir(Categoria categoria);

    /// <summary>Marca una categoría para borrarla con <see cref="GuardarAsync"/>.</summary>
    void Eliminar(Categoria categoria);

    /// <summary>Guarda los cambios pendientes; false si la base de datos los rechaza (nombre repetido o categoría en uso por una carrera).</summary>
    Task<bool> GuardarAsync(CancellationToken ct);
}
