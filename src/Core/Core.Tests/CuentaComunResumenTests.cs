using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

public class CuentaComunResumenTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Beto = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static DateOnly D(string s) => DateOnly.Parse(s);

    /// <summary>Histórico hasta el fin de <paramref name="mes"/>: Ana aporta 600 (100 a ahorro), Beto 400, y hay movimientos en enero y febrero.</summary>
    private static HistoricoCuenta Historico(DateOnly mes)
    {
        var ap = new[] { new AportacionVigente(Ana, D("2026-01-01"), 600, 100), new AportacionVigente(Beto, D("2026-01-01"), 400) };
        var gastos = new[]
        {
            new GastoDeCuenta(null, D("2026-01-05"), 200),
            new GastoDeCuenta(Ana, D("2026-01-10"), 150, DesdeAhorro: true),
            new GastoDeCuenta(null, D("2026-02-04"), 100),
        };
        var reembolsos = new[] { new ReembolsoDeCuenta(Ana, D("2026-01-20"), 50) };
        var retiradas = new[] { new RetiradaDeAhorro(D("2026-02-03"), 20) };
        var depositos = new[] { new DepositoDeAhorro(D("2026-01-12"), 100, Beto) };
        var ingresos = new[] { new IngresoDeCuenta(D("2026-01-15"), 30) };
        var fin = mes.AddMonths(1);
        return new HistoricoCuenta(
            ap, gastos.Where(g => g.Fecha < fin).ToList(), reembolsos.Where(r => r.Fecha < fin).ToList(),
            retiradas.Where(r => r.Fecha < fin).ToList(), depositos.Where(d => d.Fecha < fin).ToList(), true,
            GastadoMes: 0m, GastosDelMes: gastos, Ingresos: ingresos.Where(i => i.Fecha < fin).ToList());
    }

    [Fact]
    public void Resumen_DisponibleDeHogarEsElEfectivoDeLaCuenta()
    {
        var enero = D("2026-01-01");
        var febrero = D("2026-02-01");
        var hEnero = Historico(enero);
        var hFebrero = Historico(febrero);

        var efectivoEnero = CuentaComun.Calcular(enero, hEnero.Vigentes, hEnero.Gastos, hEnero.Reembolsos, hEnero.Retiradas, hEnero.Depositos, null, hEnero.Ingresos).Efectivo;
        var efectivoFebrero = CuentaComun.Calcular(febrero, hFebrero.Vigentes, hFebrero.Gastos, hFebrero.Reembolsos, hFebrero.Retiradas, hFebrero.Depositos, null, hFebrero.Ingresos).Efectivo;

        Assert.Equal(efectivoEnero, CuentaComun.ResumenMensual(enero, hEnero).Hogar.Disponible);
        Assert.Equal(efectivoFebrero, CuentaComun.ResumenMensual(febrero, hFebrero).Hogar.Disponible);
    }

    [Fact]
    public void Resumen_DisponibleDeAhorroEsElAhorroDisponible()
    {
        var enero = D("2026-01-01");
        var febrero = D("2026-02-01");

        var r1 = CuentaComun.ResumenMensual(enero, Historico(enero));
        var r2 = CuentaComun.ResumenMensual(febrero, Historico(febrero));

        Assert.Equal(50m, r1.Ahorro.Disponible);
        Assert.Equal(130m, r2.Ahorro.Disponible);
    }

    [Fact]
    public void Resumen_InicialDeCadaMesEsElDisponibleDelAnterior()
    {
        var enero = D("2026-01-01");
        var febrero = D("2026-02-01");

        var r1 = CuentaComun.ResumenMensual(enero, Historico(enero));
        var r2 = CuentaComun.ResumenMensual(febrero, Historico(febrero));

        Assert.Equal(0m, r1.Hogar.Inicial);
        Assert.Equal(r1.Hogar.Disponible, r2.Hogar.Inicial);
        Assert.Equal(r1.Ahorro.Disponible, r2.Ahorro.Inicial);
    }

    [Fact]
    public void Resumen_ColumnasDelMesHogar()
    {
        var enero = D("2026-01-01");

        var hogar = CuentaComun.ResumenMensual(enero, Historico(enero)).Hogar;

        // Aportado para gastos (900) más el gasto adelantado desde el ahorro (150) entran; ingreso suelto 30; gasto directo 200; reembolso 50.
        Assert.Equal(1050m, hogar.Ingresos);
        Assert.Equal(30m, hogar.IngresosSueltos);
        Assert.Equal(200m, hogar.Gastos);
        Assert.Equal(50m, hogar.Reembolsos);
        Assert.Equal(830m, hogar.Disponible);
    }

    [Fact]
    public void Resumen_ColumnasDelMesAhorro()
    {
        var febrero = D("2026-02-01");

        var ahorro = CuentaComun.ResumenMensual(febrero, Historico(febrero)).Ahorro;

        // Aportación al ahorro del mes (100), sin depósitos; gastos desde el ahorro del mes: la retirada (20).
        Assert.Equal(100m, ahorro.Ingresos);
        Assert.Equal(0m, ahorro.IngresosSueltos);
        Assert.Equal(20m, ahorro.Gastos);
        Assert.Equal(0m, ahorro.Reembolsos);
    }

    [Fact]
    public void Resumen_TotalSumaHogarYAhorro()
    {
        var febrero = D("2026-02-01");

        var r = CuentaComun.ResumenMensual(febrero, Historico(febrero));

        Assert.Equal(r.Hogar.Disponible + r.Ahorro.Disponible, r.Total.Disponible);
        Assert.Equal(r.Hogar.Inicial + r.Ahorro.Inicial, r.Total.Inicial);
    }
}
