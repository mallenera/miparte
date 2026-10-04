using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

public class RepartoNMiembrosTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");

    private static decimal[] Importes(IReadOnlyList<ParteAsumida> r) => r.Select(p => p.Importe).ToArray();

    [Fact]
    public void Porcentaje_DosMiembros()
    {
        var r = RepartoMiembros.Repartir(200m, ModoReparto.Porcentaje, [new(A, 70m), new(B, 30m)], A);
        Assert.Equal([140m, 60m], Importes(r));
    }

    [Fact]
    public void Partes_TresMiembros_CentimoSobrante()
    {
        var r = RepartoMiembros.Repartir(100m, ModoReparto.Partes, [new(A, 1m), new(B, 1m), new(C, 1m)], A);
        Assert.Equal([33.33m, 33.33m, 33.34m], Importes(r));
        Assert.Equal(100m, r.Sum(p => p.Importe));
    }

    [Fact]
    public void Ingresos_CuatroMiembros_ProporcionalesYSumaExacta()
    {
        var r = RepartoMiembros.Repartir(1000m, ModoReparto.Ingresos,
            [new(A, 1500m), new(B, 1000m), new(C, 500m), new(D, 333m)], A);
        Assert.Equal(1000m, r.Sum(p => p.Importe));
        Assert.Equal(4, r.Count);
        Assert.True(r[0].Importe > r[1].Importe && r[1].Importe > r[2].Importe && r[2].Importe > r[3].Importe);
    }

    [Fact]
    public void Ingresos_TodosCero_PartesIguales()
    {
        var r = RepartoMiembros.Repartir(90m, ModoReparto.Ingresos, [new(A, 0m), new(B, 0m), new(C, 0m)], A);
        Assert.Equal([30m, 30m, 30m], Importes(r));
    }

    [Fact]
    public void Ingresos_UnoACero_NoAsumeNada()
    {
        var r = RepartoMiembros.Repartir(100m, ModoReparto.Ingresos, [new(A, 2000m), new(B, 0m)], A);
        Assert.Equal([100m, 0m], Importes(r));
    }

    [Fact]
    public void Individual_100PorCientoAQuienPaga()
    {
        var r = RepartoMiembros.Repartir(45m, ModoReparto.Individual, [new(A, 1m), new(B, 1m), new(C, 1m)], B);
        Assert.Equal([0m, 45m, 0m], Importes(r));
    }

    [Fact]
    public void Individual_PagadorAjeno_Falla()
        => Assert.Throws<ArgumentException>(() =>
            RepartoMiembros.Repartir(10m, ModoReparto.Individual, [new(A, 1m)], B));

    [Fact]
    public void Monopersonal_100PorCiento()
    {
        foreach (var modo in Enum.GetValues<ModoReparto>())
        {
            var r = RepartoMiembros.Repartir(57.31m, modo, [new(A, 0m)], A);
            Assert.Equal([57.31m], Importes(r));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ImporteInvalido_Falla(int importe)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            RepartoMiembros.Repartir(importe, ModoReparto.Partes, [new(A, 1m)], A));

    [Fact]
    public void SinMiembros_Falla()
        => Assert.Throws<ArgumentException>(() =>
            RepartoMiembros.Repartir(10m, ModoReparto.Partes, [], A));
}
