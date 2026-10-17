using MiParte.Core.Application;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Casos de uso de la cuenta común probados sin HTTP ni base de datos, con un repositorio en memoria.</summary>
public class ServicioCuentaComunTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Septiembre = new(2026, 9, 1);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid();
    private readonly RepositorioFalso _repo = new();

    public ServicioCuentaComunTests()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos", Activo = false });
        _repo.Cuenta = true;
        _repo.Ahorro = true;
    }

    private ServicioCuentaComun Servicio => new(_repo, TimeProvider.System);

    private static RegistrarMovimiento Mov(Guid miembro, decimal importe, DateOnly? fecha = null)
        => new(Hogar, miembro, importe, fecha ?? Septiembre.AddDays(5), null);

    [Fact]
    public async Task Aportacion_ConAhorroMayorQueElImporte_EsInvalida()
    {
        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 150m), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Aportacion_DeCero_EsValidaYDejaDeAportar()
    {
        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 0m, 0m), default);

        Assert.Null(r.Error);
    }

    [Fact]
    public async Task Aportacion_DeUnMiembroInactivo_EsInvalida()
    {
        var r = await Servicio.FijarAportacionAsync(new(Hogar, _marcos, Septiembre, 100m, 0m), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Aportacion_ConLaCuentaInactiva_EsConflicto()
    {
        _repo.Cuenta = false;

        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 0m), default);

        Assert.Equal(Mensajes.CuentaInactiva, r.Error!.Mensaje);
    }

    [Fact]
    public async Task Aportacion_ConAhorroYElAhorroInactivo_EsConflicto()
    {
        _repo.Ahorro = false;

        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 40m), default);

        Assert.Equal(Mensajes.AhorroInactivo, r.Error!.Mensaje);
    }

    [Fact]
    public async Task Aportacion_QueRebajaElAhorroYDejaSinRespaldoLoYaRetirado_EsConflictoConLaFalta()
    {
        await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 100m), default);
        var retirada = await Servicio.CrearRetiradaAsync(Mov(_ana, 80m), default);
        Assert.Null(retirada.Error);

        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 50m), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(30m, r.Error.Extra!["falta"]);
    }

    [Fact]
    public async Task Retirada_QueSuperaElAhorroDisponible_EsConflictoConLoDisponible()
    {
        await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 60m), default);

        var r = await Servicio.CrearRetiradaAsync(Mov(_ana, 60.01m), default);

        Assert.Equal(60m, r.Error!.Extra!["disponible"]);
    }

    [Fact]
    public async Task Reembolso_QueSuperaLoPendiente_EsConflictoConLoPendiente()
    {
        _repo.GastadoPor[_ana] = 100m;
        _repo.ReembolsadoA[_ana] = 30m;

        var demasiado = await Servicio.CrearReembolsoAsync(Mov(_ana, 70.01m), default);
        var justo = await Servicio.CrearReembolsoAsync(Mov(_ana, 70m), default);

        Assert.Equal(70m, demasiado.Error!.Extra!["pendiente"]);
        Assert.Null(justo.Error);
    }

    [Fact]
    public async Task Reembolso_DeUnMiembroAjeno_EsInvalido()
    {
        var r = await Servicio.CrearReembolsoAsync(Mov(Guid.NewGuid(), 10m), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task EliminarDeposito_QueDejaSinRespaldoLoRetirado_EsConflicto()
    {
        var deposito = (await Servicio.CrearDepositoAsync(Mov(_ana, 200m), default)).Valor!;
        await Servicio.CrearRetiradaAsync(Mov(_ana, 150m), default);

        var r = await Servicio.EliminarDepositoAsync(deposito.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(150m, r.Error.Extra!["falta"]);
        Assert.Single(_repo.Depositos);
    }

    [Fact]
    public async Task EliminarDeposito_SinUsos_LoBorra()
    {
        var deposito = (await Servicio.CrearDepositoAsync(Mov(_ana, 200m), default)).Valor!;

        var r = await Servicio.EliminarDepositoAsync(deposito.Id, default);

        Assert.Null(r.Error);
        Assert.Empty(_repo.Depositos);
    }

    [Fact]
    public async Task EliminarDeposito_Inexistente_EsNoEncontrado()
    {
        var r = await Servicio.EliminarDepositoAsync(Guid.NewGuid(), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task ActivarAhorro_ExigeLaCuentaActivada()
    {
        _repo.Cuenta = false;

        var r = await Servicio.ActivarAhorroAsync(true, default);

        Assert.Equal(Mensajes.CuentaInactiva, r.Error!.Mensaje);
    }

    [Fact]
    public async Task Estado_SumaLasAportacionesYDevuelveLoAhorrado()
    {
        await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 40m), default);

        var s = await Servicio.EstadoAsync(Septiembre, default);

        Assert.Equal(100m, s.Estado.Aportado);
        Assert.Equal(40m, s.Estado.AhorroDisponible);
        Assert.Equal(60m, s.Estado.Saldo);
    }

    [Fact]
    public async Task El_saldo_inicial_se_suma_al_saldo_y_aparece_en_el_extracto()
    {
        var r = await Servicio.FijarSaldoInicialAsync(Septiembre.AddDays(9), 300m, default);

        Assert.Null(r.Error);
        var estado = await Servicio.EstadoAsync(Septiembre, default);
        Assert.Equal(300m, estado.Estado.Saldo);
        Assert.Equal(300m, estado.SaldoInicial!.Importe);
        Assert.Equal(300m, estado.Extracto.SaldoInicialFijado);
        Assert.Equal(0m, estado.Extracto.EfectivoInicial);
        Assert.Equal(300m, estado.Extracto.EfectivoFinal);
    }

    [Fact]
    public async Task El_saldo_inicial_con_la_cuenta_inactiva_es_conflicto()
    {
        _repo.Cuenta = false;

        var r = await Servicio.FijarSaldoInicialAsync(Septiembre, 300m, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Null(_repo.Saldo);
    }

    [Fact]
    public async Task El_saldo_inicial_no_puede_ser_negativo()
    {
        var r = await Servicio.FijarSaldoInicialAsync(Septiembre, -1m, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Null(_repo.Saldo);
    }

    [Fact]
    public async Task Quitar_el_saldo_inicial_sin_tenerlo_no_existe()
    {
        var sin = await Servicio.EliminarSaldoInicialAsync(default);
        Assert.Equal(TipoError.NoEncontrado, sin.Error!.Tipo);

        await Servicio.FijarSaldoInicialAsync(Septiembre, 300m, default);
        var quitado = await Servicio.EliminarSaldoInicialAsync(default);

        Assert.Null(quitado.Error);
        Assert.Null(_repo.Saldo);
    }

    /// <summary>Repositorio en memoria: cada colección es lo que «hay» en el hogar.</summary>
    private sealed class RepositorioFalso : IRepositorioCuentaComun
    {
        public List<Miembro> Miembros { get; } = [];
        public List<AportacionCuenta> Aportaciones { get; } = [];
        public List<DepositoAhorro> Depositos { get; } = [];
        public List<RetiradaAhorro> Retiradas { get; } = [];
        public List<ReembolsoCuenta> Reembolsos { get; } = [];
        public Dictionary<Guid, decimal> GastadoPor { get; } = [];
        public Dictionary<Guid, decimal> ReembolsadoA { get; } = [];
        public bool Cuenta { get; set; }
        public bool Ahorro { get; set; }
        public SaldoInicialCuenta? Saldo { get; set; }

        public Task<SaldoInicialCuenta?> SaldoInicialAsync(CancellationToken ct) => Task.FromResult(Saldo);

        public Task EstablecerSaldoInicialAsync(SaldoInicialCuenta? saldo, CancellationToken ct)
        {
            Saldo = saldo;
            return Task.CompletedTask;
        }

        public Task<bool> CuentaActivaAsync(CancellationToken ct) => Task.FromResult(Cuenta);

        public Task<bool> AhorroActivoAsync(CancellationToken ct) => Task.FromResult(Ahorro);

        public Task EstablecerCuentaActivaAsync(bool activa, CancellationToken ct)
        {
            Cuenta = activa;
            return Task.CompletedTask;
        }

        public Task EstablecerAhorroActivoAsync(bool activo, CancellationToken ct)
        {
            Ahorro = activo;
            return Task.CompletedTask;
        }

        public Task<HistoricoCuentaDetallado> HistoricoDetalladoAsync(DateOnly inicio, CancellationToken ct)
        {
            var fin = inicio.AddMonths(1);
            var vigentes = Vigentes();
            var historico = new HistoricoCuenta(
                vigentes, [], Reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, inicio, r.Importe)).ToList(),
                Retiradas.Where(r => r.Fecha < fin).Select(r => new RetiradaDeAhorro(inicio, r.Importe)).ToList(),
                Depositos.Where(d => d.Fecha < fin).Select(d => new DepositoDeAhorro(d.Fecha, d.Importe, d.MiembroId)).ToList(),
                Cuenta, 0m, [], Saldo);
            return Task.FromResult(new HistoricoCuentaDetallado(
                historico, Ahorro, Aportaciones.Select(a => new AportacionRegistrada(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro)).ToList(),
                [], [], [], Miembros.ToDictionary(m => m.Id, m => m.Nombre)));
        }

        public Task<bool> ExisteMiembroAsync(Guid id, CancellationToken ct) => Task.FromResult(Miembros.Any(m => m.Id == id));

        public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Miembros.Any(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto));

        private List<AportacionVigente> Vigentes()
            => Aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe, a.Ahorro)).ToList();

        public Task<IReadOnlyList<AportacionVigente>> AportacionesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AportacionVigente>>(Vigentes());

        public Task<IReadOnlyList<DepositoDeAhorro>> DepositosAsync(Guid? excluir, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DepositoDeAhorro>>(
                Depositos.Where(d => d.Id != excluir).Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToList());

        public Task<IReadOnlyList<UsoDeAhorro>> UsosDeAhorroAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UsoDeAhorro>>(Retiradas.Select(r => new UsoDeAhorro(r.Fecha, r.Importe)).ToList());

        public async Task<DatosAhorro> DatosAhorroAsync(CancellationToken ct)
            => new(Vigentes(), await DepositosAsync(null, ct), Retiradas.Sum(r => r.Importe), 0m);

        public Task<decimal> GastadoACargoAsync(Guid miembroId, CancellationToken ct) => Task.FromResult(GastadoPor.GetValueOrDefault(miembroId));

        public Task<decimal> ReembolsadoAsync(Guid miembroId, CancellationToken ct)
            => Task.FromResult(ReembolsadoA.GetValueOrDefault(miembroId) + Reembolsos.Where(r => r.MiembroId == miembroId).Sum(r => r.Importe));

        public Task<AportacionCuenta> FijarAportacionAsync(
            Guid hogarId, Guid miembroId, DateOnly desde, decimal importe, decimal ahorro, CancellationToken ct)
        {
            var a = Aportaciones.FirstOrDefault(x => x.MiembroId == miembroId && x.Desde == desde);
            if (a is null) Aportaciones.Add(a = new AportacionCuenta { Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = miembroId, Desde = desde });
            a.Importe = importe;
            a.Ahorro = ahorro;
            return Task.FromResult(a);
        }

        public Task GuardarReembolsoAsync(ReembolsoCuenta reembolso, CancellationToken ct)
        {
            Reembolsos.Add(reembolso);
            return Task.CompletedTask;
        }

        public Task<bool> EliminarReembolsoAsync(Guid id, CancellationToken ct) => Task.FromResult(Reembolsos.RemoveAll(r => r.Id == id) > 0);

        public Task GuardarDepositoAsync(DepositoAhorro deposito, CancellationToken ct)
        {
            Depositos.Add(deposito);
            return Task.CompletedTask;
        }

        public Task<bool> ExisteDepositoAsync(Guid id, CancellationToken ct) => Task.FromResult(Depositos.Any(d => d.Id == id));

        public Task<bool> EliminarDepositoAsync(Guid id, CancellationToken ct) => Task.FromResult(Depositos.RemoveAll(d => d.Id == id) > 0);

        public Task GuardarRetiradaAsync(RetiradaAhorro retirada, CancellationToken ct)
        {
            Retiradas.Add(retirada);
            return Task.CompletedTask;
        }

        public Task<bool> EliminarRetiradaAsync(Guid id, CancellationToken ct) => Task.FromResult(Retiradas.RemoveAll(r => r.Id == id) > 0);
    }
}
