using MiParte.Contracts;

namespace MiParte.Core.Tests;

public class FormatosApiTests
{
    [Theory]
    [InlineData("2026-10", 2026, 10)]
    [InlineData("0001-01", 1, 1)]
    [InlineData("2026-12", 2026, 12)]
    public void TryMes_AceptaMesesValidos(string texto, int anio, int mes)
    {
        Assert.True(FormatosApi.TryMes(texto, out var inicio));
        Assert.Equal(new DateOnly(anio, mes, 1), inicio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-1")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("0000-05")]
    [InlineData("2026/10")]
    [InlineData("2026-10-01")]
    [InlineData("+026-10")]
    public void TryMes_RechazaFormatosInvalidos(string? texto) => Assert.False(FormatosApi.TryMes(texto, out _));

    [Fact]
    public void FormatoMes_RellenaConCeros() => Assert.Equal("0042-03", FormatosApi.FormatoMes(new DateOnly(42, 3, 17)));

    [Theory]
    [InlineData("2026-10-15", true)]
    [InlineData("2026-10", false)]
    [InlineData("15/10/2026", false)]
    [InlineData("2026-02-30", false)]
    public void TryFecha_ExigeAnioMesDia(string texto, bool valida) => Assert.Equal(valida, FormatosApi.TryFecha(texto, out _));
}
