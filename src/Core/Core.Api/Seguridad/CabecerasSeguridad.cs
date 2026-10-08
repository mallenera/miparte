namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// Cabeceras de seguridad de las respuestas de Core.Api. Son una API JSON, así que la política es la más
/// restrictiva posible: nada se interpreta como documento y los datos del hogar no se guardan en cachés
/// compartidas ni del navegador. HSTS y la redirección a HTTPS son cosa del proxy que termina el TLS.
/// </summary>
public static class CabecerasSeguridad
{
    /// <summary>Quita la cabecera <c>Server: Kestrel</c>, que solo informa al atacante de la tecnología.</summary>
    public static WebApplicationBuilder AddCabecerasSeguridad(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);
        return builder;
    }

    /// <summary>
    /// Añade las cabeceras a toda respuesta, incluidas las de error del propio pipeline (401, 429...), por eso va el
    /// primero. No pisa una cabecera que el endpoint haya fijado.
    /// </summary>
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
}
