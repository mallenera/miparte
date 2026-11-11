using MiParte.Contracts;

namespace MiParte.Core.Tests;

public class FormatosApiTests
{
    [Theory]
    [InlineData("2026-10", 2026, 10)]
    [InlineData("1900-01", 1900, 1)]
    [InlineData("2026-12", 2026, 12)]
    [InlineData("2999-12", 2999, 12)]
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
    [InlineData("0001-01")]
    [InlineData("1899-12")]
    [InlineData("3000-01")]
    [InlineData("9999-12")] // antes se aceptaba y el mes siguiente desbordaba DateOnly (500)
    public void TryMes_RechazaFormatosInvalidos(string? texto) => Assert.False(FormatosApi.TryMes(texto, out _));

    [Fact]
    public void RangoDeAnios_EsElMismoEnLaApiYEnElDominio()
        => Assert.Equal((FormatosApi.AnioMinimo, FormatosApi.AnioMaximo), (MiParte.Core.Domain.Periodo.AnioMinimo, MiParte.Core.Domain.Periodo.AnioMaximo));

    [Fact]
    public void FormatoMes_RellenaConCeros() => Assert.Equal("0042-03", FormatosApi.FormatoMes(new DateOnly(42, 3, 17)));

    [Theory]
    [InlineData("2026-10-15", true)]
    [InlineData("2026-10", false)]
    [InlineData("15/10/2026", false)]
    [InlineData("2026-02-30", false)]
    [InlineData("1900-01-01", true)]
    [InlineData("2999-12-31", true)]
    [InlineData("1899-12-31", false)]
    [InlineData("9999-12-31", false)]
    public void TryFecha_ExigeAnioMesDia(string texto, bool valida) => Assert.Equal(valida, FormatosApi.TryFecha(texto, out _));
}
