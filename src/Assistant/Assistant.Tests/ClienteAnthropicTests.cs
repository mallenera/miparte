using Microsoft.Extensions.Configuration;
using MiParte.Assistant.Api.Modelo;

namespace MiParte.Assistant.Tests;

public class ClienteAnthropicTests
{
    [Fact]
    public async Task SinClave_FallaSinLlamarALaRedYSinFiltrarNada()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var cliente = new ClienteAnthropic(Ayuda.Opciones(), config);

        var ex = await Assert.ThrowsAsync<ModeloNoDisponibleException>(() => cliente.CrearAsync(
            new SolicitudModelo("s", [MensajeModelo.DeTexto(RolModelo.Usuario, "hola")], [], 100), default));

        Assert.Contains("ANTHROPIC_API_KEY", ex.Message);
    }

    [Fact]
    public void ModeloPorDefecto_EsSonnet55YEsConfigurable()
    {
        Assert.Equal("claude-sonnet-5-5", new MiParte.Assistant.Api.OpcionesAsistente().Modelo);
    }
}
