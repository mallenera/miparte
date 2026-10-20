using System.Threading.Channels;
using MiParte.Core.Application.Notificaciones;

namespace MiParte.Core.Infrastructure.Notificaciones;

/// <summary>
/// Cola en memoria de los avisos pendientes de enviar. Es acotada: si se llena (el servicio push va lento o no hay claves
/// configuradas), los avisos nuevos se descartan en lugar de bloquear la petición del usuario.
/// </summary>
public sealed class ColaNotificacionesPush : IPublicadorNotificaciones
{
    /// <summary>Avisos que caben en la cola antes de descartar los nuevos.</summary>
    public const int Capacidad = 500;

    private readonly Channel<NotificacionPush> _cola = Channel.CreateBounded<NotificacionPush>(
        new BoundedChannelOptions(Capacidad) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Lector que consume el servicio de envío en segundo plano.</summary>
    public ChannelReader<NotificacionPush> Lector => _cola.Reader;

    /// <inheritdoc />
    public void Publicar(NotificacionPush notificacion) => _cola.Writer.TryWrite(notificacion);
}
