using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>
/// Deuda de las personas con la cuenta común en un escenario completo (dos personas, los dos fondos y todos los tipos de
/// movimiento): el estado, el resumen mensual, el extracto y el extracto bancario tienen que contar el mismo dinero.
/// </summary>
public class CuentaComunDeudaEscenariosTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Luis = Guid.NewGuid();
    private static readonly DateOnly Septiembre = new(2026, 9, 1), Octubre = new(2026, 10, 1);

    private static readonly List<AportacionVigente> Aportaciones =
    [
        new(Ana, Septiembre, 300m, 50m, FrecuenciaAportacion.Mensual),
        new(Luis, Septiembre, 200m, 0m, FrecuenciaAportacion.Mensual),
    ];

    // Ana adelantó 30 de Hogar y Luis 20 desde el ahorro (la cuenta se los debe).
    private static readonly List<GastoDeCuenta> Gastos =
    [
        new(Ana, new DateOnly(2026, 10, 2), 30m),
        new(Luis, new DateOnly(2026, 10, 2), 20m, DesdeAhorro: true),
    ];

    private static readonly List<MovimientoDeuda> Deudas =
    [
        new(Ana, new DateOnly(2026, 9, 10), 25m, TipoMovimientoDeuda.Adelanto),
        new(Luis, new DateOnly(2026, 9, 12), 8m, TipoMovimientoDeuda.Adelanto, DesdeAhorro: true),
        new(Luis, new DateOnly(2026, 10, 3), 40m, TipoMovimientoDeuda.Adelanto),
        new(Luis, new DateOnly(2026, 10, 4), 10m, TipoMovimientoDeuda.Devolucion),
        new(Ana, new DateOnly(2026, 10, 5), 10m, TipoMovimientoDeuda.Compensacion),
        new(Ana, new DateOnly(2026, 10, 6), 15m, TipoMovimientoDeuda.Adelanto, DesdeAhorro: true),
        new(Luis, new DateOnly(2026, 10, 7), 5m, TipoMovimientoDeuda.Compensacion, DesdeAhorro: true),
    ];

    private static EstadoCuentaComun Estado(DateOnly mes, IEnumerable<MovimientoDeuda>? deudas = null)
        => CuentaComun.Calcular(mes, Aportaciones, Gastos.Where(g => g.Fecha < mes.AddMonths(1)), [], deudas: deudas);

    private static HistoricoCuenta Historico(DateOnly mes)
    {
        var gastos = Gastos.Where(g => g.Fecha < mes.AddMonths(1)).ToList();
        return new HistoricoCuenta(
            Aportaciones, gastos, [], [], [], true, 0m, gastos.Where(g => g.Fecha >= mes).ToList(),
            Deudas: Deudas.Where(d => d.Fecha < mes.AddMonths(1)).ToList());
    }

    [Fact]
    public void Deudas_SumanAdelantosYRestanDevolucionesYCompensaciones_PorPersonaYFondo()
    {
        var neto = CuentaComun.Deudas(Deudas);

        // Ana: Hogar 25 − 10 = 15; ahorro 15. Luis: Hogar 40 − 10 = 30; ahorro 8 − 5 = 3.
        Assert.Equal(15m, neto.Single(d => d.MiembroId == Ana && !d.DesdeAhorro).Importe);
        Assert.Equal(15m, neto.Single(d => d.MiembroId == Ana && d.DesdeAhorro).Importe);
        Assert.Equal(30m, neto.Single(d => d.MiembroId == Luis && !d.DesdeAhorro).Importe);
        Assert.Equal(3m, neto.Single(d => d.MiembroId == Luis && d.DesdeAhorro).Importe);
    }

    [Fact]
    public void Deudas_SaldadasDesaparecenDeLaLista()
    {
        var neto = CuentaComun.Deudas(
        [
            new MovimientoDeuda(Ana, Octubre, 20m, TipoMovimientoDeuda.Adelanto),
            new MovimientoDeuda(Ana, Octubre, 12m, TipoMovimientoDeuda.Devolucion),
            new MovimientoDeuda(Ana, Octubre, 8m, TipoMovimientoDeuda.Compensacion),
        ]);

        Assert.Empty(neto);
    }

    [Fact]
    public void Los_movimientos_de_meses_posteriores_no_cambian_el_estado_de_un_mes()
    {
        var sinDeudas = Estado(Septiembre);
        var conDeudasFuturas = Estado(Septiembre, Deudas.Where(d => d.Fecha >= Octubre));

        Assert.Equal(sinDeudas.Efectivo, conDeudasFuturas.Efectivo);
        Assert.Equal(sinDeudas.AhorroDisponible, conDeudasFuturas.AhorroDisponible);
        Assert.Empty(conDeudasFuturas.Deudas!);
    }

    [Fact]
    public void Efectivo_EsElSaldoMasLosPendientes_SinSumarLasDeudas()
    {
        var estado = Estado(Octubre, Deudas);

        // Los pendientes ya descuentan los adelantos y suman las devoluciones, pero no las compensaciones; el efectivo sí las resta.
        var adelantos = Deudas.Where(d => d.Tipo == TipoMovimientoDeuda.Adelanto).Sum(d => d.Importe);
        var devoluciones = Deudas.Where(d => d.Tipo == TipoMovimientoDeuda.Devolucion).Sum(d => d.Importe);
        var compensaciones = Deudas.Where(d => d.Tipo == TipoMovimientoDeuda.Compensacion).Sum(d => d.Importe);
        Assert.Equal(estado.Saldo + estado.Pendientes.Sum(p => p.Importe) + adelantos - devoluciones - compensaciones, estado.Efectivo);
    }

    [Fact]
    public void ResumenMensual_TieneElDisponibleDelEstado_Y_CadaFilaCuadra()
    {
        var estado = Estado(Octubre, Deudas);

        var resumen = CuentaComun.ResumenMensual(Octubre, Historico(Octubre));

        Assert.Equal(estado.Efectivo, resumen.Hogar.Disponible);
        Assert.Equal(estado.AhorroDisponible, resumen.Ahorro.Disponible);
        Assert.Equal(estado.Efectivo + estado.AhorroDisponible, resumen.Total.Disponible);
        foreach (var fila in new[] { resumen.Hogar, resumen.Ahorro, resumen.Total })
            Assert.Equal(fila.Inicial + fila.Ingresos + fila.IngresosSueltos - fila.Gastos - fila.Reembolsos, fila.Disponible);
    }

    [Fact]
    public void ResumenMensual_ElInicialDeOctubreEsElDisponibleDeSeptiembre()
    {
        var septiembre = CuentaComun.ResumenMensual(Septiembre, Historico(Septiembre));
        var octubre = CuentaComun.ResumenMensual(Octubre, Historico(Octubre));

        Assert.Equal(septiembre.Hogar.Disponible, octubre.Hogar.Inicial);
        Assert.Equal(septiembre.Ahorro.Disponible, octubre.Ahorro.Inicial);
    }

    [Fact]
    public void Extracto_EmpiezaDondeAcabaElMesAnterior_Y_AcabaEnElEfectivo()
    {
        var estado = Estado(Octubre, Deudas);
        var anterior = Estado(Septiembre, Deudas);

        var extracto = CuentaComun.Extracto(Octubre, Aportaciones, Gastos, [], null, estado, deudasDelMes: Deudas);

        Assert.Equal(estado.Efectivo, extracto.EfectivoFinal);
        Assert.Equal(anterior.Efectivo, extracto.EfectivoInicial);
    }

    [Fact]
    public void ExtractoBancario_SumaElEfectivoYElAhorro_Y_LosRepartaComoElEstado()
    {
        var estado = Estado(Octubre, Deudas);
        var datos = new DatosMovimientosBanco(
            Aportaciones, null, [], [], [], [], [],
            AdelantadosDelAhorro: [new GastoPagadoPorCuenta(Guid.NewGuid(), new DateOnly(2026, 10, 2), 20m, Guid.NewGuid(), null, true, Luis)],
            Deudas: Deudas);

        var banco = MovimientosBanco.Calcular(Octubre, Octubre.AddMonths(1).AddDays(-1), datos);

        Assert.Equal(estado.Efectivo + estado.AhorroDisponible, banco.SaldoFinal);
        Assert.Equal(estado.Efectivo, banco.EfectivoFinal);
        Assert.Equal(estado.AhorroDisponible, banco.AhorroFinal);
    }

    [Fact]
    public void ExtractoBancario_ClasificaCadaMovimientoSegunSuFondo()
    {
        var datos = new DatosMovimientosBanco(Aportaciones, null, [], [], [], [], [], Deudas: Deudas);

        var banco = MovimientosBanco.Calcular(Septiembre, new DateOnly(2026, 10, 31), datos);
        var tipos = banco.Movimientos.Select(m => m.Tipo).ToList();

        Assert.Equal(2, tipos.Count(t => t == TipoMovimientoBanco.AdelantoPersona));
        Assert.Equal(2, tipos.Count(t => t == TipoMovimientoBanco.AdelantoPersonaAhorro));
        Assert.Single(tipos.Where(t => t == TipoMovimientoBanco.DevolucionDeuda));
        Assert.Equal(2, tipos.Count(t => t == TipoMovimientoBanco.CompensacionDeuda));
        // Una compensación nunca mueve dinero en el banco; la del ahorro solo cambia su reparto entre efectivo y ahorro.
        Assert.All(banco.Movimientos.Where(m => m.Tipo == TipoMovimientoBanco.CompensacionDeuda), m => Assert.Equal(0m, m.Importe));
        Assert.Equal(5m, banco.Movimientos.Single(m => m.Tipo == TipoMovimientoBanco.CompensacionDeuda && m.Ahorro != 0m).Ahorro);
    }

    [Theory]
    [InlineData(TipoMovimientoBanco.AdelantoPersona, "adelanto_persona")]
    [InlineData(TipoMovimientoBanco.AdelantoPersonaAhorro, "adelanto_persona_ahorro")]
    [InlineData(TipoMovimientoBanco.DevolucionDeuda, "devolucion_deuda")]
    [InlineData(TipoMovimientoBanco.DevolucionDeudaAhorro, "devolucion_deuda_ahorro")]
    [InlineData(TipoMovimientoBanco.CompensacionDeuda, "compensacion_deuda")]
    public void TiposDeMovimientoBanco_ViajanConSuTextoDeLaApi(TipoMovimientoBanco tipo, string texto)
        => Assert.Equal(texto, MovimientosBanco.ATexto(tipo));
}
