using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiParte.Core.Application.Contacto;
using static MiParte.Core.Tests.AutenticacionTests;
using static MiParte.Core.Tests.ServicioContactoTests;

namespace MiParte.Core.Tests;

/// <summary>Cableado de POST /api/contacto: anónimo, sin hogar, códigos de estado y su propio límite de peticiones.</summary>
public class ContactoHttpTests
{
    private static readonly object Valido = new { motivo = "duda", email = "ana@ejemplo.es", mensaje = "Una duda sobre el reparto." };

    private static WebApplicationFactory<Program> Fabrica(IEnviadorCorreo enviador, int porHora = 5)
        => Crear(Secreto).WithWebHostBuilder(b =>
        {
            b.UseSetting("Limites:ContactoPorHora", porHora.ToString());
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton(enviador)));
        });

    [Fact]
    public async Task SinSesion_EnviaElMensajeYResponde204()
    {
        var enviador = new EnviadorFalso();
        using var f = Fabrica(enviador);

        var r = await f.CreateClient().PostAsJsonAsync("/api/contacto", Valido);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Single(enviador.Enviados);
    }

    [Fact]
    public async Task ConSesionYVariosHogares_NoExigeElegirHogar()
    {
        var enviador = new EnviadorFalso();
        using var f = Fabrica(enviador);
        var usuario = Guid.NewGuid();
        await Miembro(f, usuario, Guid.NewGuid());
        await Miembro(f, usuario, Guid.NewGuid());

        var r = await Cliente(f, Token(Hs256(), usuario)).PostAsJsonAsync("/api/contacto", Valido);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
    }

    [Fact]
    public async Task ConDatosInvalidos_Responde400ConMensaje()
    {
        using var f = Fabrica(new EnviadorFalso());

        var r = await f.CreateClient().PostAsJsonAsync("/api/contacto", new { motivo = "duda", email = "no", mensaje = "Una duda sobre el reparto." });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("correo", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SinEnvioConfigurado_Responde503()
    {
        using var f = Fabrica(new EnviadorFalso(disponible: false));

        var r = await f.CreateClient().PostAsJsonAsync("/api/contacto", Valido);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
    }

    [Fact]
    public async Task SinConfiguracionDeCorreo_ElEnviadorRealNoEstaDisponible()
    {
        using var f = Crear(Secreto);

        var r = await f.CreateClient().PostAsJsonAsync("/api/contacto", Valido);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
    }

    [Fact]
    public async Task TrasElLimitePorHora_Responde429()
    {
        var enviador = new EnviadorFalso();
        using var f = Fabrica(enviador, porHora: 2);
        var c = f.CreateClient();

        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/contacto", Valido)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/contacto", Valido)).StatusCode);
        Assert.Equal(2, enviador.Enviados.Count);
    }
}
