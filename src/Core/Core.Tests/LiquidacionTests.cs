using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

public class LiquidacionTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");

    private static GastoCalculado Gasto(Guid pagador, decimal importe, params MiembroReparto[] m)
        => new(pagador, importe, RepartoMiembros.Repartir(importe, ModoReparto.Partes, m, pagador));

    [Fact]
    public void Saldos_DosMiembros_PagaUnoReparteIgual()
    {
        var s = Liquidacion.CalcularSaldos([A, B], [Gasto(A, 100m, new(A, 1m), new(B, 1m))]);
        Assert.Equal([50m, -50m], s.Select(x => x.Importe));
        Assert.Equal([new Transferencia(B, A, 50m)], Liquidacion.Liquidar(s));
    }

    [Fact]
    public void Liquidacion_TresMiembros()
    {
        var gastos = new[]
        {
            Gasto(A, 90m, new(A, 1m), new(B, 1m), new(C, 1m)),
            Gasto(B, 30m, new(A, 1m), new(B, 1m), new(C, 1m)),
        };
        var s = Liquidacion.CalcularSaldos([A, B, C], gastos);
        Assert.Equal([50m, -10m, -40m], s.Select(x => x.Importe));
        Assert.Equal([new Transferencia(C, A, 40m), new Transferencia(B, A, 10m)], Liquidacion.Liquidar(s));
    }

    [Fact]
    public void Liquidacion_CuatroMiembros_NoMasDeNMenos1YTodoQuedaACero()
    {
        var gastos = new[]
        {
            Gasto(A, 200m, new(A, 1m), new(B, 1m), new(C, 1m), new(D, 1m)),
            Gasto(B, 100m, new(A, 1m), new(B, 1m), new(C, 1m), new(D, 1m)),
        };
        var t = Liquidacion.Liquidar(Liquidacion.CalcularSaldos([A, B, C, D], gastos));
        Assert.True(t.Count <= 3);
        var tras = Liquidacion.CalcularSaldos([A, B, C, D], gastos,
            t.Select(x => new PagoLiquidacion(x.De, x.A, x.Importe)));
        Assert.All(tras, x => Assert.Equal(0m, x.Importe));
    }

    [Fact]
    public void PagosParciales_ReducenLaDeuda()
    {
        var gastos = new[] { Gasto(A, 100m, new(A, 1m), new(B, 1m)) };
        var s = Liquidacion.CalcularSaldos([A, B], gastos, [new PagoLiquidacion(B, A, 20m)]);
        Assert.Equal([30m, -30m], s.Select(x => x.Importe));
        Assert.Equal([new Transferencia(B, A, 30m)], Liquidacion.Liquidar(s));
    }

    [Fact]
    public void PagoCompleto_SinTransferencias()
    {
        var gastos = new[] { Gasto(A, 100m, new(A, 1m), new(B, 1m)) };
        var s = Liquidacion.CalcularSaldos([A, B], gastos, [new PagoLiquidacion(B, A, 50m)]);
        Assert.Empty(Liquidacion.Liquidar(s));
    }

    [Fact]
    public void CentimoSobrante_LiquidacionCuadra()
    {
        var gastos = new[] { Gasto(A, 100m, new(A, 1m), new(B, 1m), new(C, 1m)) };
        var s = Liquidacion.CalcularSaldos([A, B, C], gastos);
        Assert.Equal(0m, s.Sum(x => x.Importe));
        Assert.Equal(66.67m, Liquidacion.Liquidar(s).Sum(x => x.Importe));
    }

    [Fact]
    public void Determinista_EmpatesPorId()
    {
        var s = new SaldoMiembro[] { new(D, -10m), new(C, -10m), new(B, 10m), new(A, 10m) };
        var t1 = Liquidacion.Liquidar(s);
        var t2 = Liquidacion.Liquidar(s.Reverse().ToList());
        Assert.Equal(t1, t2);
        Assert.Equal([new Transferencia(C, A, 10m), new Transferencia(D, B, 10m)], t1);
    }

    [Fact]
    public void Monopersonal_SinTransferencias()
    {
        var s = Liquidacion.CalcularSaldos([A], [Gasto(A, 80m, new MiembroReparto(A, 1m))]);
        Assert.Equal([0m], s.Select(x => x.Importe));
        Assert.Empty(Liquidacion.Liquidar(s));
    }

    [Fact]
    public void SaldosQueNoSumanCero_Falla()
        => Assert.Throws<ArgumentException>(() => Liquidacion.Liquidar([new SaldoMiembro(A, 5m), new SaldoMiembro(B, -4m)]));

    [Fact]
    public void PagoConImporteInvalido_Falla()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            Liquidacion.CalcularSaldos([A, B], [], [new PagoLiquidacion(A, B, 0m)]));
}
