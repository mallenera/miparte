using System.Net;
using System.Text;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Tests;

public class ComprobadorContrasenasTests
{
    // SHA-1 de «password»: 5BAA6 es el prefijo que sale del navegador; el resto se busca en la respuesta.
    private const string Prefijo = "5BAA6";
    private const string Sufijo = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";

    /// <summary>Manejador que responde con un cuerpo fijo y guarda la última ruta pedida.</summary>
    private sealed class Manejador(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticion = request;
            return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "text/plain") });
        }
    }

    private static (ComprobadorContrasenas comprobador, Manejador manejador) Crear(HttpStatusCode estado, string cuerpo)
    {
        var manejador = new Manejador(estado, cuerpo);
        var http = new HttpClient(manejador) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") };
        return (new ComprobadorContrasenas(http), manejador);
    }

    [Fact]
    public async Task ContrasenaEnLaFiltracion_DevuelveTrue()
    {
        var (c, _) = Crear(HttpStatusCode.OK, $"0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n{Sufijo}:3861493\r\n");

        Assert.True(await c.EstaFiltradaAsync("password"));
    }

    [Fact]
    public async Task ContrasenaAusente_DevuelveFalse()
    {
        var (c, _) = Crear(HttpStatusCode.OK, "0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n00000000000000000000000000000000000:2\r\n");

        Assert.False(await c.EstaFiltradaAsync("password"));
    }

    [Fact]
    public async Task SufijoRellenoConCuentaCero_NoCuentaComoFiltrada()
    {
        var (c, _) = Crear(HttpStatusCode.OK, $"{Sufijo}:0\r\n");

        Assert.False(await c.EstaFiltradaAsync("password"));
    }

    [Fact]
    public async Task PideSoloElPrefijoConRellenoActivado()
    {
        var (c, manejador) = Crear(HttpStatusCode.OK, "");

        await c.EstaFiltradaAsync("password");

        Assert.Equal($"/range/{Prefijo}", manejador.Peticion!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain(Sufijo, manejador.Peticion.RequestUri.ToString());
        Assert.Equal("true", manejador.Peticion.Headers.GetValues("Add-Padding").Single());
    }

    [Fact]
    public async Task ErrorDeLaApi_DevuelveNull_ParaNoBloquearElRegistro()
    {
        var (c, _) = Crear(HttpStatusCode.ServiceUnavailable, "");

        Assert.Null(await c.EstaFiltradaAsync("password"));
    }
}
