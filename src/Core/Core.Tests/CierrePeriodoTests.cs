using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Cierre de un periodo propio: un periodo abierto termina el día del cierre y uno de rango conserva su fin.</summary>
public class CierrePeriodoTests
{
    private static readonly DateOnly Inicio = new(2026, 3, 10);

    [Fact]
    public void Abierto_SeCierraEnElDiaDelCierre()
    {
        var periodo = CierrePeriodo.Cerrar(Periodo.Abierto(Inicio), new DateOnly(2026, 3, 15), out var error);

        Assert.Null(error);
        Assert.Equal(TipoPeriodo.Rango, periodo!.Tipo);
        Assert.Equal("2026-03-10/2026-03-15", periodo.ATexto());
    }

    [Fact]
    public void Rango_ConservaSuFechaDeFin()
    {
        var original = Periodo.Rango(Inicio, new DateOnly(2026, 3, 20));
        var periodo = CierrePeriodo.Cerrar(original, new DateOnly(2026, 3, 15), out var error);

        Assert.Null(error);
        Assert.Equal("2026-03-10/2026-03-20", periodo!.ATexto());
    }

    [Fact]
    public void Abierto_NoSeCierraAntesDeEmpezar()
    {
        var periodo = CierrePeriodo.Cerrar(Periodo.Abierto(Inicio), new DateOnly(2026, 3, 9), out var error);

        Assert.Null(periodo);
        Assert.Equal("El periodo todavía no ha empezado: no se puede cerrar antes de su primer día.", error);
    }

    [Fact]
    public void SinPeriodoPropio_NoSeCierra()
    {
        Assert.Null(CierrePeriodo.Cerrar(null, Inicio, out var error));
        Assert.Equal("El hogar no tiene un periodo propio que cerrar.", error);
    }

    [Fact]
    public void PeriodoDeMes_NoSeCierra()
        => Assert.Null(CierrePeriodo.Cerrar(Periodo.DeMes("2026-03"), Inicio, out _));

    [Fact]
    public void PeriodoPropioDelHogar_SeDerivaDeSusColumnas()
    {
        var hogar = new Hogar { PeriodoPropio = Periodo.Abierto(Inicio) };

        Assert.Equal(Inicio, hogar.PeriodoDesde);
        Assert.Null(hogar.PeriodoHasta);
        Assert.Equal(TipoPeriodo.Abierto, hogar.PeriodoPropio!.Tipo);
    }
}
