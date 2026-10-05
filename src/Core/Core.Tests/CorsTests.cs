using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class CorsTests
{
    private const string Permitido = "https://app.miparte.test";

    private static WebApplicationFactory<Program> ConOrigenes(params string[] origenes)
    {
        var f = Crear(Secreto);
        return origenes.Length == 0
            ? f
            : f.WithWebHostBuilder(b =>
            {
                for (var i = 0; i < origenes.Length; i++)
                    b.UseSetting($"Cors:OrigenesPermitidos:{i}", origenes[i]);
            });
    }

    private static Task<HttpResponseMessage> Preflight(HttpClient c, string origen)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/hogares");
        req.Headers.Add("Origin", origen);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-hogar-id");
        return c.SendAsync(req);
    }

    [Fact]
    public async Task OrigenConfigurado_PreflightPermitido_ConCabecerasEsperadas()
    {
        using var f = ConOrigenes(Permitido);
        var r = await Preflight(f.CreateClient(), Permitido);

        Assert.True(r.IsSuccessStatusCode);
        Assert.Equal(Permitido, r.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var cabeceras = string.Join(",", r.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("authorization", cabeceras);
        Assert.Contains("content-type", cabeceras);
        Assert.Contains("x-hogar-id", cabeceras);
    }

    [Fact]
    public async Task OrigenNoConfigurado_SinCabeceraCors()
    {
        using var f = ConOrigenes(Permitido);
        var r = await Preflight(f.CreateClient(), "https://malo.test");
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SinConfiguracion_NingunOrigenCruzadoPermitido()
    {
        using var f = ConOrigenes();
        var r = await Preflight(f.CreateClient(), Permitido);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));

        var get = new HttpRequestMessage(HttpMethod.Get, "/health");
        get.Headers.Add("Origin", Permitido);
        Assert.False((await f.CreateClient().SendAsync(get)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RespuestaNormal_IncluyeAllowOriginParaOrigenPermitido()
    {
        using var f = ConOrigenes(Permitido);
        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("Origin", Permitido);
        var r = await f.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(Permitido, r.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
