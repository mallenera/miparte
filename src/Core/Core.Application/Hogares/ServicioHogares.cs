using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Hogares;

/// <summary>Acceso a datos de los hogares del usuario (las consultas por usuario no dependen del hogar actual).</summary>
public interface IRepositorioHogares
{
    /// <summary>Hogares donde el usuario es miembro activo, ordenados por nombre.</summary>
    Task<IReadOnlyList<Hogar>> DelUsuarioAsync(Guid userId, CancellationToken ct);

    /// <summary>En cuántos hogares figura el usuario como miembro, activo o no.</summary>
    Task<int> ContarPertenenciasAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Crea el hogar, a su creador como miembro y la semilla por defecto (perfiles, detalles y categorías) en una transacción, y deja
    /// un solo evento de auditoría en lugar de decenas de filas.
    /// </summary>
    Task CrearConSemillaAsync(Hogar hogar, Miembro creador, Guid userId, CancellationToken ct);

    /// <summary>El hogar actual; null si no hay.</summary>
    Task<Hogar?> ActualAsync(CancellationToken ct);

    /// <summary>Elimina el hogar y, en cascada, todos sus datos.</summary>
    Task EliminarAsync(Hogar hogar, CancellationToken ct);
}

/// <summary>Casos de uso de los hogares del usuario: listar, crear con semilla y eliminar.</summary>
public sealed class ServicioHogares(IRepositorioHogares repositorio)
{
    /// <summary>Tope por usuario, para que un registro gratuito no llene la base de datos.</summary>
    public const int MaxHogaresPorUsuario = 10;

    /// <summary>Longitud máxima del nombre de hogar y del nombre de miembro.</summary>
    public const int MaxLongitudNombre = 100;

    /// <summary>Hogares del usuario ordenados por nombre.</summary>
    public Task<IReadOnlyList<Hogar>> ListarAsync(Guid userId, CancellationToken ct) => repositorio.DelUsuarioAsync(userId, ct);

    /// <summary>Un hogar del usuario; null si no existe o no es miembro (no se revela su existencia).</summary>
    public async Task<Hogar?> ObtenerAsync(Guid userId, Guid id, CancellationToken ct)
        => (await repositorio.DelUsuarioAsync(userId, ct)).FirstOrDefault(h => h.Id == id);

    /// <summary>
    /// Crea un hogar y a su creador como miembro adulto y admin, con la semilla por defecto. Inválido si faltan nombres o pasan de
    /// la longitud máxima; conflicto si el usuario ya está en <see cref="MaxHogaresPorUsuario"/> hogares.
    /// </summary>
    public async Task<Resultado<Hogar>> CrearAsync(Guid userId, string? nombreHogar, string? nombreMiembro, CancellationToken ct)
    {
        var hogarNombre = nombreHogar?.Trim();
        var miembroNombre = nombreMiembro?.Trim();
        if (string.IsNullOrEmpty(hogarNombre) || string.IsNullOrEmpty(miembroNombre))
            return Resultado<Hogar>.Invalido("El nombre del hogar y el tuyo son obligatorios.");
        if (hogarNombre.Length > MaxLongitudNombre || miembroNombre.Length > MaxLongitudNombre)
            return Resultado<Hogar>.Invalido($"Los nombres admiten como máximo {MaxLongitudNombre} caracteres.");
        if (await repositorio.ContarPertenenciasAsync(userId, ct) >= MaxHogaresPorUsuario)
            return Resultado<Hogar>.Conflicto($"Has alcanzado el máximo de {MaxHogaresPorUsuario} hogares.");

        var hogar = new Hogar { Id = Guid.NewGuid(), Nombre = hogarNombre };
        var creador = new Miembro
        {
            Id = Guid.NewGuid(), HogarId = hogar.Id, Nombre = miembroNombre, Tipo = TipoMiembro.Adulto,
            Rol = RolMiembro.Admin, UserId = userId,
        };
        await repositorio.CrearConSemillaAsync(hogar, creador, userId, ct);
        return Resultado<Hogar>.Ok(hogar);
    }

    /// <summary>
    /// Elimina el hogar actual y todos sus datos, sin vuelta atrás. Inválido si <paramref name="nombre"/> no es el nombre exacto del
    /// hogar (confirmación); no encontrado si no hay hogar actual.
    /// </summary>
    public async Task<Resultado<bool>> EliminarAsync(string? nombre, CancellationToken ct)
    {
        var hogar = await repositorio.ActualAsync(ct);
        if (hogar is null) return Resultado<bool>.NoEncontrado();
        if (!string.Equals(nombre?.Trim(), hogar.Nombre, StringComparison.Ordinal))
            return Resultado<bool>.Invalido("Escribe el nombre exacto del hogar para confirmar que quieres eliminarlo.");

        await repositorio.EliminarAsync(hogar, ct);
        return Resultado<bool>.Ok(true);
    }
}
