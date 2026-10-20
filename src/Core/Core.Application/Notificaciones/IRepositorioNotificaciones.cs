using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.Notificaciones;

/// <summary>
/// Acceso a las suscripciones push. <see cref="ReemplazarAsync"/>, <see cref="DestinatariosAsync"/> y
/// <see cref="EliminarCaducadaAsync"/> no aplican el filtro del hogar actual: se usan desde el envío en segundo plano, donde no
/// hay petición. <see cref="EliminarAsync"/> sí lo aplica.
/// </summary>
public interface IRepositorioNotificaciones
{
    /// <summary>
    /// Deja como única suscripción de <paramref name="endpoint"/> la nueva (el mismo navegador puede pasar a otro miembro), de forma
    /// atómica.
    /// </summary>
    Task ReemplazarAsync(string endpoint, SuscripcionPush nueva, CancellationToken ct);

    /// <summary>Quita la suscripción de ese miembro y endpoint en el hogar actual. Devuelve false si no existía.</summary>
    Task<bool> EliminarAsync(Guid miembroId, string endpoint, CancellationToken ct);

    /// <summary>Suscripciones del hogar que deben recibir un aviso, salvo las del miembro excluido.</summary>
    Task<IReadOnlyList<SuscripcionPush>> DestinatariosAsync(Guid hogarId, Guid? excluirMiembroId, CancellationToken ct);

    /// <summary>Quita una suscripción que el servicio push dio por caducada (404 o 410).</summary>
    Task EliminarCaducadaAsync(string endpoint, CancellationToken ct);
}
