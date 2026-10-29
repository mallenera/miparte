using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>Cálculo de la deuda de las personas con la cuenta común: adelantos, devoluciones y compensaciones, sin persistencia.</summary>
public class CuentaComunDeudaTests
{
    private static readonly DateOnly Septiembre = new(2026, 9, 1);
    private static readonly Guid Ana = Guid.NewGuid();

    private static readonly List<AportacionVigente> Aportaciones =
        [new(Ana, Septiembre, 100m, 0m, FrecuenciaAportacion.Mensual)];

    [Fact]
    public void Adelanto_DeLaCuenta_BajaElEfectivoYLaPersonaLeDebe()
    {
        var deudas = new[] { new MovimientoDeuda(Ana, Septiembre.AddDays(3), 40m, TipoMovimientoDeuda.Adelanto) };

        var estado = CuentaComun.Calcular(Septiembre, Aportaciones, [], [], deudas: deudas);

        Assert.Equal(60m, estado.Efectivo);
        Assert.Equal([new DeudaMiembro(Ana, 40m)], estado.Deudas);
    }

    [Fact]
    public void Devolucion_EntraEnLaCuentaYBajaLaDeuda()
    {
        var deudas = new[]
        {
            new MovimientoDeuda(Ana, Septiembre.AddDays(3), 40m, TipoMovimientoDeuda.Adelanto),
            new MovimientoDeuda(Ana, Septiembre.AddDays(4), 15m, TipoMovimientoDeuda.Devolucion),
        };

        var estado = CuentaComun.Calcular(Septiembre, Aportaciones, [], [], deudas: deudas);

        Assert.Equal(75m, estado.Efectivo);
        Assert.Equal([new DeudaMiembro(Ana, 25m)], estado.Deudas);
    }

