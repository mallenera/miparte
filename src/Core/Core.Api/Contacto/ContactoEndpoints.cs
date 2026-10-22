using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Contacto;

namespace MiParte.Core.Api.Contacto;

/// <summary>
/// Formulario de contacto: el único endpoint anónimo de la API además de <c>/health</c>, porque la página de contacto es pública. Solo
/// traduce HTTP y delega en <see cref="ServicioContacto"/>; lo frena la política <see cref="LimitacionPeticiones.Contacto"/>.
/// </summary>
public static class ContactoEndpoints
{
    /// <summary>Registra POST /api/contacto: sin autenticación ni hogar y con su propio límite de peticiones.</summary>
    public static IEndpointRouteBuilder MapContacto(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/contacto", EnviarAsync)
            .AllowAnonymous()
            .WithMetadata(new SinHogarActual())
            .RequireRateLimiting(LimitacionPeticiones.Contacto);
        return app;
    }

    /// <summary>POST /api/contacto: 204 si el mensaje se envía; 400 con datos inválidos; 503 si el envío no está configurado o falla.</summary>
    private static async Task<IResult> EnviarAsync(ContactoRequest? req, [FromServices] ServicioContacto servicio, CancellationToken ct)
    {
        if (req is null) return ApiComun.Invalido("Falta el mensaje.");

        var r = await servicio.EnviarAsync(new MensajeContacto(req.Motivo, req.Email, req.Mensaje, req.Nombre, req.Web), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
