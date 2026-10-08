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
    public void Deposito_SumaAlAhorroSinTocarElSaldoNiLoAportado()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 500, 100) };
        var depositos = new[] { new DepositoDeAhorro(D("2026-01-10"), 1000), new DepositoDeAhorro(D("2026-03-05"), 50) };

        var e = CuentaComun.Calcular(D("2026-02-01"), ap, [], [], null, depositos);

        Assert.Equal((1000m, 1200m, 1200m, 1000m), (e.AhorroDepositado, e.AhorroAcumulado, e.AhorroDisponible, e.Aportado)); // el de marzo aún no cuenta
        Assert.Equal(100m, e.AhorroMes); // febrero: solo la parte de ahorro de la aportación, sin depósitos ese mes
        Assert.Equal(800m, e.Saldo); // 1000 aportado - 200 de ahorro
    }

    [Fact]
    public void Deposito_SinAportacionesBastaParaTenerAhorro()
    {
        var e = CuentaComun.Calcular(D("2026-01-01"), [], [], [], null, [new DepositoDeAhorro(D("2026-01-02"), 300)]);

        Assert.Equal((300m, 0m), (e.AhorroDisponible, e.Saldo));
    }

    [Fact]
    public void GastoDesdeAhorro_RestaDelAhorroYNoDelSaldoNiDejaPendiente()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 500, 200) };
        var gastos = new[] { new GastoDeCuenta(null, D("2026-01-05"), 120, DesdeAhorro: true), new GastoDeCuenta(null, D("2026-01-06"), 80) };

        var e = CuentaComun.Calcular(D("2026-01-01"), ap, gastos, []);

        Assert.Equal((80m, 120m), (e.Gastado, e.AhorroGastado));
        Assert.Equal(220m, e.Saldo); // 500 - 200 de ahorro - 80 de gasto normal
        Assert.Equal(80m, e.AhorroDisponible); // 200 - 120
        Assert.Empty(e.Pendientes);
    }

    [Fact]
    public void Reembolso_ComplétoQuitaAlMiembroDePendientes()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 50) };

        var e = CuentaComun.Calcular(D("2026-01-01"), [], gastos, [new ReembolsoDeCuenta(Ana, D("2026-01-06"), 50)]);

        Assert.Empty(e.Pendientes);
        Assert.Equal(-50m, e.Saldo); // la cuenta no cubre el gasto: saldo negativo
    }

    [Fact]
    public void SuParte_RepartePorLoAportadoParaGastosYPorLoAhorradoParaElAhorro()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 600, 200), new AportacionVigente(Beto, D("2026-01-01"), 400) };
        var depositos = new[] { new DepositoDeAhorro(D("2026-01-15"), 100, Beto) };
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 101) };
        var mes = D("2026-01-01");
        var e = CuentaComun.Calcular(mes, ap, gastos, [], null, depositos);

        var partes = CuentaComun.PartesPorPersona(mes, ap, e, depositos);

        var ana = partes.Single(p => p.MiembroId == Ana);
        var beto = partes.Single(p => p.MiembroId == Beto);
        Assert.Equal((400m, 200m, 50m, 66.67m), (ana.Aportado, ana.Ahorrado, ana.PorcentajeGastos, ana.PorcentajeAhorro));
        Assert.Equal((400m, 100m, 33.33m), (beto.Aportado, beto.Ahorrado, beto.PorcentajeAhorro));
        Assert.Equal((349.5m, 349.5m), (ana.ParteSaldo, beto.ParteSaldo));
        Assert.Equal((200m, 100m), (ana.ParteAhorro, beto.ParteAhorro));
        Assert.Equal(101m, ana.Pendiente);
        Assert.Equal(0m, beto.Pendiente);
    }

    [Fact]
    public void SuParte_ElCentimoSobranteLoAbsorbeElUltimoYLasPartesSumanElTotal()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var c = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var ap = new[] { new AportacionVigente(a, D("2026-01-01"), 100), new AportacionVigente(b, D("2026-01-01"), 100), new AportacionVigente(c, D("2026-01-01"), 100) };
        var mes = D("2026-01-01");
        var e = CuentaComun.Calcular(mes, ap, [new GastoDeCuenta(null, D("2026-01-02"), 100)], []); // saldo 200 entre tres

        var partes = CuentaComun.PartesPorPersona(mes, ap, e);

        Assert.Equal(200m, partes.Sum(p => p.ParteSaldo));
        Assert.Equal([66.67m, 66.67m, 66.66m], partes.Select(p => p.ParteSaldo));
    }

    [Fact]
    public void SuParte_ConSaldoNegativoRepartePorIgualElDescubiertoYSinAportacionesNoHayPartes()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 300), new AportacionVigente(Beto, D("2026-01-01"), 100) };
        var mes = D("2026-01-01");
        var e = CuentaComun.Calcular(mes, ap, [new GastoDeCuenta(null, D("2026-01-02"), 500)], []); // saldo -100

        var partes = CuentaComun.PartesPorPersona(mes, ap, e);

        Assert.Equal((-75m, -25m), (partes.Single(p => p.MiembroId == Ana).ParteSaldo, partes.Single(p => p.MiembroId == Beto).ParteSaldo));
        Assert.Empty(CuentaComun.PartesPorPersona(mes, [], CuentaComun.Calcular(mes, [], [], [])));
    }

    [Fact]
    public void SuParte_SinAportacionesParaGastosElDescubiertoSeReparteEnPartesIgualesYSuma()
    {
        // Solo aportan ahorro (todo va a ahorro): el peso de cada uno para el saldo es 0, pero el descubierto no puede perderse.
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 100, 100), new AportacionVigente(Beto, D("2026-01-01"), 100, 100) };
        var mes = D("2026-01-01");
        var e = CuentaComun.Calcular(mes, ap, [new GastoDeCuenta(null, D("2026-01-02"), 50)], []); // saldo -50

        var partes = CuentaComun.PartesPorPersona(mes, ap, e);

        Assert.Equal(-50m, partes.Sum(p => p.ParteSaldo));
        Assert.All(partes, p => Assert.Equal(-25m, p.ParteSaldo));
    }

    [Fact]
    public void SuParte_ElDepositoSinMiembroNoSeAtribuyeANadie()
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 100, 100) };
        var depositos = new[] { new DepositoDeAhorro(D("2026-01-02"), 50) };
        var mes = D("2026-01-01");
        var e = CuentaComun.Calcular(mes, ap, [], [], null, depositos);

        var ana = Assert.Single(CuentaComun.PartesPorPersona(mes, ap, e, depositos));

        Assert.Equal(100m, ana.Ahorrado);
        Assert.Equal(150m, ana.ParteAhorro); // el ahorro disponible (150) se reparte entre quienes tienen ahorro atribuido
    }
}
