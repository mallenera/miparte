using MiParte.Web.Autenticacion;

namespace MiParte.Web.Demo;

/// <summary>
/// Último eslabón antes de la red: si la sesión es la del modo demo local contesta con <see cref="ServidorDemo"/> en lugar
/// de llamar a Core.Api; con cualquier otra sesión no interviene.
/// </summary>
public sealed class ManejadorDemo : DelegatingHandler
{
    private readonly ServicioSesion _sesion;
    private readonly ServidorDemo _servidor;

    /// <summary>Crea el manejador.</summary>
    /// <param name="sesion">Servicio de sesión, para saber si el modo demo local está activo.</param>
    /// <param name="servidor">Servidor en memoria que responde en modo demo.</param>
    public ManejadorDemo(ServicioSesion sesion, ServidorDemo servidor)
    {
        _sesion = sesion;
        _servidor = servidor;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _sesion.EsDemoLocal
            ? _servidor.ResponderAsync(request, cancellationToken)
            : base.SendAsync(request, cancellationToken);
}
