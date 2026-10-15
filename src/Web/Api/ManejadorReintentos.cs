using System.Net;

namespace MiParte.Web.Api;

/// <summary>
/// Reintenta con espera creciente las lecturas (GET/HEAD) a Core.Api cuando el servidor no está disponible: fallo de red
/// o 502/503/504, que es lo que se ve mientras Render reinicia el servicio tras un despliegue o lo despierta. También espera
/// y reintenta (hasta <see cref="MaxReintentosPorLimite"/> veces) si la API responde 429 por exceso de peticiones, según
/// <c>Retry-After</c>. Mientras espera marca <see cref="ServicioConexion.Reintentando"/> para que la interfaz lo explique. Las escrituras no se
/// reintentan: no se sabe si la primera llegó a procesarse y podrían duplicarse.
/// </summary>
public sealed class ManejadorReintentos : DelegatingHandler
{
    /// <summary>Esperas antes de cada reintento (~1 min en total: lo que tarda en despertar el plan gratuito de Render).</summary>
    public static readonly TimeSpan[] EsperasPorDefecto =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
         TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(20)];

    private readonly ServicioConexion _conexion;
    private readonly TimeProvider _tiempo;

    /// <summary>Crea el manejador.</summary>
    /// <param name="conexion">Estado de la conexión que se actualiza mientras se reintenta.</param>
    /// <param name="tiempo">Reloj usado para las esperas.</param>
    public ManejadorReintentos(ServicioConexion conexion, TimeProvider tiempo)
    {
        _conexion = conexion;
        _tiempo = tiempo;
    }

    /// <summary>Esperas antes de cada reintento; su longitud es el número de reintentos.</summary>
    public IReadOnlyList<TimeSpan> Esperas { get; init; } = EsperasPorDefecto;

    /// <summary>Veces que se reintenta una lectura rechazada con 429 antes de devolver la respuesta tal cual.</summary>
    public const int MaxReintentosPorLimite = 2;

    /// <summary>
    /// Tope de lo que se espera por un 429, aunque <c>Retry-After</c> pida más: la ventana de Core.Api es de un minuto y
    /// bloquear la pantalla tanto es peor que enseñar el mensaje de la API y dejar que la persona reintente.
    /// </summary>
    public TimeSpan EsperaMaximaPorLimite { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Espera usada con un 429 sin <c>Retry-After</c> (antes de limitarla a <see cref="EsperaMaximaPorLimite"/>).</summary>
    private static readonly TimeSpan EsperaPorLimiteSinIndicacion = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return await base.SendAsync(request, cancellationToken);

        var reintentando = false;
        var limitado = false;
        var intento = 0;
        var intentosPorLimite = 0;
        try
        {
            while (true)
            {
                HttpResponseMessage? respuesta = null;
                TimeSpan espera;
                try
                {
                    // Una petición no se puede enviar dos veces: cada intento va con una copia de la original.
                    respuesta = await base.SendAsync(Copiar(request), cancellationToken);
                    if (respuesta.StatusCode == HttpStatusCode.TooManyRequests && intentosPorLimite < MaxReintentosPorLimite)
                    {
                        // La API pidió espaciar las peticiones: se espera lo que indica (con tope) y se vuelve a intentar.
                        espera = EsperaPorLimite(respuesta);
                        intentosPorLimite++;
                        respuesta.Dispose();
                        if (!limitado)
                        {
                            limitado = true;
                            _conexion.Empezar(porLimite: true);
                        }

                        await Task.Delay(espera, _tiempo, cancellationToken);
                        continue;
                    }

                    if (!NoDisponible(respuesta.StatusCode) || intento >= Esperas.Count) return respuesta;
                    espera = Esperas[intento];
                }
                catch (HttpRequestException) when (intento < Esperas.Count && !cancellationToken.IsCancellationRequested)
                {
                    espera = Esperas[intento];
                }

                respuesta?.Dispose();
                if (!reintentando)
                {
                    reintentando = true;
                    _conexion.Empezar();
                }

                await Task.Delay(espera, _tiempo, cancellationToken);
                intento++;
            }
        }
        finally
        {
            if (reintentando) _conexion.Terminar();
            if (limitado) _conexion.Terminar(porLimite: true);
        }
    }

    /// <summary>Lo que hay que esperar según <c>Retry-After</c> (segundos o fecha), con <see cref="EsperaMaximaPorLimite"/> como tope.</summary>
    private TimeSpan EsperaPorLimite(HttpResponseMessage respuesta)
    {
        var ahora = _tiempo.GetUtcNow();
        var pedida = respuesta.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            // Una fecha ya vencida no es «espera cero»: es una indicación incoherente y se usa la espera por defecto.
            { Date: { } fecha } => fecha > ahora ? fecha - ahora : EsperaPorLimiteSinIndicacion,
            _ => EsperaPorLimiteSinIndicacion,
        };
        if (pedida < TimeSpan.Zero) pedida = TimeSpan.Zero;
        return pedida < EsperaMaximaPorLimite ? pedida : EsperaMaximaPorLimite;
    }

    private static bool NoDisponible(HttpStatusCode codigo) =>
        codigo is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static HttpRequestMessage Copiar(HttpRequestMessage original)
    {
        var copia = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version, VersionPolicy = original.VersionPolicy };
        foreach (var cabecera in original.Headers) copia.Headers.TryAddWithoutValidation(cabecera.Key, cabecera.Value);
        foreach (var opcion in original.Options) ((IDictionary<string, object?>)copia.Options).Add(opcion);
        return copia;
    }
}
