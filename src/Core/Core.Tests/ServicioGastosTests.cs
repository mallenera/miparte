using MiParte.Core.Application;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Casos de uso de gastos probados sin HTTP ni base de datos, con un repositorio en memoria.</summary>
public class ServicioGastosTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 9, 10);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid(), _categoria = Guid.NewGuid();
    private readonly PerfilReparto _partes = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Partes", Modo = ModoReparto.Partes };
    private readonly PerfilReparto _cuenta = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Cuenta", Modo = ModoReparto.CuentaComun };
    private readonly RepositorioGastosFalso _repo = new();

    public ServicioGastosTests()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos" });
        _repo.Categorias.Add(_categoria);
        _partes.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = Hogar, PerfilId = _partes.Id, MiembroId = _ana, Valor = 2m });
        _partes.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = Hogar, PerfilId = _partes.Id, MiembroId = _marcos, Valor = 1m });
        _repo.Perfiles.AddRange([_partes, _cuenta]);
    }

    private ServicioGastos Servicio => new(_repo);

    private DatosGasto Datos(
        decimal importe = 900m, Guid? pagadoPor = null, PerfilReparto? perfil = null, bool ahorro = false, bool personal = false,
        DateOnly? fecha = null, bool cuentaPaga = false) => new(
        fecha ?? Hoy, importe, _categoria, cuentaPaga || ahorro ? null : pagadoPor ?? _ana, (perfil ?? _partes).Id, null, ahorro, personal);

    [Fact]
    public async Task Crear_ConPartes2a1_RepartePorPesosYElUltimoAbsorbeElCentimo()
    {
        var r = await Servicio.CrearAsync(Hogar, Datos(900m), default);

        Assert.Null(r.Error);
        var partes = r.Valor!.Repartos.ToDictionary(x => x.MiembroId, x => x.ImporteAsumido);
        Assert.Equal(600m, partes[_ana]);
        Assert.Equal(300m, partes[_marcos]);
        Assert.Single(_repo.Gastos);
    }

    [Fact]
    public async Task Crear_Personal_LoAsumeIntegroQuienPaga()
    {
        var r = await Servicio.CrearAsync(Hogar, Datos(50m, personal: true), default);

        var parte = Assert.Single(r.Valor!.Repartos);
        Assert.Equal((_ana, 50m), (parte.MiembroId, parte.ImporteAsumido));
        Assert.True(r.Valor.EsPersonal);
        Assert.False(r.Valor.ACargoCuentaComun);
    }

    [Fact]
    public async Task AdultoSinCuenta_PagaYFiguraEnElReparto()
    {
        // Ana no tiene usuario vinculado: un adulto sin cuenta paga y reparte como cualquier otro adulto del hogar.
        Assert.Null(_repo.Miembros.Single(m => m.Id == _ana).UserId);

        var r = await Servicio.CrearAsync(Hogar, Datos(900m, pagadoPor: _ana), default);

        Assert.Null(r.Error);
        Assert.Equal(_ana, r.Valor!.PagadoPor);
        var partes = r.Valor.Repartos.ToDictionary(x => x.MiembroId, x => x.ImporteAsumido);
        Assert.Equal(600m, partes[_ana]);
        Assert.Equal(300m, partes[_marcos]);
    }

    [Fact]
    public async Task Crear_ConPerfilDeCuentaComun_NoRepartePeroExigeLaCuentaActiva()
    {
        var sinCuenta = await Servicio.CrearAsync(Hogar, Datos(perfil: _cuenta, cuentaPaga: true), default);
        Assert.Equal(TipoError.Conflicto, sinCuenta.Error!.Tipo);
        Assert.Equal(Mensajes.CuentaInactiva, sinCuenta.Error.Mensaje);

        _repo.CuentaActiva = true;
        var r = await Servicio.CrearAsync(Hogar, Datos(perfil: _cuenta, cuentaPaga: true), default);

        Assert.Null(r.Error);
        Assert.True(r.Valor!.ACargoCuentaComun);
        Assert.Empty(r.Valor.Repartos);
    }

    [Fact]
    public async Task Crear_EnMesCerrado_EsConflictoYNoGuarda()
    {
        _repo.MesesCerrados.Add(new DateOnly(2026, 9, 1));

        var r = await Servicio.CrearAsync(Hogar, Datos(), default);

        Assert.Equal(Mensajes.MesCerrado, r.Error!.Mensaje);
        Assert.Empty(_repo.Gastos);
    }

    [Fact]
    public async Task Crear_SiLaBaseDeDatosRechazaPorMesCerrado_EsConflicto()
    {
        _repo.RechazarAlGuardar = true;

        var r = await Servicio.CrearAsync(Hogar, Datos(), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(Mensajes.MesCerrado, r.Error.Mensaje);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10.005)]
    public async Task Crear_ConImporteInvalido_EsInvalido(double importe)
    {
        var r = await Servicio.CrearAsync(Hogar, Datos((decimal)importe), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_PagadoPorQuienNoEsAdultoActivo_EsInvalido()
    {
        var r = await Servicio.CrearAsync(Hogar, Datos(pagadoPor: Guid.NewGuid()), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_DesdeElAhorro_NoAlcanzaDevuelveLoDisponible()
    {
        _repo.CuentaActiva = true;
        _repo.AhorroOn = true;
        _repo.Ahorro = new DatosAhorro([new AportacionVigente(_ana, new DateOnly(2026, 9, 1), 100m, 100m)], [], 0m, 0m);

        var r = await Servicio.CrearAsync(Hogar, Datos(150m, perfil: _cuenta, ahorro: true), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(100m, r.Error.Extra!["disponible"]);
    }

    [Fact]
    public async Task Crear_DesdeElAhorro_QueAlcanzaSeGuarda()
    {
        _repo.CuentaActiva = true;
        _repo.AhorroOn = true;
        _repo.Ahorro = new DatosAhorro([new AportacionVigente(_ana, new DateOnly(2026, 9, 1), 100m, 100m)], [], 0m, 0m);

        var r = await Servicio.CrearAsync(Hogar, Datos(100m, perfil: _cuenta, ahorro: true), default);

        Assert.Null(r.Error);
        Assert.True(r.Valor!.PagadoDesdeAhorro);
    }

    [Fact]
    public async Task Editar_RecalculaSoloEsteGastoYCompruebaAmbosMeses()
    {
        var creado = (await Servicio.CrearAsync(Hogar, Datos(900m), default)).Valor!;
        _repo.MesesCerrados.Add(new DateOnly(2026, 10, 1));

        var aOctubre = await Servicio.EditarAsync(creado.Id, Datos(900m, fecha: new DateOnly(2026, 10, 2)), default);
        Assert.Equal(Mensajes.MesCerrado, aOctubre.Error!.Mensaje);

        var cambiaImporte = await Servicio.EditarAsync(creado.Id, Datos(300m), default);
        Assert.Null(cambiaImporte.Error);
        Assert.Equal(200m, cambiaImporte.Valor!.Repartos.Single(x => x.MiembroId == _ana).ImporteAsumido);
    }

    [Fact]
    public async Task Editar_Inexistente_EsNoEncontrado()
    {
        var r = await Servicio.EditarAsync(Guid.NewGuid(), Datos(), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task Borrar_EnMesCerrado_EsConflictoYConservaElGasto()
    {
        var creado = (await Servicio.CrearAsync(Hogar, Datos(), default)).Valor!;
        _repo.MesesCerrados.Add(new DateOnly(2026, 9, 1));

        var r = await Servicio.BorrarAsync(creado.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Single(_repo.Gastos);
    }

    [Fact]
    public async Task Borrar_EnMesAbierto_QuitaElGasto()
    {
        var creado = (await Servicio.CrearAsync(Hogar, Datos(), default)).Valor!;

        var r = await Servicio.BorrarAsync(creado.Id, default);

        Assert.Null(r.Error);
        Assert.Empty(_repo.Gastos);
    }

    [Fact]
    public async Task Editar_GastoYaLiquidadoPorUnCiclo_EsConflictoYNoCambiaNada()
    {
        var creado = (await Servicio.CrearAsync(Hogar, Datos(900m), default)).Valor!;
        creado.CicloLiquidacionId = Guid.NewGuid();
        var repartoAntes = creado.Repartos.ToDictionary(x => x.MiembroId, x => x.ImporteAsumido);

        var r = await Servicio.EditarAsync(creado.Id, Datos(300m), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(Mensajes.GastoEnCiclo, r.Error.Mensaje);
        Assert.Equal(900m, creado.Importe);
        Assert.Equal(repartoAntes, creado.Repartos.ToDictionary(x => x.MiembroId, x => x.ImporteAsumido));
    }

    [Fact]
    public async Task Borrar_GastoYaLiquidadoPorUnCiclo_EsConflictoYConservaElGasto()
    {
        var creado = (await Servicio.CrearAsync(Hogar, Datos(), default)).Valor!;
        creado.CicloLiquidacionId = Guid.NewGuid();

        var r = await Servicio.BorrarAsync(creado.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(Mensajes.GastoEnCiclo, r.Error.Mensaje);
        Assert.Single(_repo.Gastos);
    }
}
