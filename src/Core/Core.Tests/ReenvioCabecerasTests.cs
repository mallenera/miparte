using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Core.Api.Seguridad;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class ReenvioCabecerasTests
{
    private static IConfiguration Config(params (string, string)[] valores)
        => new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => KeyValuePair.Create(v.Item1, (string?)v.Item2))).Build();

    [Fact]
    public void SinConfiguracion_NoReenvia()
        => Assert.Null(ReenvioCabeceras.Crear(Config()));

    [Fact]
    public void ConfiarSinListas_AceptaCualquierRemitenteDirecto()
    {
        var o = ReenvioCabeceras.Crear(Config(("ProxyInverso:Confiar", "true")))!;
        Assert.Empty(o.KnownProxies);
        Assert.Empty(o.KnownIPNetworks);
        Assert.Equal(1, o.ForwardLimit);
    }

    [Fact]
    public void ConProxiesYRedes_SoloEsosRemitentesSonDeConfianza()
    {
        var o = ReenvioCabeceras.Crear(Config(
            ("ProxyInverso:Proxies:0", "198.51.100.7"),
            ("ProxyInverso:Redes:0", "10.0.0.0/8")))!;
        Assert.Equal(IPAddress.Parse("198.51.100.7"), Assert.Single(o.KnownProxies));
        Assert.Single(o.KnownIPNetworks);
        Assert.Equal(1, o.ForwardLimit);
    }

    [Theory]
    [InlineData("ProxyInverso:Proxies:0", "no-es-una-ip")]
    [InlineData("ProxyInverso:Redes:0", "10.0.0.0/99")]
    public void ValorInvalido_FallaAlArrancar(string clave, string valor)
        => Assert.Throws<InvalidOperationException>(() => ReenvioCabeceras.Crear(Config((clave, valor))));

    /// <summary>Fija la IP del remitente directo (TestServer no tiene) antes de todo el pipeline.</summary>
    private sealed class RemitenteFilter(string ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use((ctx, sig) =>
                {
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                    return sig();
                });
                next(app);
            };
    }

    private static async Task<HttpStatusCode> AnonimaDesde(HttpClient c, string ip)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/yo");
        peticion.Headers.Add("X-Forwarded-For", ip);
        return (await c.SendAsync(peticion)).StatusCode;
    }

    private static WebApplicationFactory<Program> Fabrica(string remitente, string proxyConfiable)
        => Crear(Secreto).WithWebHostBuilder(b =>
        {
            b.UseSetting("Limites:PeticionesPorMinuto", "1");
            b.UseSetting("ProxyInverso:Proxies:0", proxyConfiable);
            b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new RemitenteFilter(remitente)));
        });

    [Fact]
    public async Task RemitenteEnLaListaDeProxies_SeUsaLaIpReenviada()
    {
        using var f = Fabrica("198.51.100.7", "198.51.100.7");
        var c = f.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await AnonimaDesde(c, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await AnonimaDesde(c, "203.0.113.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await AnonimaDesde(c, "203.0.113.2"));
    }

    [Fact]
    public async Task RemitenteFueraDeLaLista_SeIgnoraXForwardedFor()
    {
        using var f = Fabrica("192.0.2.50", "198.51.100.7");
        var c = f.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await AnonimaDesde(c, "203.0.113.1"));
        // Cambiar la cabecera no da un cubo nuevo: el cliente no es un proxy de confianza.
        Assert.Equal(HttpStatusCode.TooManyRequests, await AnonimaDesde(c, "203.0.113.2"));
    }
}
