using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Miembros;

/// <summary>Resultado de intentar consumir una invitación de forma atómica.</summary>
public enum ResultadoConsumo
{
    /// <summary>La invitación se marcó como usada y el usuario quedó vinculado o dado de alta.</summary>
    Consumida,

    /// <summary>Otra petición se adelantó: la invitación ya está usada o ha caducado.</summary>
    InvitacionNoDisponible,

    /// <summary>Otra petición se adelantó: el miembro de la invitación ya no está disponible.</summary>
    MiembroNoDisponible,
}

/// <summary>
/// Acceso a datos de los miembros y las invitaciones. Salvo las operaciones «de cualquier hogar» (aceptar una invitación,
/// que ocurre antes de pertenecer al hogar), el implementador aplica el aislamiento por el hogar actual.
/// </summary>
public interface IRepositorioMiembros
{
    /// <summary>Miembros del hogar ordenados por nombre; los inactivos solo con <paramref name="incluirInactivos"/>.</summary>
    Task<IReadOnlyList<Miembro>> ListarAsync(bool incluirInactivos, CancellationToken ct);

    /// <summary>Miembro activo del hogar actual vinculado al usuario, o null si no lo es.</summary>
    Task<Miembro?> MiembroActivoDeUsuarioAsync(Guid userId, CancellationToken ct);

    /// <summary>Miembro del hogar actual para leerlo y modificarlo; los cambios se persisten con <see cref="GuardarAsync"/>.</summary>
    Task<Miembro?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Guarda un miembro nuevo.</summary>
    Task AnadirAsync(Miembro miembro, CancellationToken ct);

    /// <summary>Si hay otro administrador activo y vinculado a un usuario distinto de <paramref name="excluirId"/>.</summary>
    Task<bool> HayOtroAdminActivoAsync(Guid excluirId, CancellationToken ct);

    /// <summary>Miembros activos con cuenta distintos de <paramref name="excluirId"/>.</summary>
    Task<IReadOnlyList<Miembro>> OtrosConCuentaActivosAsync(Guid excluirId, CancellationToken ct);

    /// <summary>Si el adulto es responsable de algún miembro a cargo activo.</summary>
    Task<bool> TieneACargoActivosAsync(Guid responsableId, CancellationToken ct);

    /// <summary>Guarda los cambios pendientes de los miembros obtenidos con <see cref="ObtenerAsync"/>.</summary>
    Task GuardarAsync(CancellationToken ct);

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> de modo que dos peticiones sobre el mismo hogar no se solapen (lectura de reglas y
    /// guardado son una unidad). Confirma si el resultado es correcto y revierte si no.
    /// </summary>
    Task<Resultado<T>> EjecutarExclusivoAsync<T>(Guid hogarId, Func<Task<Resultado<T>>> operacion, CancellationToken ct);

    /// <summary>Guarda una invitación nueva.</summary>
    Task GuardarInvitacionAsync(InvitacionHogar invitacion, CancellationToken ct);

    /// <summary>Invitación (de cualquier hogar) con ese hash de token, o null.</summary>
    Task<InvitacionHogar?> InvitacionPorHashAsync(string hash, CancellationToken ct);

    /// <summary>Si el usuario ya figura, activo o no, en ese hogar.</summary>
    Task<bool> UsuarioEnHogarAsync(Guid hogarId, Guid userId, CancellationToken ct);

    /// <summary>Miembro de ese hogar (cualquiera), o null.</summary>
    Task<Miembro?> MiembroDeHogarAsync(Guid hogarId, Guid miembroId, CancellationToken ct);

    /// <summary>
    /// Marca la invitación como usada y vincula al usuario a su miembro (o da de alta <paramref name="nuevo"/>) de forma atómica,
    /// dejando rastro en la auditoría. Si otra petición se adelantó no cambia nada y lo indica.
    /// </summary>
    Task<ResultadoConsumo> ConsumirInvitacionAsync(
        InvitacionHogar invitacion, Guid userId, Miembro? nuevo, DateTimeOffset ahora, CancellationToken ct);

    /// <summary>El hogar con ese id (de cualquier hogar).</summary>
    Task<Hogar> HogarAsync(Guid id, CancellationToken ct);
}
