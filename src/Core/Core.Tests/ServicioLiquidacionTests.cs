using MiParte.Core.Application;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Casos de uso de liquidación probados sin HTTP ni base de datos, con un repositorio en memoria.</summary>
public class ServicioLiquidacionTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Mes = new(2026, 9, 1);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid();
    private readonly RepositorioFalso _repo = new();

    private ServicioLiquidacion Servicio => new(_repo, TimeProvider.System);

    private void DosAdultos()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos" });
    }

    /// <summary>Gasto de <paramref name="importe"/> pagado por Ana y repartido a partes iguales.</summary>
    private void GastoPagadoPorAna(decimal importe)
    {
        var mitad = importe / 2;
        var g = new Gasto { Id = Guid.NewGuid(), HogarId = Hogar, Fecha = Mes.AddDays(3), Importe = importe, PagadoPor = _ana };
        g.Repartos.Add(new GastoReparto { GastoId = g.Id, HogarId = Hogar, MiembroId = _ana, ImporteAsumido = mitad });
        g.Repartos.Add(new GastoReparto { GastoId = g.Id, HogarId = Hogar, MiembroId = _marcos, ImporteAsumido = mitad });
        _repo.Gastos.Add(g);
    }

    private RegistrarPago Pago(decimal importe, Guid? de = null, Guid? a = null, DateOnly? mes = null) =>
        new(Hogar, mes ?? Mes, de ?? _marcos, a ?? _ana, importe, null, null);

    [Fact]
    public async Task Liquidacion_Marcos_DebeLaMitadAAna()
    {
        DosAdultos();
        GastoPagadoPorAna(100m);

        var l = await Servicio.LiquidacionAsync(Mes, default);

        var t = Assert.Single(l.Transferencias);
        Assert.Equal((_marcos, _ana, 50m), (t.De, t.A, t.Importe));
        Assert.Equal(50m, l.PendienteEntre(_marcos, _ana));
    }

    [Fact]
    public async Task ResumenYLiquidacion_DanLosMismosImportesQueLasConsultasSueltas()
    {
        DosAdultos();
        GastoPagadoPorAna(80m);

        var juntas = await Servicio.ResumenYLiquidacionAsync(Mes, default);

        var sueltas = await Servicio.ResumenAsync(Mes, default);
        Assert.Equal(sueltas.GastosTotales, juntas.Resumen.GastosTotales);
        Assert.Equal(sueltas.PorMiembro, juntas.Resumen.PorMiembro);
        Assert.Equal(80m, juntas.Resumen.GastosTotales);
        Assert.Equal((await Servicio.LiquidacionAsync(Mes, default)).Saldos, juntas.Liquidacion.Saldos);
    }

    [Fact]
    public async Task RegistrarPago_ConImporteQueSuperaLaDeuda_EsConflictoConLoPendiente()
    {
        DosAdultos();
        GastoPagadoPorAna(100m);

        var r = await Servicio.RegistrarPagoAsync(Pago(50.01m), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(50m, r.Error.Extra!["pendiente"]);
        Assert.Empty(_repo.Pagos);
    }

    [Fact]
    public async Task RegistrarPago_DentroDeLaDeuda_LoGuardaYBajaLoPendiente()
    {
        DosAdultos();
        GastoPagadoPorAna(100m);

        var r = await Servicio.RegistrarPagoAsync(Pago(20m), default);

        Assert.Null(r.Error);
        Assert.Single(_repo.Pagos);
        Assert.Equal(30m, (await Servicio.LiquidacionAsync(Mes, default)).PendienteEntre(_marcos, _ana));
    }

    [Fact]
    public async Task RegistrarPago_EnMesSinGastos_SoloValidaElImporte()
    {
        DosAdultos();

        var r = await Servicio.RegistrarPagoAsync(Pago(500m), default);

        Assert.Null(r.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.123)]
    public async Task RegistrarPago_ConImporteInvalido_EsInvalido(double importe)
    {
        DosAdultos();

        var r = await Servicio.RegistrarPagoAsync(Pago((decimal)importe), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task RegistrarPago_ConMesQueNoEsDia1_EsInvalido()
    {
        DosAdultos();

        var r = await Servicio.RegistrarPagoAsync(Pago(10m, mes: Mes.AddDays(4)), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task RegistrarPago_ConLosMismosMiembros_EsInvalido()
    {
        DosAdultos();

        var r = await Servicio.RegistrarPagoAsync(Pago(10m, de: _ana, a: _ana), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task RegistrarPago_ConMiembroAjenoAlHogar_EsInvalido()
    {
        DosAdultos();

        var r = await Servicio.RegistrarPagoAsync(Pago(10m, de: Guid.NewGuid()), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task EliminarPago_Inexistente_EsNoEncontrado()
    {
        var r = await Servicio.EliminarPagoAsync(Guid.NewGuid(), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    /// <summary>Repositorio en memoria: cada lista es lo que «hay» en el hogar.</summary>
    private sealed class RepositorioFalso : IRepositorioLiquidacion
    {
        public List<Gasto> Gastos { get; } = [];
        public List<Miembro> Miembros { get; } = [];
        public List<PagoLiquidacionRegistro> Pagos { get; } = [];

        public Task<IReadOnlyList<Gasto>> GastosDelMesAsync(DateOnly inicio, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Gasto>>(Gastos.Where(g => g.Fecha >= inicio && g.Fecha < inicio.AddMonths(1)).ToList());

        public Task<IReadOnlyList<Miembro>> MiembrosAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Miembro>>(Miembros);

        public Task<IReadOnlyDictionary<Guid, string>> NombresCategoriasAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<HistoricoCuenta> HistoricoCuentaAsync(DateOnly inicio, CancellationToken ct)
            => Task.FromResult(new HistoricoCuenta([], [], [], [], [], false, 0m));

        public Task<IReadOnlyList<PagoLiquidacionRegistro>> PagosDelMesAsync(DateOnly inicio, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PagoLiquidacionRegistro>>(Pagos.Where(p => p.Mes == inicio).ToList());

        public Task<bool> ExistenAmbosAsync(Guid uno, Guid otro, CancellationToken ct)
            => Task.FromResult(Miembros.Any(m => m.Id == uno) && Miembros.Any(m => m.Id == otro));

        public Task AnadirPagoAsync(PagoLiquidacionRegistro pago, CancellationToken ct)
        {
            Pagos.Add(pago);
            return Task.CompletedTask;
        }

        public Task<bool> EliminarPagoAsync(Guid id, CancellationToken ct) => Task.FromResult(Pagos.RemoveAll(p => p.Id == id) > 0);
    }
}
