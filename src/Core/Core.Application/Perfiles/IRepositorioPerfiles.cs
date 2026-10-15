using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Perfiles;

/// <summary>Acceso a datos de los perfiles de reparto del hogar actual (el implementador aplica el aislamiento por hogar).</summary>
public interface IRepositorioPerfiles
{
    /// <summary>Perfiles con su detalle, ordenados por nombre.</summary>
    Task<IReadOnlyList<PerfilReparto>> ListarAsync(CancellationToken ct);

    /// <summary>Un perfil con su detalle; null si no existe en el hogar.</summary>
    Task<PerfilReparto?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Un perfil con su detalle para modificarlo con <see cref="GuardarAsync"/>; null si no existe en el hogar.</summary>
    Task<PerfilReparto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct);

    /// <summary>Cuántos de los miembros dados son adultos activos del hogar.</summary>
    Task<int> ContarAdultosActivosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Si otro perfil (distinto de <paramref name="excluirId"/>) tiene ese nombre.</summary>
    Task<bool> NombreEnUsoAsync(string nombre, Guid? excluirId, CancellationToken ct);

    /// <summary>Si alguna categoría, gasto o gasto recurrente usa el perfil.</summary>
    Task<bool> EnUsoAsync(Guid id, CancellationToken ct);

    /// <summary>Marca un perfil nuevo para guardarlo con <see cref="GuardarAsync"/>.</summary>
    void Anadir(PerfilReparto perfil);

    /// <summary>Sustituye todo el detalle del perfil por <paramref name="nuevo"/>.</summary>
    void ReemplazarDetalle(PerfilReparto perfil, IReadOnlyList<PerfilRepartoDetalle> nuevo);

    /// <summary>Marca el perfil y su detalle para borrarlos con <see cref="GuardarAsync"/>.</summary>
    void Eliminar(PerfilReparto perfil);

    /// <summary>Guarda los cambios pendientes; false si la base de datos los rechaza (nombre repetido o perfil en uso por una carrera).</summary>
    Task<bool> GuardarAsync(CancellationToken ct);
}
