using System.Net;

namespace MiParte.Web.Api;

/// <summary>
/// Reintenta con espera creciente las lecturas (GET/HEAD) a Core.Api cuando el servidor no está disponible: fallo de red
/// o 502/503/504, que es lo que se ve mientras Render reinicia el servicio tras un despliegue o lo despierta. Mientras
/// espera marca <see cref="ServicioConexion.Reintentando"/> para que la interfaz lo explique. Las escrituras no se
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

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return await base.SendAsync(request, cancellationToken);

        var reintentando = false;
        try
        {
            for (var intento = 0; ; intento++)
            {
                HttpResponseMessage? respuesta = null;
                try
                {
                    // Una petición no se puede enviar dos veces: cada intento va con una copia de la original.
                    respuesta = await base.SendAsync(Copiar(request), cancellationToken);
                    if (!NoDisponible(respuesta.StatusCode) || intento >= Esperas.Count) return respuesta;
                }
                catch (HttpRequestException) when (intento < Esperas.Count && !cancellationToken.IsCancellationRequested)
                {
                }

                respuesta?.Dispose();
                if (!reintentando)
                {
                    reintentando = true;
                    _conexion.Empezar();
                }
                await Task.Delay(Esperas[intento], _tiempo, cancellationToken);
            }
        }
        finally
        {
            if (reintentando) _conexion.Terminar();
        }
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
