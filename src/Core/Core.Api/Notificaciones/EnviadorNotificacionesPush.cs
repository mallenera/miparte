using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Infrastructure.Notificaciones;
using WebPush;

namespace MiParte.Core.Api.Notificaciones;

/// <summary>
/// Servicio en segundo plano que envía los avisos encolados en <see cref="ColaNotificacionesPush"/> a las suscripciones del hogar.
/// Un fallo de envío nunca llega a la petición que lo provocó: se registra y se sigue con el siguiente. Las suscripciones que el
/// servicio push da por caducadas (404 o 410) se borran. Sin claves VAPID no envía nada.
/// </summary>
public sealed class EnviadorNotificacionesPush(
    ColaNotificacionesPush cola, IServiceScopeFactory ambitos, IOptions<OpcionesPush> opciones, ILogger<EnviadorNotificacionesPush> log)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        if (!o.Configurado)
        {
            log.LogWarning("Avisos del navegador desactivados: faltan las claves VAPID (Notificaciones__VapidPublica, VapidPrivada o Contacto).");
            return;
        }

        var cliente = new WebPushClient();
        var vapid = new VapidDetails(o.Contacto, o.VapidPublica, o.VapidPrivada);
        try
        {
            await foreach (var aviso in cola.Lector.ReadAllAsync(ct))
                await EnviarAsync(cliente, vapid, aviso, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Apagado de la API: se deja de enviar.
        }
    }

    /// <summary>Envía un aviso a cada suscripción del hogar salvo la del miembro excluido, en su propio ámbito de base de datos.</summary>
    private async Task EnviarAsync(WebPushClient cliente, VapidDetails vapid, NotificacionPush aviso, CancellationToken ct)
    {
        try
        {
            await using var ambito = ambitos.CreateAsyncScope();
            var repositorio = ambito.ServiceProvider.GetRequiredService<IRepositorioNotificaciones>();
            var destinos = await repositorio.DestinatariosAsync(aviso.HogarId, aviso.ExcluirMiembroId, ct);
            if (destinos.Count == 0) return;

            var carga = JsonSerializer.Serialize(new { titulo = aviso.Titulo, cuerpo = aviso.Cuerpo, ruta = aviso.Ruta });
            foreach (var destino in destinos)
            {
                try
                {
                    await cliente.SendNotificationAsync(new PushSubscription(destino.Endpoint, destino.P256dh, destino.Auth), carga, vapid, ct);
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    // Suscripción caducada o revocada por el navegador: ya no sirve. No se registra el endpoint (es un secreto).
                    await repositorio.EliminarCaducadaAsync(destino.Endpoint, ct);
                }
                catch (WebPushException ex)
                {
                    log.LogWarning("Aviso push no entregado (HTTP {Estado}).", (int)ex.StatusCode);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Error al enviar un aviso push del hogar {HogarId}.", aviso.HogarId);
        }
    }
}
