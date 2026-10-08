using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// Configuración de <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> tras un proxy inverso. Sin ella
/// <c>RemoteIpAddress</c> es la IP del proxy; con ella mal puesta cualquier cliente falsearía su IP y eludiría
/// el limitador, así que por defecto no se activa y nunca se confía a ciegas salvo opt-in explícito.
/// </summary>
public static class ReenvioCabeceras
{
    /// <summary>
    /// Devuelve las opciones de reenvío según la configuración, o <c>null</c> si no hay que reenviar nada:
    /// <c>ProxyInverso:Proxies</c> (IPs) y <c>ProxyInverso:Redes</c> (CIDR, p. ej. <c>10.0.0.0/8</c>) limitan quién
    /// puede reenviar cabeceras; <c>ProxyInverso:Confiar</c> sin listas acepta a cualquier remitente directo y solo
    /// es válido si el contenedor únicamente es alcanzable a través del proxy (Render). Con listas, <c>Confiar</c>
    /// no las amplía. En todos los casos <c>ForwardLimit = 1</c> toma la última IP, la añadida por el proxy de confianza.
    /// </summary>
    /// <exception cref="InvalidOperationException">Una IP o red configurada no es válida (fallar al arrancar es más seguro que ignorarla).</exception>
    public static ForwardedHeadersOptions? Crear(IConfiguration configuracion)
    {
        var proxies = Lista(configuracion, "ProxyInverso:Proxies");
        var redes = Lista(configuracion, "ProxyInverso:Redes");
        var confiarEnTodos = configuracion.GetValue<bool>("ProxyInverso:Confiar");
        if (proxies.Length == 0 && redes.Length == 0 && !confiarEnTodos) return null;

        var opciones = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };
        opciones.KnownIPNetworks.Clear();
        opciones.KnownProxies.Clear();
        foreach (var ip in proxies)
        {
            opciones.KnownProxies.Add(IPAddress.TryParse(ip, out var direccion)
                ? direccion
                : throw new InvalidOperationException($"ProxyInverso:Proxies contiene una IP no válida: '{ip}'."));
        }
        foreach (var red in redes)
        {
            opciones.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(red, out var n)
                ? n
                : throw new InvalidOperationException($"ProxyInverso:Redes contiene una red CIDR no válida: '{red}'."));
        }
        return opciones;
    }

    private static string[] Lista(IConfiguration configuracion, string seccion)
        => configuracion.GetSection(seccion).Get<string[]>()?
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray() ?? [];
}
