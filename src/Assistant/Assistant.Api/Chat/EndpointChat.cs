using Microsoft.AspNetCore.RateLimiting;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Assistant.Api.Seguridad;
using MiParte.Contracts;

namespace MiParte.Assistant.Api.Chat;

/// <summary>Endpoint del chat del asistente.</summary>
public static class EndpointChat
{
    /// <summary>Cabecera con el hogar sobre el que se pregunta (la misma que usa Core.Api).</summary>
    public const string CabeceraHogar = "X-Hogar-Id";

    /// <summary>Registra <c>POST /api/chat</c> (autenticado y con límite de preguntas por minuto).</summary>
    /// <param name="app">Aplicación.</param>
    public static void MapChat(this WebApplication app)
    {
        app.MapPost("/api/chat", ResponderAsync)
            .RequireAuthorization()
            .RequireRateLimiting(SeguridadAsistente.PoliticaChat);
    }

    private static async Task<IResult> ResponderAsync(
        HttpContext http, ChatRequest peticion, ServicioChat chat, IFabricaDatosHogar fabrica,
        ILogger<ServicioChat> log, CancellationToken ct)
    {
        // Hogar: debe indicarse siempre; la pertenencia la comprueba Core.Api al consultar con el JWT de la persona.
        if (!Guid.TryParse(http.Request.Headers[CabeceraHogar].ToString(), out var hogarId))
            return Error(StatusCodes.Status400BadRequest, "Falta la cabecera X-Hogar-Id con el hogar sobre el que preguntas.");

        var cabecera = http.Request.Headers.Authorization.ToString();
        var jwt = cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? cabecera[7..].Trim() : "";
        var datos = fabrica.Crear(jwt, hogarId);

        try
        {
            await datos.MiembrosAsync(ct); // 403 si no es miembro activo de ese hogar
            return Results.Ok(await chat.ResponderAsync(peticion?.Mensajes, datos, ct));
        }
        catch (SolicitudInvalidaException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (HogarNoAccesibleException ex)
        {
            return ex.Codigo == System.Net.HttpStatusCode.Unauthorized
                ? Error(StatusCodes.Status401Unauthorized, "La sesión ha caducado.")
                : Error(StatusCodes.Status403Forbidden, "No tienes acceso a ese hogar.");
        }
        catch (DatosNoDisponiblesException ex)
        {
            log.LogWarning(ex, "Core.Api no disponible para el asistente");
            return Error(StatusCodes.Status502BadGateway, "No he podido consultar los datos del hogar. Inténtalo de nuevo en un momento.");
        }
        catch (ModeloNoDisponibleException ex)
        {
            log.LogWarning(ex, "Modelo no disponible");
            return Error(StatusCodes.Status503ServiceUnavailable, "El asistente no está disponible ahora mismo.");
        }
    }

    private static IResult Error(int codigo, string mensaje) => Results.Json(new { error = mensaje }, statusCode: codigo);
}
