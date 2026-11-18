using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiParte.Seguridad;

/// <summary>
/// Deja en el log la duración de cada petición y el tiempo de arranque del servicio, para medir los tiempos de espera reales
/// en los logs de Render (incluidos los arranques en frío del plan gratuito). Solo registra método, plantilla de ruta (sin ids
/// ni query string), código de estado y milisegundos: nada del usuario ni de sus datos. Las rutas <c>/health</c> no se registran:
/// las llama el monitor externo cada pocos minutos y él ya guarda sus propios tiempos.
/// </summary>
public static class RegistroPeticiones
{
    /// <summary>Categoría del log; permite filtrar estas líneas o ajustar su nivel con <c>Logging__LogLevel__MiParte.Peticiones</c>.</summary>
    public const string Categoria = "MiParte.Peticiones";

    /// <summary>Umbral por defecto a partir del cual una petición se registra como aviso (configurable en <c>Registro:PeticionLentaMs</c>).</summary>
    public const int PeticionLentaMsPorDefecto = 1000;

    /// <summary>
    /// Registra el middleware de medición. Va al principio del pipeline para contar también los rechazos de CORS, autenticación y
    /// limitador. Una petición que tarda al menos <c>Registro:PeticionLentaMs</c> sale como aviso; el resto, como información.
    /// </summary>
    /// <param name="app">Aplicación.</param>
    public static WebApplication UseRegistroPeticiones(this WebApplication app)
    {
        var umbral = app.Configuration.GetValue("Registro:PeticionLentaMs", PeticionLentaMsPorDefecto);
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Categoria);

        // Desde que arranca el proceso hasta que acepta peticiones: en Render es casi todo el arranque en frío que sufre el usuario.
        app.Lifetime.ApplicationStarted.Register(() => log.LogInformation(
            "Servicio listo {Ms:0} ms después de arrancar el proceso",
            (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds));

        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            var inicio = Stopwatch.GetTimestamp();
            try
            {
                await next();
            }
            finally
            {
                var ms = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
                // La plantilla agrupa todas las peticiones de un endpoint y no expone ids; sin endpoint (404, preflight) va genérica.
                // Los grupos de rutas dejan una barra final («/api/gastos/»): se quita para que un endpoint salga siempre igual.
                var ruta = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText is { Length: > 1 } plantilla
                    ? plantilla.TrimEnd('/')
                    : "(sin ruta)";
                log.Log(ms >= umbral ? LogLevel.Warning : LogLevel.Information,
                    "Petición {Metodo} {Ruta} -> {Estado} en {Ms:0} ms", ctx.Request.Method, ruta, ctx.Response.StatusCode, ms);
            }
        });
        return app;
    }
}
