using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

public class RepartoTests
{
    [Fact]
    public void ProporcionalAIngresos_A2X_B1X_900Euros()
    {
        var r = Reparto.Dividir(900m, [2m, 1m]);
        Assert.Equal([600m, 300m], r);
    }

    [Fact]
    public void Porcentaje_60_40()
    {
        var r = Reparto.Dividir(100m, [60m, 40m]);
        Assert.Equal([60m, 40m], r);
    }

    [Fact]
    public void UltimoMiembroAbsorbeElCentimo()
    {
        var r = Reparto.Dividir(100m, [1m, 1m, 1m]);
        Assert.Equal([33.33m, 33.33m, 33.34m], r);
        Assert.Equal(100m, r.Sum());
    }

    [Fact]
    public void Individual_100PorCiento()
    {
        var r = Reparto.Dividir(45m, [0m, 1m]);
        Assert.Equal([0m, 45m], r);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ImporteNoPositivo_Falla(int importe)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Reparto.Dividir(importe, [1m]));
}
