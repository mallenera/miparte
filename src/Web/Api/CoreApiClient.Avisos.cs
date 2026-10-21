using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Avisos del navegador: clave pública VAPID y suscripción de este dispositivo.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Clave pública VAPID para suscribir este navegador a los avisos (<c>GET /api/notificaciones/clave-publica</c>). 404 si el servidor no tiene avisos configurados.</summary>
    /// <param name="ct">Token de cancelación.</param>
    public async Task<string> ObtenerClaveAvisosAsync(CancellationToken ct = default) =>
        (await _nucleo.ObtenerAsync<ClavePublicaPushResponse>("api/notificaciones/clave-publica", ct)).ClavePublica;

    /// <summary>Registra la suscripción de este dispositivo a los avisos (<c>POST /api/notificaciones/suscripcion</c>).</summary>
    /// <param name="peticion">Endpoint y claves que devuelve el navegador al suscribirse.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task GuardarSuscripcionAvisosAsync(SuscripcionPushRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Post, "api/notificaciones/suscripcion", ct, peticion);

    /// <summary>Quita la suscripción de este dispositivo (<c>DELETE /api/notificaciones/suscripcion</c>). No falla si ya no existía.</summary>
    /// <param name="endpoint">Endpoint de la suscripción que se quita.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task QuitarSuscripcionAvisosAsync(string endpoint, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, "api/notificaciones/suscripcion", ct, new BajaSuscripcionPushRequest(endpoint));
}
