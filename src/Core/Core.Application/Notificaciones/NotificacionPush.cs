namespace MiParte.Core.Application.Notificaciones;

/// <summary>
/// Aviso que se envía a los miembros del hogar con la suscripción activa, salvo <paramref name="ExcluirMiembroId"/>.
/// </summary>
/// <param name="HogarId">Hogar cuyos miembros reciben el aviso.</param>
/// <param name="ExcluirMiembroId">Miembro que no lo recibe (normalmente quien provocó el cambio), o null.</param>
/// <param name="Titulo">Título visible.</param>
/// <param name="Cuerpo">Texto visible.</param>
/// <param name="Ruta">Ruta de la PWA que se abre al pulsar el aviso (p. ej. <c>/gastos</c>).</param>
public sealed record NotificacionPush(Guid HogarId, Guid? ExcluirMiembroId, string Titulo, string Cuerpo, string Ruta);

/// <summary>
/// Puerto para encolar avisos sin esperar al envío: el caso de uso no conoce el servicio push ni su latencia. Quien lo implementa
/// descarta el aviso si no puede encolarlo; el cambio que lo provocó ya está guardado y no debe fallar por ello.
/// </summary>
public interface IPublicadorNotificaciones
{
    /// <summary>Encola el aviso para su envío en segundo plano.</summary>
    void Publicar(NotificacionPush notificacion);
}
