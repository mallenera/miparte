using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>Reglas de las frecuencias de aportación: qué meses cubre cada cobro y cuándo cobra.</summary>
public class FrecuenciaAportacionTests
{
    [Theory]
    [InlineData(FrecuenciaAportacion.Mensual, 1)]
    [InlineData(FrecuenciaAportacion.Bimestral, 2)]
    [InlineData(FrecuenciaAportacion.Trimestral, 3)]
    [InlineData(FrecuenciaAportacion.Semestral, 6)]
    [InlineData(FrecuenciaAportacion.Anual, 12)]
    public void MesesPorPeriodo_SegunLaFrecuencia(FrecuenciaAportacion frecuencia, int meses)
        => Assert.Equal(meses, ReglasFrecuenciaAportacion.MesesPorPeriodo(frecuencia));

    [Fact]
    public void Unica_NoTienePeriodo()
    {
        Assert.False(ReglasFrecuenciaAportacion.TienePeriodo(FrecuenciaAportacion.Unica));
        Assert.Null(ReglasFrecuenciaAportacion.PeriodoCubierto(FrecuenciaAportacion.Unica, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Bimestral_DesdeEnero_CubreEneroYFebreroComoUnPeriodo()
    {
        var desde = new DateOnly(2026, 1, 1);

        var enero = ReglasFrecuenciaAportacion.PeriodoCubierto(FrecuenciaAportacion.Bimestral, desde, new DateOnly(2026, 1, 15));
        var febrero = ReglasFrecuenciaAportacion.PeriodoCubierto(FrecuenciaAportacion.Bimestral, desde, new DateOnly(2026, 2, 3));
        var marzo = ReglasFrecuenciaAportacion.PeriodoCubierto(FrecuenciaAportacion.Bimestral, desde, new DateOnly(2026, 3, 1));

        Assert.Equal("2026-01-01/2026-02-28", enero!.ATexto());
        Assert.Equal("2026-01-01/2026-02-28", febrero!.ATexto());
        Assert.Equal("2026-03-01/2026-04-30", marzo!.ATexto());
    }

    [Fact]
    public void Mensual_CadaMesEsSuPropioPeriodo()
    {
        var desde = new DateOnly(2026, 1, 1);
        Assert.Equal("2026-02-01/2026-02-28", ReglasFrecuenciaAportacion.PeriodoCubierto(FrecuenciaAportacion.Mensual, desde, new DateOnly(2026, 2, 1))!.ATexto());
    }

    [Fact]
    public void PeriodoCubierto_AntesDelInicio_EsNulo()
        => Assert.Null(ReglasFrecuenciaAportacion.PeriodoCubierto(FrecuenciaAportacion.Trimestral, new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)));

    [Fact]
    public void Trimestral_CobraCadaTresMesesDesdeSuInicio()
    {
        var desde = new DateOnly(2026, 1, 1);
        var cobros = Enumerable.Range(0, 12).Select(i => desde.AddMonths(i))
            .Where(m => ReglasFrecuenciaAportacion.EsMesDeCobro(FrecuenciaAportacion.Trimestral, desde, m))
            .Select(m => m.Month).ToList();

        Assert.Equal(new[] { 1, 4, 7, 10 }, cobros);
    }

    [Fact]
    public void Unica_CobraSoloEnSuMesDeInicio()
    {
        var desde = new DateOnly(2026, 1, 1);
        Assert.True(ReglasFrecuenciaAportacion.EsMesDeCobro(FrecuenciaAportacion.Unica, desde, new DateOnly(2026, 1, 1)));
        Assert.False(ReglasFrecuenciaAportacion.EsMesDeCobro(FrecuenciaAportacion.Unica, desde, new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void Mensual_CobraTodosLosMesesDesdeElInicio()
    {
        var desde = new DateOnly(2026, 1, 1);
        Assert.True(Enumerable.Range(0, 12).All(i => ReglasFrecuenciaAportacion.EsMesDeCobro(FrecuenciaAportacion.Mensual, desde, desde.AddMonths(i))));
        Assert.False(ReglasFrecuenciaAportacion.EsMesDeCobro(FrecuenciaAportacion.Mensual, desde, new DateOnly(2025, 12, 1)));
    }

    [Theory]
    [InlineData(null, "mensual")]
    [InlineData("", "mensual")]
    [InlineData("Bimestral", "bimestral")]
    [InlineData("  anual ", "anual")]
    public void TextoFrecuencia_AceptaFormatosYDefectoMensual(string? texto, string esperado)
    {
        Assert.True(TextoFrecuenciaAportacion.TryParse(texto, out var f));
        Assert.Equal(esperado, TextoFrecuenciaAportacion.ATexto(f));
    }

    [Theory]
    [InlineData("quincenal")]
    [InlineData("diaria")]
    public void TextoFrecuencia_RechazaValoresDesconocidos(string texto)
        => Assert.False(TextoFrecuenciaAportacion.TryParse(texto, out _));
}
