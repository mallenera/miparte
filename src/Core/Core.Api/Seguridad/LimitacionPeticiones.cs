using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// Límites de peticiones de Core.Api, configurables en la sección <c>Limites</c> (variables
/// <c>Limites__PeticionesPorMinuto</c>, <c>Limites__CostosasPorMinuto</c> y <c>Limites__MaxCuerpoBytes</c>).
/// </summary>
public sealed class OpcionesLimites
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Limites";

    /// <summary>Peticiones por minuto y usuario (o por IP si no hay sesión) en toda la API.</summary>
    public int PeticionesPorMinuto { get; set; } = 120;

    /// <summary>Peticiones por minuto y usuario en los endpoints costosos o sensibles a abuso (<see cref="LimitacionPeticiones.Costosa"/>).</summary>
    public int CostosasPorMinuto { get; set; } = 10;

    /// <summary>Tamaño máximo del cuerpo de una petición; los JSON de la API son de unos pocos KB.</summary>
    public long MaxCuerpoBytes { get; set; } = 64 * 1024;
}

/// <summary>Registro del limitador de peticiones (por usuario, con la IP como respaldo) y del tope de tamaño del cuerpo.</summary>
public static class LimitacionPeticiones
{
    /// <summary>
    /// Política con un tope más estricto para operaciones caras o abusables: crear hogares, generar recurrentes
    /// del mes y crear o aceptar invitaciones (esta última, además, frena el sondeo de tokens).
    /// </summary>
    public const string Costosa = "costosa";

    /// <summary>Límite de peticiones simultáneas en cola: ninguna, se rechaza directamente con 429.</summary>
    private const int SinCola = 0;

    /// <summary>Segmentos de la ventana de un minuto.</summary>
    private const int Segmentos = 6;

    /// <summary>
    /// Espera que se indica en <c>Retry-After</c> cuando el limitador no la calcula: la ventana entera. Quien agota la
    /// cuota en un solo segmento no recupera permisos hasta ~50-60 s después, así que avisar de menos provocaría un
    /// segundo 429 al reintentar.
    /// </summary>
    private const int SegundosVentana = 60;

    /// <summary>
    /// Registra el limitador global y la política <see cref="Costosa"/>, y baja el tope de tamaño del cuerpo de
    /// Kestrel. La partición es el <c>sub</c> del JWT; sin sesión, la IP remota. Detrás de un proxy hay que
    /// configurar <c>ForwardedHeaders</c> para que la IP sea la del cliente y no la del proxy.
    /// </summary>
    public static WebApplicationBuilder AddLimitacionPeticiones(this WebApplicationBuilder builder)
    {
        var opciones = builder.Configuration.GetSection(OpcionesLimites.Seccion).Get<OpcionesLimites>() ?? new();

        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = opciones.MaxCuerpoBytes);

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                // La ventana deslizante no informa del tiempo de espera: se indica la ventana completa (lo seguro).
                var segundos = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera)
                    ? (int)Math.Ceiling(espera.TotalSeconds)
                    : SegundosVentana;
                ctx.HttpContext.Response.Headers.RetryAfter = segundos.ToString();
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Demasiadas peticiones. Espera un momento antes de volver a intentarlo." }, ct);
            };

            // Sin política propia todas las rutas pasan por este; /health lo desactiva con DisableRateLimiting.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                ctx => Ventana("global:" + Clave(ctx), opciones.PeticionesPorMinuto));

            o.AddPolicy(Costosa, ctx => Ventana("costosa:" + Clave(ctx), opciones.CostosasPorMinuto));
        });

        return builder;
    }

    /// <summary>Ventana deslizante de un minuto (en segmentos) para la clave dada, sin cola de espera.</summary>
    private static RateLimitPartition<string> Ventana(string clave, int limite)
        => RateLimitPartition.GetSlidingWindowLimiter(clave, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limite,
            Window = TimeSpan.FromSeconds(SegundosVentana),
            SegmentsPerWindow = Segmentos,
            QueueLimit = SinCola,
        });

    /// <summary>Identidad a la que se imputa la petición: el usuario autenticado o, si no lo hay, su IP.</summary>
    private static string Clave(HttpContext ctx)
        => ctx.User.FindFirst("sub")?.Value is { Length: > 0 } sub
            ? "u:" + sub
            : "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida");
}
