using MiParte.Contracts;
using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

public class PeriodoTests
{
    [Theory]
    [InlineData("2026-02", 2026, 2, 1, 2026, 2, 28)]
    [InlineData("2026-12", 2026, 12, 1, 2026, 12, 31)]
    [InlineData("2024-02", 2024, 2, 1, 2024, 2, 29)]
    [InlineData("2025-02", 2025, 2, 1, 2025, 2, 28)]
    public void DeMes_DaElPrimerYUltimoDiaDelMes(string mes, int aD, int mD, int dD, int aH, int mH, int dH)
    {
        var periodo = Periodo.DeMes(mes);

        Assert.Equal(TipoPeriodo.Mes, periodo.Tipo);
        Assert.Equal(new DateOnly(aD, mD, dD), periodo.Desde);
        Assert.Equal(new DateOnly(aH, mH, dH), periodo.Hasta);
        Assert.Equal(mes, periodo.ATexto());
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-2")]
    [InlineData("26-02")]
    [InlineData("2026/02")]
    [InlineData("2026-02-01")]
    [InlineData("")]
    [InlineData(null)]
    public void DeMes_RechazaFormatosQueNoSonMes(string? mes)
    {
        var ex = Assert.Throws<ArgumentException>(() => Periodo.DeMes(mes!));
        Assert.Contains("YYYY-MM", ex.Message);
    }

    [Theory]
    [InlineData("2026-01")]
    [InlineData("2026-02")]
    [InlineData("2026-12")]
    [InlineData("2000-07")]
    [InlineData("0001-01")]
    [InlineData("9999-12")]
    public void Mes_EsCompatibleConLosMesesDeLaApi(string mes)
    {
        // Lo que la API ya acepta como mes lo acepta el dominio, y al revés; el texto no cambia.
        Assert.True(FormatosApi.TryMes(mes, out var inicio));
        Assert.Equal(FormatosApi.FormatoMes(inicio), Periodo.DeMes(mes).ATexto());
        Assert.True(Periodo.TryParse(mes, out var parseado, out var error));
        Assert.Null(error);
        Assert.Equal(mes, parseado!.ATexto());
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("0000-05")]
    [InlineData("2026-2")]
    [InlineData("2026-02 ")]
    [InlineData("abcd-ef")]
    public void MesInvalido_TambienLoRechazaLaApi(string mes)
    {
        Assert.False(FormatosApi.TryMes(mes, out _));
        Assert.False(Periodo.TryParse(mes, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rango_IncluyeLosDosLimites()
    {
        var periodo = Periodo.Rango(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 20));

        Assert.False(periodo.Contiene(new DateOnly(2026, 7, 9)));
        Assert.True(periodo.Contiene(new DateOnly(2026, 7, 10)));
        Assert.True(periodo.Contiene(new DateOnly(2026, 7, 15)));
        Assert.True(periodo.Contiene(new DateOnly(2026, 7, 20)));
        Assert.False(periodo.Contiene(new DateOnly(2026, 7, 21)));
    }

    [Fact]
    public void Rango_DeUnSoloDia_ContieneESeDia()
    {
        var dia = new DateOnly(2026, 8, 1);
        var periodo = Periodo.Rango(dia, dia);

        Assert.True(periodo.Contiene(dia));
        Assert.False(periodo.Contiene(dia.AddDays(1)));
        Assert.False(periodo.Contiene(dia.AddDays(-1)));
    }

    [Fact]
    public void Rango_ConDesdeMayorQueHasta_SeRechaza()
    {
        var ex = Assert.Throws<ArgumentException>(() => Periodo.Rango(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 10)));
        Assert.Contains("anterior", ex.Message);

        Assert.False(Periodo.TryParse("2026-07-20/2026-07-10", out _, out var error));
        Assert.Contains("anterior", error);
    }

    [Fact]
    public void Abierto_NoTieneFinYSeCuentaDesdeSuInicio()
    {
        var periodo = Periodo.Abierto(new DateOnly(2026, 9, 1));

        Assert.Equal(TipoPeriodo.Abierto, periodo.Tipo);
        Assert.Null(periodo.Hasta);
        Assert.False(periodo.Contiene(new DateOnly(2026, 8, 31)));
        Assert.True(periodo.Contiene(new DateOnly(2026, 9, 1)));
        Assert.True(periodo.Contiene(new DateOnly(2031, 1, 1)));
    }

    [Theory]
    [InlineData("2026-07-10/2026-07-20", TipoPeriodo.Rango)]
    [InlineData("2026-09-01/", TipoPeriodo.Abierto)]
    [InlineData("2026-02", TipoPeriodo.Mes)]
    public void Parse_IdentificaLaFormaDelTexto(string texto, TipoPeriodo tipo)
    {
        var periodo = Periodo.Parse(texto);

        Assert.Equal(tipo, periodo.Tipo);
        Assert.Equal(texto, periodo.ATexto());
        Assert.Equal(texto, periodo.ToString());
    }

    [Fact]
    public void ATexto_DeRangoYAbierto_UsaElFormatoExplicito()
    {
        Assert.Equal("2026-07-05/2026-07-09", Periodo.Rango(new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 9)).ATexto());
        Assert.Equal("2026-07-05/", Periodo.Abierto(new DateOnly(2026, 7, 5)).ATexto());
    }

    [Theory]
    [InlineData("2026-07-10/2026-07-20", "2026-07-10", "2026-07-20")]
    [InlineData("2024-02-29/2024-03-01", "2024-02-29", "2024-03-01")]
    public void Parse_RangoDevuelveLosDosExtremos(string texto, string desde, string hasta)
    {
        var periodo = Periodo.Parse(texto);

        Assert.Equal(DateOnly.ParseExact(desde, "yyyy-MM-dd"), periodo.Desde);
        Assert.Equal(DateOnly.ParseExact(hasta, "yyyy-MM-dd"), periodo.Hasta);
    }

    [Theory]
    [InlineData("2026-07-10/2026-07-20/2026-07-30")]
    [InlineData("2026-07-10-2026-07-20")]
    [InlineData("2026-7-10/2026-07-20")]
    [InlineData("2026-02-30/2026-03-01")]
    [InlineData("2026-07-10/2026-02-31")]
    [InlineData("/2026-07-20")]
    [InlineData("2026-07-10/hoy")]
    [InlineData("2026-07-10/2026-07-20 ")]
    [InlineData("/")]
    [InlineData("texto")]
    public void TryParse_RechazaTextosDePeriodoMalformados(string texto)
    {
        Assert.False(Periodo.TryParse(texto, out var periodo, out var error));
        Assert.Null(periodo);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TextoVacioONulo_DaMensajeEnEspanol()
    {
        Assert.False(Periodo.TryParse(null, out _, out var nulo));
        Assert.Contains("formato", nulo);

        Assert.False(Periodo.TryParse("", out _, out var vacio));
        Assert.Contains("formato", vacio);
    }

    [Fact]
    public void Parse_LanzaArgumentExceptionConElMensajeDeError()
    {
        var ex = Assert.Throws<ArgumentException>(() => Periodo.Parse("2026-07-20/2026-07-10"));
        Assert.Contains("anterior", ex.Message);
    }

    [Fact]
    public void Parse_Y_ATexto_SonIdaYVueltaParaRangoYAbierto()
    {
        const string rango = "2026-12-28/2027-01-03";
        Assert.Equal(rango, Periodo.Parse(rango).ATexto());

        const string abierto = "2026-12-28/";
        Assert.Equal(abierto, Periodo.Parse(abierto).ATexto());
    }

    [Fact]
    public void Igualdad_CompareElContenidoDelPeriodo()
    {
        Assert.Equal(Periodo.DeMes("2026-02"), Periodo.Parse("2026-02"));
        Assert.Equal(Periodo.Rango(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2)), Periodo.Parse("2026-07-01/2026-07-02"));
        Assert.NotEqual(Periodo.Abierto(new DateOnly(2026, 7, 1)), Periodo.Rango(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2)));
    }
}
