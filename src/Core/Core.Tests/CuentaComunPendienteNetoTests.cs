using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>
/// El pendiente del resumen mensual es neto de las deudas de las personas con la cuenta: lo que la cuenta les debe menos lo que
/// ellas le deben, igual que el saldo que se muestra por persona.
/// </summary>
public class CuentaComunPendienteNetoTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Luis = Guid.NewGuid();
    private static readonly DateOnly Octubre = new(2026, 10, 1);

    [Fact]
    public void ResumenMensual_PendienteRestaLoQueUnaPersonaDeboALaCuenta()
    {
        // Ana adelanta 100 para la cuenta (la cuenta le debe 100) y la cuenta paga 40 por Luis (él le debe 40): neto 60.
        var gastos = new List<GastoDeCuenta> { new(Ana, new DateOnly(2026, 10, 2), 100m) };
        var deudas = new List<MovimientoDeuda> { new(Luis, new DateOnly(2026, 10, 3), 40m, TipoMovimientoDeuda.Adelanto) };
        var historico = new HistoricoCuenta([], gastos, [], [], [], true, 0m, gastos, Deudas: deudas);

        var resumen = CuentaComun.ResumenMensual(Octubre, historico);

        Assert.Equal(60m, resumen.Hogar.Pendiente);
        Assert.Equal(60m, resumen.Total.Pendiente);
    }

    [Fact]
    public void ResumenMensual_PendienteEsNegativoSiLasPersonasDebenMasDeLoQueLaCuentaLes()
    {
        // Luis debe 40 a la cuenta y Ana no tiene nada que reembolsar: el pendiente sale negativo.
        var deudas = new List<MovimientoDeuda> { new(Luis, new DateOnly(2026, 10, 3), 40m, TipoMovimientoDeuda.Adelanto) };
        var historico = new HistoricoCuenta([], [], [], [], [], true, 0m, [], Deudas: deudas);

        var resumen = CuentaComun.ResumenMensual(Octubre, historico);

        Assert.Equal(-40m, resumen.Hogar.Pendiente);
    }
}
