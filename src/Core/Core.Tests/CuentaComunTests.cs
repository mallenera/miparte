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
    public void Reembolso_ComplétoQuitaAlMiembroDePendientes()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 50) };

        var e = CuentaComun.Calcular(D("2026-01-01"), [], gastos, [new ReembolsoDeCuenta(Ana, D("2026-01-06"), 50)]);

        Assert.Empty(e.Pendientes);
        Assert.Equal(-50m, e.Saldo); // la cuenta no cubre el gasto: saldo negativo
    }
}
