using Microsoft.AspNetCore.Mvc;

namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// Convierte las excepciones no controladas en un 500 JSON dentro del pipeline, después del CORS. Sin esto Kestrel
/// descarta las cabeceras ya escritas y el navegador, al no ver <c>Access-Control-Allow-Origin</c>, muestra un error
/// de CORS que oculta el fallo real (p. ej. la base de datos caída).
/// </summary>
public static class ErroresInesperados
{
    /// <summary>
    /// Captura lo que no haya resuelto un endpoint, lo registra con su traza y responde
    /// un <c>500</c> <see cref="ProblemDetails"/> (<c>application/problem+json</c>) en español sin detalles internos.
    /// Va tras <c>UseCors</c> para conservar sus cabeceras.
    /// </summary>
    public static IApplicationBuilder UseErroresInesperados(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (!ctx.RequestAborted.IsCancellationRequested && !ctx.Response.HasStarted)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("MiParte.Core.Api.Errores")
                    .LogError(ex, "Excepción no controlada en {Metodo} {Ruta}", ctx.Request.Method, ctx.Request.Path);
                // Cabeceras de cuerpo que el endpoint pudo fijar antes de fallar: describirían otro cuerpo. Las demás
                // (CORS, seguridad) se conservan a propósito.
                ctx.Response.Headers.ContentLength = null;
                ctx.Response.Headers.Remove("Content-Encoding");
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var problema = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Error interno del servidor.",
                    Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                };
                // «error» mantiene el contrato de los demás errores de la API (el front lee ese campo).
                problema.Extensions["error"] = problema.Title;
                problema.Extensions["traceId"] = ctx.TraceIdentifier;
                await ctx.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
            }
        });
}
