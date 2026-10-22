using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MiParte.Core.Application.Contacto;
using MiParte.Core.Infrastructure.Correo;

namespace MiParte.Core.Tests;

public class EnviadorCorreoResendTests
{
    private sealed class ManejadorFalso(HttpStatusCode codigo) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }
        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticion = request;
            Cuerpo = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(codigo);
        }
    }

    private static readonly OpcionesCorreo Opciones = new()
    {
        ResendApiKey = "re_prueba", Remitente = "Mi parte, tu parte <onboarding@resend.dev>", Destino = "buzon@ejemplo.es",
    };

    private static EnviadorCorreoResend Enviador(ManejadorFalso manejador, OpcionesCorreo? opciones = null)
        => new(new HttpClient(manejador) { BaseAddress = EnviadorCorreoResend.UrlBase }, opciones ?? Opciones,
            NullLogger<EnviadorCorreoResend>.Instance);

    [Fact]
    public async Task Enviar_PublicaEnEmailsConLaClaveYElDestinoDeLaConfiguracion()
    {
        var manejador = new ManejadorFalso(HttpStatusCode.OK);

        var ok = await Enviador(manejador).EnviarAsync(new CorreoContacto("Asunto", "Texto", "ana@ejemplo.es"), default);

        Assert.True(ok);
        Assert.Equal(HttpMethod.Post, manejador.Peticion!.Method);
        Assert.Equal("https://api.resend.com/emails", manejador.Peticion.RequestUri!.ToString());
        Assert.Equal("Bearer re_prueba", manejador.Peticion.Headers.Authorization!.ToString());
        Assert.NotEmpty(manejador.Peticion.Headers.UserAgent);
        using var json = JsonDocument.Parse(manejador.Cuerpo!);
        var raiz = json.RootElement;
        Assert.Equal("Mi parte, tu parte <onboarding@resend.dev>", raiz.GetProperty("from").GetString());
        Assert.Equal("buzon@ejemplo.es", raiz.GetProperty("to")[0].GetString());
        Assert.Equal("Asunto", raiz.GetProperty("subject").GetString());
        Assert.Equal("Texto", raiz.GetProperty("text").GetString());
        Assert.Equal("ana@ejemplo.es", raiz.GetProperty("reply_to").GetString());
    }

    [Fact]
    public async Task Enviar_SiResendRechaza_DevuelveFalse()
    {
        var ok = await Enviador(new ManejadorFalso(HttpStatusCode.Forbidden))
            .EnviarAsync(new CorreoContacto("Asunto", "Texto", "ana@ejemplo.es"), default);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("", "buzon@ejemplo.es")]
    [InlineData("re_prueba", "")]
    public void SinClaveODestino_NoEstaDisponible(string clave, string destino)
    {
        var enviador = Enviador(new ManejadorFalso(HttpStatusCode.OK), new OpcionesCorreo { ResendApiKey = clave, Destino = destino });

        Assert.False(enviador.Disponible);
    }
}
