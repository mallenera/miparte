using System.Net;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class CabecerasSeguridadTests
{
    private static void AfirmaCabeceras(HttpResponseMessage r)
    {
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.False(r.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Health_LlevaLasCabecerasDeSeguridad()
    {
        using var f = Crear(Secreto);
        AfirmaCabeceras(await f.CreateClient().GetAsync("/health"));
    }

    [Fact]
    public async Task Rechazo401_LlevaLasCabecerasDeSeguridad()
    {
        using var f = Crear(Secreto);
        var r = await f.CreateClient().GetAsync("/api/yo");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        AfirmaCabeceras(r);
    }

    [Fact]
    public async Task Rechazo429_LlevaLasCabecerasDeSeguridad()
    {
        using var f = Crear(Secreto).WithWebHostBuilder(b => b.UseSetting("Limites:PeticionesPorMinuto", "1"));
        var c = f.CreateClient();
        await c.GetAsync("/api/yo");
        var r = await c.GetAsync("/api/yo");
        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        AfirmaCabeceras(r);
    }
}
