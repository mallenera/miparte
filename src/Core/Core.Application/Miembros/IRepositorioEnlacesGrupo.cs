using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Miembros;

/// <summary>Resultado de intentar usar un enlace de grupo de forma atómica.</summary>
public enum ResultadoUsoEnlace
{
    /// <summary>La unión se hizo: el usuario quedó vinculado o dado de alta y el uso contado.</summary>
    Usado,

    /// <summary>Otra petición se adelantó: el enlace se revocó o caducó mientras tanto.</summary>
    EnlaceNoDisponible,

    /// <summary>Otra petición del mismo usuario se adelantó: ya figura en el hogar. No cuenta uso ni deja miembro nuevo.</summary>
    UsuarioYaEnHogar,
}

/// <summary>
/// Acceso a datos de los enlaces de grupo. Salvo <see cref="PorHashAsync"/> (que ocurre antes de pertenecer al hogar), el
/// implementador aplica el aislamiento por el hogar actual.
/// </summary>
public interface IRepositorioEnlacesGrupo
{
    /// <summary>Todos los enlaces del hogar actual (activos, revocados y caducados), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<EnlaceGrupo>> ListarAsync(CancellationToken ct);

    /// <summary>Enlace del hogar actual para leerlo y revocarlo; los cambios se persisten con <see cref="GuardarAsync"/>.</summary>
    Task<EnlaceGrupo?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Guarda un enlace nuevo.</summary>
    Task AnadirAsync(EnlaceGrupo enlace, CancellationToken ct);

    /// <summary>Guarda los cambios pendientes de un enlace obtenido con <see cref="ObtenerAsync"/>.</summary>
    Task GuardarAsync(CancellationToken ct);

    /// <summary>Enlace de cualquier hogar con ese hash de token, o null.</summary>
    Task<EnlaceGrupo?> PorHashAsync(string hash, CancellationToken ct);

    /// <summary>
    /// Cuenta una unión con el enlace y da de alta <paramref name="nuevo"/>, de forma atómica y con rastro en la auditoría. Si otra
    /// petición se adelantó (enlace revocado o caducado) no cambia nada y lo indica.
    /// </summary>
    Task<ResultadoUsoEnlace> UsarAsync(EnlaceGrupo enlace, Guid userId, Miembro nuevo, DateTimeOffset ahora, CancellationToken ct);
}