    [Fact]
    public void Compensacion_NoMueveElEfectivoPeroRestaLoQueLaCuentaDebe()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, Septiembre.AddDays(2), 30m) };
        var adelanto = new MovimientoDeuda(Ana, Septiembre.AddDays(3), 10m, TipoMovimientoDeuda.Adelanto);
        var compensacion = new MovimientoDeuda(Ana, Septiembre.AddDays(4), 10m, TipoMovimientoDeuda.Compensacion);

        var sinCompensar = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, [], deudas: [adelanto]);
        var compensada = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, [], deudas: [adelanto, compensacion]);

        Assert.Equal(sinCompensar.Efectivo, compensada.Efectivo);
        // La compensación resta a la vez lo que la cuenta le debe y lo que ella le debe: lo pendiente sigue siendo 30 − 10.
        Assert.Equal(20m, compensada.Pendientes.Single().Importe);
        Assert.Equal(sinCompensar.Saldo + 10m, compensada.Saldo);
        Assert.Empty(compensada.Deudas!);
    }

    [Fact]
    public void Compensacion_DesdeElAhorro_PasaLoPendienteAlAhorro()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, Septiembre.AddDays(2), 30m, DesdeAhorro: true) };
        var deudas = new[] { new MovimientoDeuda(Ana, Septiembre.AddDays(3), 10m, TipoMovimientoDeuda.Compensacion, DesdeAhorro: true) };

        var sinCompensar = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, []);
        var compensada = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, [], deudas: deudas);

        Assert.Equal(sinCompensar.Efectivo - 10m, compensada.Efectivo);
        Assert.Equal(sinCompensar.AhorroDisponible + 10m, compensada.AhorroDisponible);
    }

    [Fact]
    public void Adelanto_DescuentaLoQueLaCuentaLeDebe_YPuedeQuedarNegativoHastaQueLoDevuelve()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, Septiembre.AddDays(2), 30m) };
        var adelanto = new MovimientoDeuda(Ana, Septiembre.AddDays(3), 40m, TipoMovimientoDeuda.Adelanto);
        var devolucion = new MovimientoDeuda(Ana, Septiembre.AddDays(4), 10m, TipoMovimientoDeuda.Devolucion);

        var adelantado = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, [], deudas: [adelanto]);
        var devuelto = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, [], deudas: [adelanto, devolucion]);

        // La cuenta le debía 30 y le ha adelantado 40: ahora ella le debe 10, y el efectivo baja los 40 que salieron.
        Assert.Equal(-10m, adelantado.Pendientes.Single().Importe);
        Assert.Equal([new DeudaMiembro(Ana, 10m)], adelantado.Deudas!);
        Assert.Equal(60m, adelantado.Efectivo);

        // Con su devolución de 10 vuelve a cero: no queda pendiente ni deuda, y el efectivo recupera lo devuelto.
        Assert.Empty(devuelto.Pendientes);
        Assert.Empty(devuelto.Deudas!);
        Assert.Equal(70m, devuelto.Efectivo);
    }

    [Fact]
    public void AdelantoDelAhorro_DescuentaDeLoQueLaCuentaDebeEnElAhorro_YPuedeQuedarNegativo()
    {
        var gastos = new[] { new GastoDeCuenta(Ana, Septiembre.AddDays(2), 30m, DesdeAhorro: true) };
        var deudas = new[] { new MovimientoDeuda(Ana, Septiembre.AddDays(3), 102m, TipoMovimientoDeuda.Adelanto, DesdeAhorro: true) };

        var sinAdelanto = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, []);
        var adelantado = CuentaComun.Calcular(Septiembre, Aportaciones, gastos, [], deudas: deudas);

        // El ahorro le debía 30 por su gasto y la cuenta le adelanta 102: ella le debe 72 al ahorro. El efectivo no cambia (el dinero sale del ahorro).
        Assert.Equal(-72m, adelantado.Pendientes.Single(p => p.DesdeAhorro).Importe);
        Assert.Equal([new DeudaMiembro(Ana, 72m, true)], adelantado.Deudas!);
        Assert.Equal(sinAdelanto.Efectivo, adelantado.Efectivo);
    }

    [Fact]
    public void AdelantoDelAhorro_BajaElAhorroYNoElEfectivo()
    {
        var deudas = new[] { new MovimientoDeuda(Ana, Septiembre.AddDays(3), 25m, TipoMovimientoDeuda.Adelanto, DesdeAhorro: true) };

        var sinDeuda = CuentaComun.Calcular(Septiembre, Aportaciones, [], []);
        var estado = CuentaComun.Calcular(Septiembre, Aportaciones, [], [], deudas: deudas);

        Assert.Equal(sinDeuda.Efectivo, estado.Efectivo);
        Assert.Equal(sinDeuda.AhorroDisponible - 25m, estado.AhorroDisponible);
        Assert.Equal([new DeudaMiembro(Ana, 25m, true)], estado.Deudas);
    }

    [Fact]
    public void Extracto_YBanco_CuadranConLosMovimientosDeDeuda()
    {
        var deudas = new[]
        {
            new MovimientoDeuda(Ana, Septiembre.AddDays(3), 40m, TipoMovimientoDeuda.Adelanto),
            new MovimientoDeuda(Ana, Septiembre.AddDays(4), 15m, TipoMovimientoDeuda.Devolucion),
        };
        var estado = CuentaComun.Calcular(Septiembre, Aportaciones, [], [], deudas: deudas);

        var extracto = CuentaComun.Extracto(Septiembre, Aportaciones, [], [], null, estado, deudasDelMes: deudas);
        var banco = MovimientosBanco.Calcular(
            Septiembre, Septiembre.AddMonths(1).AddDays(-1),
            new DatosMovimientosBanco(Aportaciones, null, [], [], [], [], [], Deudas: deudas));

        Assert.Equal(extracto.EfectivoInicial + extracto.Aportaciones - 40m + 15m, extracto.EfectivoFinal);
        Assert.Equal(extracto.EfectivoFinal, banco.EfectivoFinal);
        Assert.Equal(TipoMovimientoBanco.AdelantoPersona, banco.Movimientos.First(m => m.Importe < 0).Tipo);
    }
}
