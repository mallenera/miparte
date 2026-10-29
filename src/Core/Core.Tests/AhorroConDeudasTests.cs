using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>El ahorro disponible y su holgura cuentan las deudas con el ahorro igual que el estado de la cuenta común.</summary>
public class AhorroConDeudasTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateOnly Septiembre = new(2026, 9, 1), Octubre = new(2026, 10, 1);

    private static readonly List<AportacionVigente> Aportaciones =
        [new(Ana, Septiembre, 100m, 50m, FrecuenciaAportacion.Mensual)];

    [Fact]
    public void UsosPorDeudas_SoloCuentanElAhorro_ConSigno()
    {
        var usos = AhorroCuenta.UsosPorDeudas(
        [
            new MovimientoDeuda(Ana, Septiembre, 30m, TipoMovimientoDeuda.Adelanto, DesdeAhorro: true),
            new MovimientoDeuda(Ana, Septiembre, 10m, TipoMovimientoDeuda.Devolucion, DesdeAhorro: true),
            new MovimientoDeuda(Ana, Septiembre, 5m, TipoMovimientoDeuda.Compensacion, DesdeAhorro: true),
            new MovimientoDeuda(Ana, Septiembre, 99m, TipoMovimientoDeuda.Adelanto), // de Hogar: no toca el ahorro
        ]);

        Assert.Equal([30m, -10m, -5m], usos.Select(u => u.Importe));
        Assert.Equal(15m, usos.Sum(u => u.Importe));
    }

    [Fact]
    public void ElAhorroDisponible_EsElMismoPorElEstadoQuePorLosDatosDelAhorro()
    {
        MovimientoDeuda[] deudas =
        [
            new(Ana, new DateOnly(2026, 9, 10), 40m, TipoMovimientoDeuda.Adelanto, DesdeAhorro: true),
            new(Ana, new DateOnly(2026, 9, 20), 15m, TipoMovimientoDeuda.Devolucion, DesdeAhorro: true),
            new(Ana, new DateOnly(2026, 10, 2), 5m, TipoMovimientoDeuda.Compensacion, DesdeAhorro: true),
        ];
        var depositos = new[] { new DepositoDeAhorro(new DateOnly(2026, 9, 5), 20m) };
        var estado = CuentaComun.Calcular(Octubre, Aportaciones, [], [], null, depositos, null, null, deudas);

        var datos = new DatosAhorro(Aportaciones, depositos, 0m, AhorroCuenta.UsosPorDeudas(deudas).Sum(u => u.Importe));

        Assert.Equal(estado.AhorroDisponible, AhorroCuenta.Disponible(Octubre, datos));
    }

    [Fact]
    public void LaHolgura_CuentaLosAdelantosComoUsos_YLasDevolucionesLaAumentan()
    {
        var adelanto = new UsoDeAhorro(new DateOnly(2026, 9, 10), 50m);
        var devolucion = new UsoDeAhorro(new DateOnly(2026, 9, 20), -20m);

        // Septiembre ahorra 50 y se usan 50 − 20 netos: sobran 20. Sin la devolución, el adelanto lo dejaría justo.
        Assert.Equal(20m, HolguraAhorro.Calcular(Aportaciones, [], [adelanto, devolucion]));
        Assert.Equal(0m, HolguraAhorro.Calcular(Aportaciones, [], [adelanto]));
        // Sin la devolución y con un adelanto mayor, faltaría ahorro.
        Assert.Equal(-10m, HolguraAhorro.Calcular(Aportaciones, [], [new UsoDeAhorro(adelanto.Fecha, 60m)]));
    }

    [Fact]
    public void BorrarUnaDevolucion_EsAnadirUnUsoDelMismoImporteEnSuFecha()
    {
        var adelanto = new UsoDeAhorro(new DateOnly(2026, 9, 10), 50m);
        var devolucion = new UsoDeAhorro(new DateOnly(2026, 9, 20), -30m);
        var retirada = new UsoDeAhorro(new DateOnly(2026, 9, 25), 30m);

        var con = HolguraAhorro.Calcular(Aportaciones, [], [adelanto, devolucion, retirada]);
        var sinLaDevolucion = HolguraAhorro.Calcular(Aportaciones, [], [adelanto, retirada]);
        var simulado = HolguraAhorro.Calcular(Aportaciones, [], [adelanto, devolucion, retirada, new UsoDeAhorro(devolucion.Fecha, 30m)]);

        Assert.Equal(0m, con);
        Assert.Equal(-30m, sinLaDevolucion);
        Assert.Equal(sinLaDevolucion, simulado);
    }
}
