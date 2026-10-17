using MiParte.Core.Application;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>
/// Liquidación por ciclos: cerrar un ciclo congela los gastos pendientes, el siguiente ciclo empieza con lo que llegue después
/// y el modo mensual sigue igual. Se prueba con el repositorio en memoria de <see cref="ServicioLiquidacionTests"/>.
/// </summary>
public class LiquidacionCiclosTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Mes = new(2026, 9, 1);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid();
    private readonly ServicioLiquidacionTests.RepositorioFalso _repo = new();

    private ServicioLiquidacion Servicio => new(_repo, TimeProvider.System);

    private Miembro Yo => _repo.Miembros.First(m => m.Id == _ana);

    public LiquidacionCiclosTests()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos" });
        _repo.Modo = ModoLiquidacion.Ciclos;
    }

    /// <summary>Gasto de Ana de <paramref name="importe"/> repartido a partes iguales con Marcos, en la fecha indicada.</summary>
    private Gasto GastoDeAna(decimal importe, DateOnly? fecha = null)
    {
        var mitad = importe / 2;
        var g = new Gasto { Id = Guid.NewGuid(), HogarId = Hogar, Fecha = fecha ?? Mes.AddDays(3), Importe = importe, PagadoPor = _ana };
        g.Repartos.Add(new GastoReparto { GastoId = g.Id, HogarId = Hogar, MiembroId = _ana, ImporteAsumido = mitad });
        g.Repartos.Add(new GastoReparto { GastoId = g.Id, HogarId = Hogar, MiembroId = _marcos, ImporteAsumido = mitad });
        _repo.Gastos.Add(g);
        return g;
    }

    [Fact]
    public async Task CerrarCiclo_CreaElCicloYMarcaLosGastosPendientes()
    {
        var a = GastoDeAna(100, new DateOnly(2026, 10, 3));
        var b = GastoDeAna(50, new DateOnly(2026, 10, 5));

        var r = await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        Assert.Null(r.Error);
        Assert.Equal(1, r.Valor!.Ciclo.Numero);
        Assert.Equal(150m, r.Valor.Ciclo.Total);
        Assert.Equal(new DateOnly(2026, 10, 3), r.Valor.Ciclo.DesdeGastoFecha);
        Assert.Equal(new DateOnly(2026, 10, 5), r.Valor.Ciclo.HastaFecha);
        Assert.Equal(_ana, r.Valor.Ciclo.CerradoPor);
        Assert.Equal("Ana", r.Valor.Autor);
        Assert.Equal(r.Valor.Ciclo.Id, a.CicloLiquidacionId);
        Assert.Equal(r.Valor.Ciclo.Id, b.CicloLiquidacionId);
        // Cerrar no bloquea: los gastos siguen ahí y consultables.
        Assert.Equal(2, _repo.Gastos.Count);
    }

    [Fact]
    public async Task CerrarCiclo_SinGastosPendientes_EsConflicto()
    {
        var r = await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("No hay gastos sin liquidar", r.Error.Mensaje);
        Assert.Empty(_repo.Ciclos);
    }

    [Fact]
    public async Task CerrarCiclo_YaLiquidadoTodo_EsConflictoLaSegundaVez()
    {
        GastoDeAna(100);
        await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        var r = await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Single(_repo.Ciclos);
    }

    [Fact]
    public async Task GastoNuevoTrasCerrar_EntraEnElSiguienteCiclo()
    {
        var primero = GastoDeAna(100);
        var primerCiclo = (await Servicio.CerrarCicloAsync(Yo, CancellationToken.None)).Valor!.Ciclo;

        var nuevo = GastoDeAna(40, new DateOnly(2026, 10, 20));
        var segundo = (await Servicio.CerrarCicloAsync(Yo, CancellationToken.None)).Valor!.Ciclo;

        Assert.Equal(2, segundo.Numero);
        Assert.Equal(40m, segundo.Total);
        Assert.Equal(primerCiclo.Id, primero.CicloLiquidacionId);
        Assert.Equal(segundo.Id, nuevo.CicloLiquidacionId);
        Assert.Equal(100m, primerCiclo.Total); // el primer ciclo no cambia
    }

    [Fact]
    public async Task LiquidacionCiclos_SoloCuentaLosGastosPendientes()
    {
        GastoDeAna(100);
        await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        var saldo = await Servicio.LiquidacionAsync(Mes, CancellationToken.None);
        Assert.False(saldo.HayGastos);
        Assert.Empty(saldo.Transferencias);

        GastoDeAna(60, new DateOnly(2026, 10, 9));
        saldo = await Servicio.LiquidacionAsync(Mes, CancellationToken.None);
        Assert.True(saldo.HayGastos);
        Assert.Equal(-30m, saldo.Saldos.First(s => s.MiembroId == _marcos).Importe);
    }

    [Fact]
    public async Task ModoMensual_CerrarEsConflicto_Y_LaLiquidacionSigueSiendoLaDelMes()
    {
        _repo.Modo = ModoLiquidacion.Mensual;
        GastoDeAna(100);

        var cierre = await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);
        var liquidacion = await Servicio.LiquidacionAsync(Mes, CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, cierre.Error!.Tipo);
        Assert.Empty(_repo.Ciclos);
        Assert.Equal(-50m, liquidacion.Saldos.First(s => s.MiembroId == _marcos).Importe);
        Assert.Equal(ModoLiquidacion.Mensual, await Servicio.CambiarModoAsync(ModoLiquidacion.Mensual, CancellationToken.None));
    }

    [Fact]
    public async Task CambiarDeModo_NoTocaCiclosNiGastosYaCerrados()
    {
        var g = GastoDeAna(100);
        var ciclo = (await Servicio.CerrarCicloAsync(Yo, CancellationToken.None)).Valor!.Ciclo;

        await Servicio.CambiarModoAsync(ModoLiquidacion.Mensual, CancellationToken.None);
        var ciclos = await Servicio.ListarCiclosAsync(CancellationToken.None);
        await Servicio.CambiarModoAsync(ModoLiquidacion.Ciclos, CancellationToken.None);

        Assert.Single(ciclos);
        Assert.Equal(ciclo.Id, ciclos[0].Ciclo.Id);
        Assert.Equal(100m, ciclos[0].Ciclo.Total);
        Assert.Equal(ciclo.Id, g.CicloLiquidacionId);
        Assert.Single(_repo.Ciclos);
    }

    [Fact]
    public async Task ModoCiclos_NoSeRegistranPagos()
    {
        GastoDeAna(100);

        var r = await Servicio.RegistrarPagoAsync(
            new RegistrarPago(Hogar, Mes, _marcos, _ana, 10m, null, null), CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(Mensajes.PagosEnCiclos, r.Error.Mensaje);
        Assert.Empty(_repo.Pagos);
    }

    [Fact]
    public void Resumir_SumaLosImportesYLasFechasExtremas()
    {
        var gastos = new List<Gasto>
        {
            new() { Importe = 30m, Fecha = new DateOnly(2026, 10, 12) },
            new() { Importe = 12.50m, Fecha = new DateOnly(2026, 10, 4) },
        };

        var (total, desde, hasta) = CalculoCiclo.Resumir(gastos);

        Assert.Equal(42.50m, total);
        Assert.Equal(new DateOnly(2026, 10, 4), desde);
        Assert.Equal(new DateOnly(2026, 10, 12), hasta);
    }

    [Fact]
    public void Resumir_SinGastos_Lanza()
    {
        Assert.Throws<ArgumentException>(() => CalculoCiclo.Resumir([]));
    }

    [Theory]
    [InlineData(null, "mensual")]
    [InlineData("", "mensual")]
    [InlineData("  Ciclos ", "ciclos")]
    [InlineData("MENSUAL", "mensual")]
    public void TextoModo_AceptaLosValoresConocidos(string? texto, string esperado)
    {
        Assert.True(TextoModoLiquidacion.TryParse(texto, out var modo));
        Assert.Equal(esperado, TextoModoLiquidacion.ATexto(modo));
    }

    [Fact]
    public void TextoModo_RechazaUnValorDesconocido()
    {
        Assert.False(TextoModoLiquidacion.TryParse("semanal", out _));
    }
}
