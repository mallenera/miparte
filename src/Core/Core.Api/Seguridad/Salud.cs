using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// Comprobación de disponibilidad para el monitor externo (UptimeRobot). <c>/health</c> solo dice que el proceso responde;
/// <c>/health/listo</c> dice además si la base de datos contesta, que es lo que el usuario necesita para usar la app.
/// </summary>
public static class Salud
{
    /// <summary>Ruta de la comprobación con base de datos.</summary>
    public const string RutaListo = "/health/listo";

    /// <summary>Tiempo máximo de la comprobación: más allá, para el usuario la app ya está caída.</summary>
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    /// <summary>Registra la comprobación de la base de datos.</summary>
    /// <param name="builder">Constructor de la aplicación.</param>
    public static WebApplicationBuilder AddSalud(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks().AddCheck<ComprobacionBaseDatos>("base-de-datos", timeout: Espera);
        return builder;
    }

    /// <summary>
    /// <c>GET /health/listo</c>, anónimo: 200 <c>{ service, status: "ok" }</c> si la base de datos responde y 503 con
    /// <c>status: "caido"</c> si no. No da detalles del fallo (están en el log). Mantiene el limitador de peticiones porque, a
    /// diferencia de <c>/health</c>, cada llamada abre una conexión.
    /// </summary>
    /// <param name="app">Aplicación.</param>
    public static IEndpointRouteBuilder MapSalud(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks(RutaListo, new HealthCheckOptions
        {
            ResponseWriter = (ctx, informe) => ctx.Response.WriteAsJsonAsync(new
            {
                service = "core",
                status = informe.Status == HealthStatus.Healthy ? "ok" : "caido",
            }),
        });
        return app;
    }

    /// <summary>Comprueba que se puede abrir una conexión a la base de datos (sin consultar datos).</summary>
    /// <param name="servicios">Servicios del ámbito de la comprobación; sin cadena de conexión no hay contexto registrado.</param>
    /// <param name="logger">Log donde queda el motivo de un fallo.</param>
    internal sealed class ComprobacionBaseDatos(IServiceProvider servicios, ILogger<ComprobacionBaseDatos> logger) : IHealthCheck
    {
        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (servicios.GetService<MiParteDbContext>() is not { } db)
                return HealthCheckResult.Unhealthy("Sin base de datos configurada (ConnectionStrings__Default).");
            try
            {
                if (await db.Database.CanConnectAsync(cancellationToken)) return HealthCheckResult.Healthy();
                logger.LogError("Comprobación de salud: la base de datos no acepta conexiones");
                return HealthCheckResult.Unhealthy("La base de datos no acepta conexiones.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Comprobación de salud: error al conectar con la base de datos");
                return HealthCheckResult.Unhealthy("Error al conectar con la base de datos.");
            }
        }
    }
}
