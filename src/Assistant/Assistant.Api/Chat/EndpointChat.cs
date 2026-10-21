using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Assistant.Api.Seguridad;
using MiParte.Contracts;

namespace MiParte.Assistant.Api.Chat;

/// <summary>Endpoints del asistente: el chat (completo o en streaming) y la confirmación de un gasto propuesto.</summary>
public static class EndpointChat
{
    /// <summary>Cabecera con el hogar sobre el que se pregunta (la misma que usa Core.Api).</summary>
    public const string CabeceraHogar = "X-Hogar-Id";

    /// <summary>Opciones de serialización de los eventos del streaming (camelCase, como el resto de la API).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registra el chat, su versión en streaming y la confirmación de gastos (autenticados y con límite de peticiones).</summary>
    /// <param name="app">Aplicación.</param>
    public static void MapChat(this WebApplication app)
    {
        app.MapPost("/api/chat", ResponderAsync)
            .RequireAuthorization()
            .RequireRateLimiting(SeguridadAsistente.PoliticaChat);
        app.MapPost("/api/chat/stream", ResponderStreamingAsync)
            .RequireAuthorization()
            .RequireRateLimiting(SeguridadAsistente.PoliticaChat);
        app.MapPost("/api/chat/confirmar-gasto", ConfirmarGastoAsync)
            .RequireAuthorization()
            .RequireRateLimiting(SeguridadAsistente.PoliticaChat);
    }

    private static async Task<IResult> ResponderAsync(
        HttpContext http, ChatRequest peticion, ServicioChat chat, IFabricaDatosHogar fabrica,
        ILogger<ServicioChat> log, CancellationToken ct)
    {
        if (!LeerHogar(http, out var hogarId, out var error)) return error!;
        var datos = DatosDe(http, fabrica, hogarId);

        try
        {
            await datos.MiembrosAsync(ct); // 403 si no es miembro activo de ese hogar
            return Results.Ok(await chat.ResponderAsync(peticion?.Mensajes, datos, ct));
        }
        catch (Exception ex) when (Traducir(ex, log) is { } e)
        {
            return Error(e.Codigo, e.Mensaje);
        }
    }

    /// <summary>
    /// <c>POST /api/chat/stream</c>: la misma conversación, con la respuesta en vivo como <c>text/event-stream</c>. Eventos:
    /// <c>texto</c> (<c>{ texto }</c>, trozos en orden), <c>fin</c> (la <see cref="ChatResponse"/> completa) y, si falla ya
    /// empezado el flujo, <c>error</c> (<c>{ error }</c>). Antes de empezar, los errores son respuestas JSON normales.
    /// </summary>
    private static async Task<IResult> ResponderStreamingAsync(
        HttpContext http, ChatRequest peticion, ServicioChat chat, IFabricaDatosHogar fabrica,
        ILogger<ServicioChat> log, CancellationToken ct)
    {
        if (!LeerHogar(http, out var hogarId, out var error)) return error!;
        var datos = DatosDe(http, fabrica, hogarId);
        var iniciado = false;

        async Task IniciarAsync()
        {
            if (iniciado) return;
            iniciado = true;
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no"; // que el proxy no retenga los eventos
            await http.Response.StartAsync(ct);
        }

        try
        {
            await datos.MiembrosAsync(ct);
            var respuesta = await chat.ResponderAsync(peticion?.Mensajes, datos, async texto =>
            {
                await IniciarAsync();
                await EscribirEventoAsync(http, "texto", new { texto }, ct);
            }, ct);
            await IniciarAsync(); // respuestas sin texto (p. ej. solo un rechazo) también llegan como «fin»
            await EscribirEventoAsync(http, "fin", respuesta, ct);
            return Results.Empty;
        }
        catch (Exception ex) when (Traducir(ex, log) is { } e)
        {
            if (!iniciado) return Error(e.Codigo, e.Mensaje);
            await EscribirEventoAsync(http, "error", new { error = e.Mensaje }, ct);
            return Results.Empty;
        }
    }

