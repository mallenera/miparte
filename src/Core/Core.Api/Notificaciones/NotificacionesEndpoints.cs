using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Miembros;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Notificaciones;

/// <summary>
/// Endpoints de los avisos del navegador: clave pública VAPID y alta y baja de la suscripción del dispositivo. No exigen permiso:
/// cada miembro decide para sí. Solo traducen HTTP y delegan en <see cref="ServicioNotificaciones"/>.
/// </summary>
public static class NotificacionesEndpoints
{
    /// <summary>Registra GET /api/notificaciones/clave-publica y POST/DELETE /api/notificaciones/suscripcion. Requieren autenticación y hogar.</summary>
    public static IEndpointRouteBuilder MapNotificaciones(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notificaciones/clave-publica", ClavePublica).RequireAuthorization();
        app.MapPost("/api/notificaciones/suscripcion", SuscribirAsync).RequireAuthorization();
        app.MapDelete("/api/notificaciones/suscripcion", DesuscribirAsync).RequireAuthorization();
        return app;
    }

    /// <summary>GET /api/notificaciones/clave-publica: 200 con la clave VAPID pública, o 404 si el servidor no tiene avisos configurados.</summary>
    private static IResult ClavePublica([FromServices] IOptions<OpcionesPush> opciones)
    {
        var o = opciones.Value;
        return o.Configurado
            ? Results.Ok(new ClavePublicaPushResponse(o.VapidPublica))
            : Results.NotFound(new { error = "Los avisos del navegador no están configurados en el servidor." });
    }

    /// <summary>POST /api/notificaciones/suscripcion: 204 si queda registrada; 400 con datos inválidos; 403 si no eres miembro activo.</summary>
    private static async Task<IResult> SuscribirAsync(
        SuscripcionPushRequest? req, HttpContext ctx, [FromServices] ServicioNotificaciones servicio,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!MiembrosEndpoints.TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (req is null) return ApiComun.Invalido("Falta la suscripción.");

        var r = await servicio.SuscribirAsync(userId, hogarId, new DatosSuscripcionPush(req.Endpoint, req.P256dh, req.Auth), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>DELETE /api/notificaciones/suscripcion: 204 también si ya no existía (idempotente).</summary>
    private static async Task<IResult> DesuscribirAsync(
        [FromBody] BajaSuscripcionPushRequest? req, HttpContext ctx, [FromServices] ServicioNotificaciones servicio,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!MiembrosEndpoints.TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (string.IsNullOrWhiteSpace(req?.Endpoint)) return ApiComun.Invalido("Falta el endpoint de la suscripción.");

        var r = await servicio.DesuscribirAsync(userId, req.Endpoint, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
