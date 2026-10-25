using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>Extracto bancario de la cuenta común: qué líneas salen, con qué signo y fecha, y cómo se acumula el saldo.</summary>
public class MovimientosBancoTests
{
    private static readonly DateOnly Enero = new(2026, 1, 1);
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Luis = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static DatosMovimientosBanco Datos(
        IReadOnlyList<AportacionVigente>? aportaciones = null, SaldoInicialCuenta? saldoInicial = null,
        IReadOnlyList<GastoPagadoPorCuenta>? gastos = null, IReadOnlyList<MovimientoCuenta>? reembolsos = null,
        IReadOnlyList<MovimientoCuenta>? ingresos = null, IReadOnlyList<MovimientoCuenta>? depositos = null,
        IReadOnlyList<MovimientoCuenta>? retiradas = null)
        => new(aportaciones ?? [], saldoInicial, gastos ?? [], reembolsos ?? [], ingresos ?? [], depositos ?? [], retiradas ?? []);

    private static MovimientoCuenta Mov(DateOnly fecha, decimal importe, Guid? miembro = null)
        => new(Guid.NewGuid(), miembro ?? Ana, fecha, importe, null);

    [Fact]
    public void Aportaciones_SalenUnaPorPersonaYMesFechadasElDia1ConElImporteCompleto()
    {
        var d = Datos(aportaciones: [new(Ana, Enero, 500m, 100m), new(Luis, Enero, 300m)]);

        var x = MovimientosBanco.Calcular(Enero, new DateOnly(2026, 2, 28), d);

        Assert.Equal(4, x.Movimientos.Count);
        Assert.All(x.Movimientos, m => Assert.Equal(1, m.Fecha.Day));
        var ana = x.Movimientos.First(m => m.MiembroId == Ana);
        Assert.Equal(500m, ana.Importe);
        Assert.Equal(100m, ana.Ahorro);
        Assert.Equal(1600m, x.SaldoFinal);
    }

    [Fact]
    public void Aportaciones_Trimestrales_SoloCobranElPrimerMesDelPeriodo()
    {
        var d = Datos(aportaciones: [new(Ana, Enero, 300m, 0m, FrecuenciaAportacion.Trimestral)]);

        var x = MovimientosBanco.Calcular(Enero, new DateOnly(2026, 6, 30), d);

        Assert.Equal([Enero, new DateOnly(2026, 4, 1)], x.Movimientos.Select(m => m.Fecha));
    }

    [Fact]
    public void Aportacion_DeCero_NoGeneraLinea()
    {
        var d = Datos(aportaciones: [new(Ana, Enero, 200m), new(Ana, Enero.AddMonths(1), 0m)]);

        var x = MovimientosBanco.Calcular(Enero, new DateOnly(2026, 3, 31), d);

        Assert.Single(x.Movimientos);
    }

    [Fact]
    public void Salidas_LlevanSignoNegativo_YEntradasPositivo()
    {
        var d = Datos(
            saldoInicial: new(Enero.AddDays(1), 1000m),
            gastos: [new(Guid.NewGuid(), Enero.AddDays(2), 80m, Guid.NewGuid(), "Luz", false),
                     new(Guid.NewGuid(), Enero.AddDays(3), 50m, Guid.NewGuid(), "Viaje", true)],
            reembolsos: [Mov(Enero.AddDays(4), 30m)],
            ingresos: [Mov(Enero.AddDays(5), 20m)],
            depositos: [Mov(Enero.AddDays(6), 200m)],
            retiradas: [Mov(Enero.AddDays(7), 70m)]);

        var x = MovimientosBanco.Calcular(Enero, Enero.AddDays(30), d);

        Assert.Equal(
            [1000m, -80m, -50m, -30m, 20m, 200m, -70m],
            x.Movimientos.Select(m => m.Importe));
        Assert.Equal(TipoMovimientoBanco.GastoAhorro, x.Movimientos[2].Tipo);
        Assert.Equal(1220m, x.Entradas);
        Assert.Equal(230m, x.Salidas);
        Assert.Equal(990m, x.SaldoFinal);
        Assert.Equal(990m, x.Movimientos[^1].Saldo);
    }

    [Fact]
    public void SaldoAnterior_AcumulaLoPrevioADesde_YLasLineasSoloSonDelRango()
    {
        var d = Datos(aportaciones: [new(Ana, Enero, 100m)], retiradas: [Mov(new DateOnly(2026, 3, 10), 40m)]);

        var x = MovimientosBanco.Calcular(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), d);

