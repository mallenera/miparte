using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class LimitacionPeticionesTests
{
    private static WebApplicationFactory<Program> Fabrica(int global = 1000, int costosas = 3)
        => Crear(Secreto).WithWebHostBuilder(b =>
        {
            b.UseSetting("Limites:PeticionesPorMinuto", global.ToString());
            b.UseSetting("Limites:CostosasPorMinuto", costosas.ToString());
        });

    private static HttpClient ClienteDe(WebApplicationFactory<Program> f, Guid usuario)
        => Cliente(f, Token(Hs256(), usuario));

    private static Task<HttpResponseMessage> AceptarInvitacionInventada(HttpClient c)
        => c.PostAsJsonAsync("/api/invitaciones/aceptar", new { token = "no-existe" });

    [Fact]
    public async Task EndpointCostoso_TrasElLimite_Responde429ConRetryAfterYMensaje()
    {
        using var f = Fabrica(costosas: 3);
        var c = ClienteDe(f, Guid.NewGuid());

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await AceptarInvitacionInventada(c)).StatusCode);

        var rechazada = await AceptarInvitacionInventada(c);
        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        // Sin metadato del limitador se indica la ventana entera: avisar de menos provocaría un segundo 429.
        Assert.Equal("60", rechazada.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Demasiadas peticiones", await rechazada.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElLimiteEsPorUsuario_OtroUsuarioNoSeVeAfectado()
    {
        using var f = Fabrica(costosas: 2);
        var abusador = ClienteDe(f, Guid.NewGuid());
        var otro = ClienteDe(f, Guid.NewGuid());

        for (var i = 0; i < 2; i++) await AceptarInvitacionInventada(abusador);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await AceptarInvitacionInventada(abusador)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await AceptarInvitacionInventada(otro)).StatusCode);
    }

    [Fact]
    public async Task LaPoliticaCostosa_NoLimitaLosEndpointsNormalesAntesDeSuTope()
    {
        using var f = Fabrica(global: 1000, costosas: 1);
        var c = ClienteDe(f, Guid.NewGuid());

        await AceptarInvitacionInventada(c);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await AceptarInvitacionInventada(c)).StatusCode);

        // El tope estricto solo aplica a los endpoints marcados como costosos.
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task LimiteGlobal_ProtegeToda_LaApiMenosHealth()
    {
        using var f = Fabrica(global: 3);
        var c = ClienteDe(f, Guid.NewGuid());

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/yo")).StatusCode);

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task SinSesion_ElLimiteSeAplicaPorIp_YUnTokenFalsoNoEvitaElLimite()
    {
        using var f = Fabrica(global: 2);
        var c = f.CreateClient();
        // Un sub falsificado (firma inválida) no autentica: sigue contando como anónimo por IP.
        c.DefaultRequestHeaders.Authorization = new("Bearer", Token(Hs256("otro-secreto-distinto-de-al-menos-32-bytes!!"), Guid.NewGuid()));

        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/yo")).StatusCode);

        // Cambiar el sub en cada petición no da una cuota nueva.
        c.DefaultRequestHeaders.Authorization = new("Bearer", Token(Hs256("otro-secreto-distinto-de-al-menos-32-bytes!!"), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/yo")).StatusCode);
    }

    private static async Task<HttpStatusCode> AnonimaDesde(HttpClient c, string ip)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/yo");
        peticion.Headers.Add("X-Forwarded-For", ip);
        return (await c.SendAsync(peticion)).StatusCode;
    }

    [Fact]
    public async Task TrasProxyDeConfianza_LasAnonimasSeLimitanPorIpReenviada()
    {
        using var f = Fabrica(global: 1).WithWebHostBuilder(b => b.UseSetting("ProxyInverso:Confiar", "true"));
        var c = f.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await AnonimaDesde(c, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await AnonimaDesde(c, "203.0.113.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await AnonimaDesde(c, "203.0.113.2"));
    }

    [Fact]
    public async Task SinProxyDeConfianza_SeIgnoraXForwardedFor()
    {
        using var f = Fabrica(global: 1);
        var c = f.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await AnonimaDesde(c, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await AnonimaDesde(c, "203.0.113.2"));
    }
}
