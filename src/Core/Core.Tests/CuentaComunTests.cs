using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

public class CuentaComunTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Beto = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void Aportacion_ValeDesdeSuMesYSeSustituyeConLaSiguiente()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 500), new AportacionVigente(Ana, D("2026-04-01"), 700) };

        Assert.Equal(0m, CuentaComun.AportacionDelMes(Ana, D("2025-12-01"), ap));
        Assert.Equal(500m, CuentaComun.AportacionDelMes(Ana, D("2026-03-01"), ap));
        Assert.Equal(700m, CuentaComun.AportacionDelMes(Ana, D("2026-04-01"), ap));
        Assert.Equal(0m, CuentaComun.AportacionDelMes(Beto, D("2026-04-01"), ap));
    }

    [Fact]
    public void Saldo_AcumulaAportacionesMenosGastosMesAMes()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 600), new AportacionVigente(Beto, D("2026-01-01"), 400) };
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 900), new GastoDeCuenta(Ana, D("2026-03-05"), 900) };

        var e = CuentaComun.Calcular(D("2026-03-01"), ap, gastos, []);

        Assert.Equal(1000m, e.AportadoMes);
        Assert.Equal(3000m, e.Aportado);
        Assert.Equal(1800m, e.Gastado);
        Assert.Equal(1200m, e.Saldo);
    }

    [Fact]
    public void Saldo_NoCuentaGastosPosterioresAlMes()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 1000) };
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-02-01"), 300) };

        var e = CuentaComun.Calcular(D("2026-01-01"), ap, gastos, []);

        Assert.Equal(0m, e.Gastado);
        Assert.Equal(1000m, e.Saldo);
    }

    [Fact]
    public void Reembolso_BajaLoPendienteYElEfectivoPeroNoElSaldo()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 1000) };
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 900) };

        var sin = CuentaComun.Calcular(D("2026-01-01"), ap, gastos, []);
        var con = CuentaComun.Calcular(D("2026-01-01"), ap, gastos, [new ReembolsoDeCuenta(Ana, D("2026-01-20"), 400)]);

        Assert.Equal(100m, sin.Saldo);
        Assert.Equal(900m, sin.Pendientes.Single().Importe);
        Assert.Equal(1000m, sin.Efectivo); // el dinero sigue en la cuenta hasta reembolsar

        Assert.Equal(100m, con.Saldo);
        Assert.Equal(500m, con.Pendientes.Single().Importe);
        Assert.Equal(600m, con.Efectivo);
    }

    [Fact]
    public void Ahorro_NoEntraEnElSaldoNiEnElEfectivoDeGastos()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 600, 200), new AportacionVigente(Beto, D("2026-01-01"), 400, 100) };
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 500) };

        var e = CuentaComun.Calcular(D("2026-02-01"), ap, gastos, []);

        Assert.Equal(2000m, e.Aportado);
        Assert.Equal((300m, 600m), (e.AhorroMes, e.AhorroAcumulado));
        Assert.Equal(900m, e.Saldo); // 2000 aportado - 600 ahorrado - 500 gastado
        Assert.Equal(1400m, e.Efectivo); // el gasto lo adelantó Ana: sigue pendiente de reembolso
        Assert.Equal(600m, e.AhorroDisponible);
    }

    [Fact]
    public void Ahorro_CeroSeComportaComoAntes()
    {
        var sin = CuentaComun.Calcular(D("2026-03-01"), [new AportacionVigente(Ana, D("2026-01-01"), 500)], [], []);

        Assert.Equal((1500m, 1500m), (sin.Aportado, sin.Saldo));
        Assert.Equal(0m, sin.AhorroAcumulado);
    }

    [Fact]
    public void Ahorro_CambiaDesdeElMesDeLaNuevaAportacion()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 500, 100), new AportacionVigente(Ana, D("2026-03-01"), 500, 300) };

        Assert.Equal(100m, CuentaComun.AhorroDelMes(Ana, D("2026-02-01"), ap));
        Assert.Equal(300m, CuentaComun.AhorroDelMes(Ana, D("2026-03-01"), ap));
        Assert.Equal(0m, CuentaComun.AhorroDelMes(Beto, D("2026-03-01"), ap));
        Assert.Equal(500m, CuentaComun.Calcular(D("2026-03-01"), ap, [], []).AhorroAcumulado); // 100 + 100 + 300
    }

    [Fact]
    public void Retirada_BajaElAhorroDisponibleYNoElSaldo()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 500, 200) };
        var retiradas = new[] { new RetiradaDeAhorro(D("2026-02-10"), 250), new RetiradaDeAhorro(D("2026-04-01"), 100) };

        var e = CuentaComun.Calcular(D("2026-02-01"), ap, [], [], retiradas);

        Assert.Equal((400m, 250m, 150m), (e.AhorroAcumulado, e.AhorroRetirado, e.AhorroDisponible)); // la de abril aún no cuenta
        Assert.Equal(600m, e.Saldo);
    }

    [Fact]
    public void Reembolso_ComplétoQuitaAlMiembroDePendientes()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 50) };

        var e = CuentaComun.Calcular(D("2026-01-01"), [], gastos, [new ReembolsoDeCuenta(Ana, D("2026-01-06"), 50)]);

        Assert.Empty(e.Pendientes);
        Assert.Equal(-50m, e.Saldo); // la cuenta no cubre el gasto: saldo negativo
    }
}