    /// <summary>
    /// <c>POST /api/chat/confirmar-gasto</c>: guarda el gasto que la persona confirma tras ver el borrador. Es la única vía
    /// de escritura del asistente; categoría y perfil los pone el servidor (los predeterminados del hogar).
    /// </summary>
    private static async Task<IResult> ConfirmarGastoAsync(
        HttpContext http, ConfirmarGastoRequest peticion, ServicioAltaGasto alta, IFabricaDatosHogar fabrica,
        ILogger<ServicioAltaGasto> log, CancellationToken ct)
    {
        if (!LeerHogar(http, out var hogarId, out var error)) return error!;
        if (peticion is null) return Error(StatusCodes.Status400BadRequest, "Faltan los datos del gasto.");
        var datos = DatosDe(http, fabrica, hogarId);

        try
        {
            await datos.MiembrosAsync(ct);
            return Results.Json(await alta.ConfirmarAsync(peticion, datos, ct), statusCode: StatusCodes.Status201Created);
        }
        catch (Exception ex) when (Traducir(ex, log) is { } e)
        {
            return Error(e.Codigo, e.Mensaje);
        }
    }

    /// <summary>Lee la cabecera de hogar; si falta o no es un identificador, devuelve el 400 correspondiente.</summary>
    private static bool LeerHogar(HttpContext http, out Guid hogarId, out IResult? error)
    {
        error = null;
        if (Guid.TryParse(http.Request.Headers[CabeceraHogar].ToString(), out hogarId)) return true;
        error = Error(StatusCodes.Status400BadRequest, "Falta la cabecera X-Hogar-Id con el hogar sobre el que preguntas.");
        return false;
    }

    /// <summary>Acceso a Core.Api con el JWT de la petición y el hogar indicado. La pertenencia la comprueba Core.Api.</summary>
    private static IDatosHogar DatosDe(HttpContext http, IFabricaDatosHogar fabrica, Guid hogarId)
    {
        var cabecera = http.Request.Headers.Authorization.ToString();
        var jwt = cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? cabecera[7..].Trim() : "";
        return fabrica.Crear(jwt, hogarId);
    }

    /// <summary>Código y mensaje para la persona de una excepción conocida; null si es un fallo inesperado (que sube como 500).</summary>
    private static (int Codigo, string Mensaje)? Traducir(Exception ex, ILogger log) => ex switch
    {
        SolicitudInvalidaException e => (StatusCodes.Status400BadRequest, e.Message),
        GastoRechazadoException e => ((int)e.Codigo, e.Message),
        HogarNoAccesibleException e when e.Codigo == System.Net.HttpStatusCode.Unauthorized
            => (StatusCodes.Status401Unauthorized, "La sesión ha caducado."),
        HogarNoAccesibleException => (StatusCodes.Status403Forbidden, "No tienes acceso a ese hogar."),
        DatosNoDisponiblesException e => Registrar(log, e, StatusCodes.Status502BadGateway,
            "No he podido consultar los datos del hogar. Inténtalo de nuevo en un momento."),
        ModeloNoDisponibleException e => Registrar(log, e, StatusCodes.Status503ServiceUnavailable,
            "El asistente no está disponible ahora mismo."),
        _ => null,
    };

    private static (int Codigo, string Mensaje) Registrar(ILogger log, Exception ex, int codigo, string mensaje)
    {
        log.LogWarning(ex, "Fallo del asistente ({Codigo})", codigo);
        return (codigo, mensaje);
    }

    private static async Task EscribirEventoAsync(HttpContext http, string nombre, object datos, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {nombre}\ndata: {JsonSerializer.Serialize(datos, Json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static IResult Error(int codigo, string mensaje) => Results.Json(new { error = mensaje }, statusCode: codigo);
}
