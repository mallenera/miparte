using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MiParte.Assistant.Api.Seguridad;

/// <summary>
/// Defensas transversales de Assistant.Api: cabeceras, límite de peticiones por usuario (cada pregunta cuesta tokens
/// de pago), tope del cuerpo y manejo de errores sin filtrar detalles internos.
/// </summary>
public static class SeguridadAsistente
{
    /// <summary>Política de límite para <c>POST /api/chat</c>.</summary>
    public const string PoliticaChat = "chat";

    /// <summary>Segmentos de la ventana de un minuto.</summary>
    private const int Segmentos = 6;

    /// <summary>Quita la cabecera <c>Server</c> y acota el cuerpo de las peticiones.</summary>
    /// <param name="builder">Constructor de la aplicación.</param>
    /// <param name="opciones">Opciones ya leídas del asistente.</param>
    public static WebApplicationBuilder AddSeguridadAsistente(this WebApplicationBuilder builder, OpcionesAsistente opciones)
    {
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = opciones.MaxCuerpoBytes;
        });

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Demasiadas preguntas seguidas. Espera un momento antes de volver a preguntar." }, ct);
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                ctx => Ventana("global:" + Clave(ctx), 60));
            o.AddPolicy(PoliticaChat, ctx => Ventana("chat:" + Clave(ctx), opciones.MensajesPorMinuto));
        });
        return builder;
    }

    /// <summary>Cabeceras de una API JSON: nada se interpreta como documento ni se guarda en cachés.</summary>
    /// <param name="app">Aplicación.</param>
    public static IApplicationBuilder UseCabecerasSeguridad(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h.TryAdd("X-Content-Type-Options", "nosniff");
                h.TryAdd("Cache-Control", "no-store");
                h.TryAdd("Referrer-Policy", "no-referrer");
                h.TryAdd("X-Frame-Options", "DENY");
                h.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>Convierte cualquier excepción no controlada en un 500 genérico (la traza queda en el log).</summary>
    /// <param name="app">Aplicación.</param>
    public static IApplicationBuilder UseErroresInesperados(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (!ctx.Response.HasStarted && ex is not OperationCanceledException)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Asistente")
                    .LogError(ex, "Error no controlado en {Ruta}", ctx.Request.Path);
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ctx.Response.WriteAsJsonAsync(new { error = "Error interno del servidor." });
            }
        });

    private static string Clave(HttpContext ctx)
        => ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo";

    private static RateLimitPartition<string> Ventana(string clave, int porMinuto)
        => RateLimitPartition.GetSlidingWindowLimiter(clave, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, porMinuto),
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = Segmentos,
            QueueLimit = 0,
        });
}
