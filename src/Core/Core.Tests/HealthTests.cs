using Microsoft.AspNetCore.Mvc.Testing;

namespace MiParte.Core.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_Devuelve200()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);
    }
}
