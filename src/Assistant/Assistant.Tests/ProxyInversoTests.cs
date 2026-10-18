using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace MiParte.Assistant.Tests;

/// <summary>Configuración de <c>ForwardedHeaders</c> (<c>ProxyInverso__*</c>) en Assistant.Api.</summary>
public class ProxyInversoTests
{
    [Fact]
    public void IpDeProxyInvalida_FallaAlArrancar()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ProxyInverso:Proxies:0", "no-es-una-ip"));

        Assert.ThrowsAny<Exception>(() => f.CreateClient());
    }

    [Fact]
    public async Task ConfiguracionValida_ArrancaYSigueExigiendoToken()
    {
        await using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ProxyInverso:Proxies:0", "198.51.100.7"));

        var r = await f.CreateClient().PostAsync("/api/chat", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