        Assert.Equal(200m, x.SaldoAnterior);
        Assert.Equal([100m, -40m], x.Movimientos.Select(m => m.Importe));
        Assert.Equal([300m, 260m], x.Movimientos.Select(m => m.Saldo));
        Assert.Equal(260m, x.SaldoFinal);
    }

    [Fact]
    public void MovimientosPosterioresAHasta_SeIgnoran()
    {
        var d = Datos(ingresos: [Mov(Enero.AddDays(1), 10m), Mov(Enero.AddMonths(2), 99m)], aportaciones: [new(Ana, Enero, 5m)]);

        var x = MovimientosBanco.Calcular(Enero, Enero.AddDays(27), d);

        Assert.Equal(15m, x.SaldoFinal);
        Assert.Equal(2, x.Movimientos.Count);
    }

    [Fact]
    public void MismoDia_PrimeroEntradasLuegoSalidas()
    {
        var d = Datos(retiradas: [Mov(Enero, 10m)], ingresos: [Mov(Enero, 10m)]);

        var x = MovimientosBanco.Calcular(Enero, Enero, d);

        Assert.Equal([TipoMovimientoBanco.Ingreso, TipoMovimientoBanco.RetiradaAhorro], x.Movimientos.Select(m => m.Tipo));
        Assert.Equal([10m, 0m], x.Movimientos.Select(m => m.Saldo));
    }

    [Fact]
    public void AlFinalDeMes_ElEfectivoYElAhorroCoincidenConLosDeLaCuenta()
    {
        var aportaciones = new[] { new AportacionVigente(Ana, Enero, 500m, 100m), new AportacionVigente(Luis, Enero.AddMonths(1), 300m, 50m) };
        var saldoInicial = new SaldoInicialCuenta(Enero.AddDays(4), 1000m);
        var directo = new GastoPagadoPorCuenta(Guid.NewGuid(), Enero.AddDays(9), 120m, Guid.NewGuid(), null, false);
        var delAhorro = new GastoPagadoPorCuenta(Guid.NewGuid(), Enero.AddMonths(1).AddDays(2), 60m, Guid.NewGuid(), null, true);
        var adelantado = new GastoDeCuenta(Luis, Enero.AddMonths(1).AddDays(5), 90m);
        var adelantadoDelAhorro = new GastoPagadoPorCuenta(Guid.NewGuid(), Enero.AddDays(20), 30m, Guid.NewGuid(), null, true, Ana);
        var reembolso = Mov(Enero.AddMonths(1).AddDays(8), 50m, Luis);
        var ingreso = Mov(Enero.AddDays(12), 40m);
        var deposito = Mov(Enero.AddDays(15), 200m);
        var retirada = Mov(Enero.AddMonths(1).AddDays(10), 70m);
        var d = Datos(aportaciones, saldoInicial, [directo, delAhorro], [reembolso], [ingreso], [deposito], [retirada]) with
        {
            AdelantadosDelAhorro = [adelantadoDelAhorro],
        };
        var febrero = Enero.AddMonths(1);

        var x = MovimientosBanco.Calcular(Enero, febrero.AddMonths(1).AddDays(-1), d);
        var cuenta = CuentaComun.Calcular(
            febrero, aportaciones,
            [new(null, directo.Fecha, directo.Importe), new(null, delAhorro.Fecha, delAhorro.Importe, true), adelantado,
                new(Ana, adelantadoDelAhorro.Fecha, adelantadoDelAhorro.Importe, true)],
            [new(Luis, reembolso.Fecha, reembolso.Importe)], [new(retirada.Fecha, retirada.Importe)],
            [new(deposito.Fecha, deposito.Importe)], saldoInicial, [new(ingreso.Fecha, ingreso.Importe)]);

        Assert.Equal(cuenta.Efectivo, x.EfectivoFinal);
        Assert.Equal(cuenta.AhorroDisponible, x.AhorroFinal);
        Assert.Equal(x.SaldoFinal, x.EfectivoFinal + x.AhorroFinal);
    }

    [Fact]
    public void UnGastoDelAhorroAdelantado_SaleComoLineaSinImporte_YPasaDelAhorroAlEfectivo()
    {
        var aportaciones = new[] { new AportacionVigente(Ana, Enero, 500m, 100m) };
        var cat = Guid.NewGuid();
        var adelantado = new GastoPagadoPorCuenta(Guid.NewGuid(), Enero.AddDays(9), 60m, cat, "Fianza", true, Ana);
        var d = Datos(aportaciones) with { AdelantadosDelAhorro = [adelantado] };

        var x = MovimientosBanco.Calcular(Enero, Enero.AddDays(30), d);

        var linea = Assert.Single(x.Movimientos, m => m.Tipo == TipoMovimientoBanco.GastoAhorroAdelantado);
        Assert.Equal((adelantado.Id, 0m, -60m, Ana, cat, "Fianza"), (linea.Id!.Value, linea.Importe, linea.Ahorro, linea.MiembroId!.Value, linea.CategoriaId!.Value, linea.Concepto));
        Assert.Equal(500m, linea.Saldo); // el banco no cambia
        Assert.Equal((0m, 500m), (x.Salidas, x.SaldoFinal));
        Assert.Equal((460m, 40m), (x.EfectivoFinal, x.AhorroFinal)); // 400 + 60 que la cuenta debe a Ana; 100 - 60 de ahorro
        Assert.Equal("gasto_ahorro_adelantado", MovimientosBanco.ATexto(linea.Tipo));
    }

    [Fact]
    public void HastaAnteriorADesde_LanzaExcepcion()
        => Assert.Throws<ArgumentException>(() => MovimientosBanco.Calcular(Enero.AddDays(1), Enero, Datos()));
}
