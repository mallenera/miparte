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

    private static RegistrarMovimiento Mov(Guid miembro, decimal importe, DateOnly? fecha = null, bool desdeAhorro = false)
        => new(Hogar, miembro, importe, fecha ?? Septiembre.AddDays(5), null, desdeAhorro);

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
    public async Task Reembolso_DelAhorro_SeLimitaAlPendienteDelAhorro_SinTocarHogar()
    {
        _repo.GastadoPor[_ana] = 100m;
        _repo.GastadoPorAhorro[_ana] = 50m;

        var delAhorro = await Servicio.CrearReembolsoAsync(Mov(_ana, 50.01m, desdeAhorro: true), default);
        var delAhorroJusto = await Servicio.CrearReembolsoAsync(Mov(_ana, 50m, desdeAhorro: true), default);
        var deHogar = await Servicio.CrearReembolsoAsync(Mov(_ana, 100m), default);

        Assert.Equal(50m, delAhorro.Error!.Extra!["pendiente"]);
        Assert.Null(delAhorroJusto.Error);
        Assert.Null(deHogar.Error);
        Assert.True(delAhorroJusto.Valor!.DesdeAhorro);
        Assert.False(deHogar.Valor!.DesdeAhorro);
    }

    [Fact]
    public async Task Reembolso_DeUnMiembroAjeno_EsInvalido()
    {
        var r = await Servicio.CrearReembolsoAsync(Mov(Guid.NewGuid(), 10m), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task CrearIngreso_SeGuardaSinTocarAhorroNiReembolsos()
    {
        var r = await Servicio.CrearIngresoAsync(new(Hogar, _marcos, 40m, Septiembre.AddDays(5), "Cena amigas"), default);

        Assert.Null(r.Error);
        var ingreso = Assert.Single(_repo.Ingresos);
        Assert.Equal("Cena amigas", ingreso.Concepto);
        Assert.Empty(_repo.Depositos);
        Assert.Empty(_repo.Reembolsos);
        Assert.Empty(_repo.Retiradas);
    }

    [Fact]
    public async Task CrearIngreso_SumaAlSaldoAlEfectivoYAlExtractoDelMes()
    {
        await Servicio.CrearIngresoAsync(new(Hogar, _ana, 40m, Septiembre.AddDays(5), "Cena amigas"), default);

        var e = await Servicio.EstadoAsync(Septiembre, default);
        var anterior = await Servicio.EstadoAsync(Septiembre.AddMonths(-1), default);

        Assert.Equal(40m, e.Estado.Saldo);
        Assert.Equal(40m, e.Estado.Efectivo);
        Assert.Equal(40m, e.Extracto.Ingresos);
        Assert.Equal(0m, e.Extracto.EfectivoInicial);
        Assert.Equal(0m, anterior.Estado.Efectivo);
        Assert.Equal(0m, e.Estado.AhorroDisponible);
    }

    [Fact]
    public async Task CrearIngreso_ConLaCuentaInactiva_EsConflicto()
    {
        _repo.Cuenta = false;

        var r = await Servicio.CrearIngresoAsync(Mov(_ana, 40m), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Empty(_repo.Ingresos);
    }

    [Fact]
    public async Task CrearIngreso_ConImporteInvalido_EsInvalido()
    {
        var r = await Servicio.CrearIngresoAsync(Mov(_ana, 0m), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_repo.Ingresos);
    }

    [Fact]
    public async Task CrearIngreso_DeMiembroAjeno_EsInvalido()
    {
        var r = await Servicio.CrearIngresoAsync(new(Hogar, Guid.NewGuid(), 40m, null, null), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task EliminarIngreso_Inexistente_EsNoEncontrado()
    {
        var r = await Servicio.EliminarIngresoAsync(Guid.NewGuid(), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task EliminarIngreso_LoBorra()
    {
        var ingreso = (await Servicio.CrearIngresoAsync(Mov(_ana, 40m), default)).Valor!;

        var r = await Servicio.EliminarIngresoAsync(ingreso.Id, default);

        Assert.Null(r.Error);
        Assert.Empty(_repo.Ingresos);
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
    [Fact]
    public async Task MovimientosBanco_ConDesdePosteriorAHasta_EsInvalido()
    {
        var r = await Servicio.MovimientosBancoAsync(Septiembre.AddDays(10), Septiembre, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task MovimientosBanco_JuntaAportacionesIngresosYAhorroConSaldoCorrido()
    {
        _repo.Aportaciones.Add(new AportacionCuenta { Id = Guid.NewGuid(), HogarId = Hogar, MiembroId = _ana, Desde = Septiembre, Importe = 500m, Ahorro = 100m });
        _repo.Ingresos.Add(new IngresoCuentaComun { Id = Guid.NewGuid(), HogarId = Hogar, MiembroId = _ana, Fecha = Septiembre.AddDays(3), Importe = 40m, Concepto = "Cena" });
        _repo.Retiradas.Add(new RetiradaAhorro { Id = Guid.NewGuid(), HogarId = Hogar, MiembroId = _ana, Fecha = Septiembre.AddDays(20), Importe = 60m });

        var r = await Servicio.MovimientosBancoAsync(Septiembre, Septiembre.AddDays(29), default);

        var x = r.Valor!;
        Assert.Equal(0m, x.SaldoAnterior);
        Assert.Equal([TipoMovimientoBanco.Aportacion, TipoMovimientoBanco.Ingreso, TipoMovimientoBanco.RetiradaAhorro], x.Movimientos.Select(m => m.Tipo));
        Assert.Equal([500m, 540m, 480m], x.Movimientos.Select(m => m.Saldo));
        Assert.Equal(480m, x.SaldoFinal);
    }

    [Fact]
    public async Task Devolucion_MayorQueLaDeuda_EsConflicto()
    {
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 50m), TipoMovimientoDeuda.Adelanto, default);

        var r = await Servicio.RegistrarDeudaAsync(Mov(_ana, 60m), TipoMovimientoDeuda.Devolucion, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Devolucion_HastaLaDeuda_SeRegistra()
    {
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 50m), TipoMovimientoDeuda.Adelanto, default);

        var r = await Servicio.RegistrarDeudaAsync(Mov(_ana, 50m), TipoMovimientoDeuda.Devolucion, default);

        Assert.Null(r.Error);
        Assert.Equal(TipoMovimientoDeuda.Devolucion, _repo.Deudas.Single(d => d.Tipo == TipoMovimientoDeuda.Devolucion).Tipo);
    }

    [Fact]
    public async Task Compensacion_SinPendienteDeReembolso_EsConflicto()
    {
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 50m), TipoMovimientoDeuda.Adelanto, default);

        var r = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m), TipoMovimientoDeuda.Compensacion, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Compensacion_NoPasaDeLoPendienteYDescuentaLasAnteriores()
    {
        _repo.GastadoPor[_ana] = 30m;
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 20m), TipoMovimientoDeuda.Adelanto, default);

        var primera = await Servicio.RegistrarDeudaAsync(Mov(_ana, 20m), TipoMovimientoDeuda.Compensacion, default);
        var segunda = await Servicio.RegistrarDeudaAsync(Mov(_ana, 15m), TipoMovimientoDeuda.Compensacion, default);

        Assert.Null(primera.Error);
        Assert.Equal(TipoError.Conflicto, segunda.Error!.Tipo);
    }

    [Fact]
    public async Task AdelantoDelAhorro_SinAhorroDisponible_EsConflicto()
    {
        var r = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Deuda_DeUnMiembroAjeno_EsInvalida()
    {
        var r = await Servicio.RegistrarDeudaAsync(new(Hogar, Guid.NewGuid(), 10m, null, null), TipoMovimientoDeuda.Adelanto, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    private void AportarConAhorro(decimal ahorro = 50m)
        => _repo.Aportaciones.Add(new AportacionCuenta { Id = Guid.NewGuid(), HogarId = Hogar, MiembroId = _ana, Desde = Septiembre, Importe = 100m, Ahorro = ahorro });

    [Fact]
    public async Task AdelantoDelAhorro_TieneEnCuentaLoYaAdelantado_YNoDejaElAhorroEnNegativo()
    {
        AportarConAhorro();

        var primero = await Servicio.RegistrarDeudaAsync(Mov(_ana, 30m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default);
        var segundo = await Servicio.RegistrarDeudaAsync(Mov(_ana, 30m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default);

        Assert.Null(primero.Error);
        Assert.Equal(TipoError.Conflicto, segundo.Error!.Tipo);
        Assert.Equal(20m, segundo.Error.Extra!["disponible"]);
    }

    [Fact]
    public async Task UnaDevolucionAlAhorro_ReponeElAhorroParaVolverAAdelantar()
    {
        AportarConAhorro();
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 50m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default);
        Assert.Equal(TipoError.Conflicto, (await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default)).Error!.Tipo);

        await Servicio.RegistrarDeudaAsync(Mov(_ana, 20m, desdeAhorro: true), TipoMovimientoDeuda.Devolucion, default);

        Assert.Null((await Servicio.RegistrarDeudaAsync(Mov(_ana, 20m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default)).Error);
    }

    [Fact]
    public async Task LaDeudaDeHogarYLaDelAhorro_SonIndependientes()
    {
        AportarConAhorro();
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m), TipoMovimientoDeuda.Adelanto, default);

        // Debe 10 a Hogar, pero nada al ahorro: no puede devolver al ahorro.
        var alAhorro = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m, desdeAhorro: true), TipoMovimientoDeuda.Devolucion, default);
        var aHogar = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m), TipoMovimientoDeuda.Devolucion, default);

        Assert.Equal(TipoError.Conflicto, alAhorro.Error!.Tipo);
        Assert.Null(aHogar.Error);
    }

    [Fact]
    public async Task CompensacionDelAhorro_UsaLoPendienteDelAhorro_NoLoDeHogar()
    {
        AportarConAhorro();
        _repo.GastadoPor[_ana] = 30m; // la cuenta le debe 30 de Hogar
        await Servicio.RegistrarDeudaAsync(Mov(_ana, 20m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default);

        var sinPendienteDelAhorro = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m, desdeAhorro: true), TipoMovimientoDeuda.Compensacion, default);
        _repo.GastadoPorAhorro[_ana] = 30m;
        var conPendienteDelAhorro = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m, desdeAhorro: true), TipoMovimientoDeuda.Compensacion, default);

        Assert.Equal(TipoError.Conflicto, sinPendienteDelAhorro.Error!.Tipo);
        Assert.Null(conPendienteDelAhorro.Error);
    }

    [Fact]
    public async Task ElAhorroInactivo_BloqueaLasDeudasDelAhorro_PeroNoLasDeHogar()
    {
        _repo.Ahorro = false;

        var delAhorro = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m, desdeAhorro: true), TipoMovimientoDeuda.Adelanto, default);
        var deHogar = await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m), TipoMovimientoDeuda.Adelanto, default);

        Assert.Equal(TipoError.Conflicto, delAhorro.Error!.Tipo);
        Assert.Null(deHogar.Error);
    }

    [Fact]
    public async Task ConLaCuentaInactiva_NoSeRegistraNingunTipoDeMovimiento()
    {
        _repo.Cuenta = false;

        foreach (var tipo in Enum.GetValues<TipoMovimientoDeuda>())
            Assert.Equal(TipoError.Conflicto, (await Servicio.RegistrarDeudaAsync(Mov(_ana, 10m), tipo, default)).Error!.Tipo);
        Assert.Empty(_repo.Deudas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UnImporteNoPositivo_EsInvalido(int importe)
    {
        var r = await Servicio.RegistrarDeudaAsync(Mov(_ana, importe), TipoMovimientoDeuda.Adelanto, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_repo.Deudas);
    }

    [Fact]
    public async Task UnConceptoDeMasDeDoscientosCaracteres_EsInvalido_YUnoNormalSeRecorta()
    {
        var largo = await Servicio.RegistrarDeudaAsync(new(Hogar, _ana, 10m, Septiembre, new string('a', 201)), TipoMovimientoDeuda.Adelanto, default);
        var normal = await Servicio.RegistrarDeudaAsync(new(Hogar, _ana, 10m, Septiembre, "  Fontanero  "), TipoMovimientoDeuda.Adelanto, default);

        Assert.Equal(TipoError.Invalido, largo.Error!.Tipo);
        Assert.Equal("Fontanero", normal.Valor!.Concepto);
    }

    [Fact]
    public async Task SinFecha_ElMovimientoSeFechaHoy_YGuardaTipoFondoYPersona()
    {
        var reloj = new RelojDeDeuda(new DateTimeOffset(2026, 10, 28, 23, 30, 0, TimeSpan.Zero));
        var servicio = new ServicioCuentaComun(_repo, reloj);

        var r = await servicio.RegistrarDeudaAsync(new(Hogar, _ana, 10m, null, null), TipoMovimientoDeuda.Adelanto, default);

        var d = Assert.Single(_repo.Deudas);
        Assert.Equal(new DateOnly(2026, 10, 28), d.Fecha);
        Assert.Equal((Hogar, _ana, TipoMovimientoDeuda.Adelanto, false), (d.HogarId, d.MiembroId, d.Tipo, d.DesdeAhorro));
        Assert.Equal(d.Id, r.Valor!.Id);
    }

    private async Task<Guid> Registrar(decimal importe, TipoMovimientoDeuda tipo, bool desdeAhorro = false, Guid? miembro = null)
    {
        var r = await Servicio.RegistrarDeudaAsync(Mov(miembro ?? _ana, importe, desdeAhorro: desdeAhorro), tipo, default);
        Assert.Null(r.Error);
        return r.Valor!.Id;
    }

    [Fact]
    public async Task BorrarUnaDeudaQueNoExiste_NoLaEncuentra()
    {
        var r = await Servicio.EliminarDeudaAsync(Guid.NewGuid(), default);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }

    [Fact]
    public async Task BorrarUnAdelantoSinNadaAplicado_LoQuitaYLaDeudaDesaparece()
    {
        var adelanto = await Registrar(40m, TipoMovimientoDeuda.Adelanto);

        var r = await Servicio.EliminarDeudaAsync(adelanto, default);

        Assert.Null(r.Error);
        Assert.Empty(_repo.Deudas);
        Assert.Empty(MiParte.Core.Domain.CuentaComun.Deudas(await _repo.DeudasAsync(default)));
    }

    [Fact]
    public async Task UnAdelantoAlQueYaSeAplicoUnaDevolucion_NoSePuedeBorrar_PeroUnaDevolucionSi()
    {
        var adelanto = await Registrar(50m, TipoMovimientoDeuda.Adelanto);
        var devolucion = await Registrar(20m, TipoMovimientoDeuda.Devolucion);

        var sinSitio = await Servicio.EliminarDeudaAsync(adelanto, default);
        Assert.Equal(TipoError.Conflicto, sinSitio.Error!.Tipo);
        Assert.Equal(20m, sinSitio.Error.Extra!["falta"]);
        Assert.Equal(2, _repo.Deudas.Count);

        // Quitar la devolución vuelve a dejar la deuda entera, y entonces sí se puede borrar el adelanto.
        Assert.Null((await Servicio.EliminarDeudaAsync(devolucion, default)).Error);
        Assert.Null((await Servicio.EliminarDeudaAsync(adelanto, default)).Error);
        Assert.Empty(_repo.Deudas);
    }

    [Fact]
    public async Task ConDosAdelantos_SePuedeBorrarUnoMientrasLaDeudaRestanteCubraLoDevuelto()
    {
        var primero = await Registrar(30m, TipoMovimientoDeuda.Adelanto);
        var segundo = await Registrar(50m, TipoMovimientoDeuda.Adelanto);
        await Registrar(25m, TipoMovimientoDeuda.Devolucion);

        // Debe 55: sin el primer adelanto quedarían 25, justo lo devuelto; sin el segundo, -5.
        Assert.Null((await Servicio.EliminarDeudaAsync(primero, default)).Error);
        var r = await Servicio.EliminarDeudaAsync(segundo, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task UnaCompensacionSePuedeBorrar_YLaDeudaVuelve()
    {
        _repo.GastadoPor[_ana] = 30m;
        await Registrar(20m, TipoMovimientoDeuda.Adelanto);
        var compensacion = await Registrar(20m, TipoMovimientoDeuda.Compensacion);
        Assert.Empty(MiParte.Core.Domain.CuentaComun.Deudas(await _repo.DeudasAsync(default)));

        var r = await Servicio.EliminarDeudaAsync(compensacion, default);

        Assert.Null(r.Error);
        Assert.Equal(20m, Assert.Single(MiParte.Core.Domain.CuentaComun.Deudas(await _repo.DeudasAsync(default))).Importe);
    }

    [Fact]
    public async Task LasDeudasDeHogarYDelAhorro_SeBorranPorSeparado()
    {
        AportarConAhorro();
        var deHogar = await Registrar(10m, TipoMovimientoDeuda.Adelanto);
        await Registrar(10m, TipoMovimientoDeuda.Devolucion);
        var delAhorro = await Registrar(10m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true);

        // La devolución de Hogar no cubre el adelanto del ahorro, pero sí le quita el sitio al de Hogar.
        Assert.Equal(TipoError.Conflicto, (await Servicio.EliminarDeudaAsync(deHogar, default)).Error!.Tipo);
        Assert.Null((await Servicio.EliminarDeudaAsync(delAhorro, default)).Error);
    }

    // ----- El ahorro cuenta las deudas -----

    [Fact]
    public async Task UnaDevolucionAlAhorroYaUsada_NoSePuedeBorrar()
    {
        AportarConAhorro();
        await Registrar(50m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true);
        var devolucion = await Registrar(50m, TipoMovimientoDeuda.Devolucion, desdeAhorro: true);
        _repo.Retiradas.Add(new RetiradaAhorro { Id = Guid.NewGuid(), HogarId = Hogar, MiembroId = _ana, Fecha = Septiembre.AddDays(7), Importe = 50m });

        var r = await Servicio.EliminarDeudaAsync(devolucion, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(50m, r.Error.Extra!["falta"]);
        Assert.Equal(2, _repo.Deudas.Count);
    }

    [Fact]
    public async Task UnaDevolucionAlAhorroSinUsar_SePuedeBorrar()
    {
        AportarConAhorro();
        await Registrar(50m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true);
        var devolucion = await Registrar(20m, TipoMovimientoDeuda.Devolucion, desdeAhorro: true);

        // El ahorro quedaba en 20 (50 − 50 + 20): quitar la devolución lo deja en 0, que cubre todo lo usado.
        Assert.Null((await Servicio.EliminarDeudaAsync(devolucion, default)).Error);
    }

    [Fact]
    public async Task RebajarElAhorroDeUnaAportacion_TieneEnCuentaLoAdelantadoDesdeElAhorro()
    {
        AportarConAhorro(50m);
        await Registrar(50m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true); // el ahorro disponible queda en 0

        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 20m), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(30m, r.Error.Extra!["falta"]);
    }

    [Fact]
    public async Task UnaDevolucionDelAhorro_DaSitioParaRebajarElAhorroDeUnaAportacion()
    {
        AportarConAhorro(50m);
        await Registrar(50m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true);
        await Registrar(30m, TipoMovimientoDeuda.Devolucion, desdeAhorro: true);

        // Usado neto: 20; lo ahorrado tras rebajar a 20 cubre justo.
        var r = await Servicio.FijarAportacionAsync(new(Hogar, _ana, Septiembre, 100m, 20m), default);

        Assert.Null(r.Error);
    }

    [Fact]
    public async Task BorrarUnIngresoDeAhorro_TieneEnCuentaLasDeudasDelAhorro()
    {
        AportarConAhorro(0m);
        var deposito = await Servicio.CrearDepositoAsync(Mov(_ana, 100m), default);
        await Registrar(80m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true);

        var r = await Servicio.EliminarDepositoAsync(deposito.Valor!.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(80m, r.Error.Extra!["falta"]);
    }

    [Fact]
    public async Task CambiarLaFrecuencia_TieneEnCuentaLasDeudasDelAhorro()
    {
        AportarConAhorro(50m);
        await Registrar(50m, TipoMovimientoDeuda.Adelanto, desdeAhorro: true);

        // Con frecuencia única solo cobra la primera vez: el mismo ahorro de septiembre, así que sigue cubriendo.
        var r = await Servicio.CambiarFrecuenciaAsync(FrecuenciaAportacion.Unica, default);

        Assert.Null(r.Error);
    }

    private sealed class RelojDeDeuda(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class RepositorioFalso : IRepositorioCuentaComun
    {
        public List<Miembro> Miembros { get; } = [];
        public List<AportacionCuenta> Aportaciones { get; } = [];
        public List<DepositoAhorro> Depositos { get; } = [];
        public List<RetiradaAhorro> Retiradas { get; } = [];
        public List<ReembolsoCuenta> Reembolsos { get; } = [];
        public Dictionary<Guid, decimal> GastadoPor { get; } = [];
        public Dictionary<Guid, decimal> GastadoPorAhorro { get; } = [];
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
                vigentes, [], Reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, inicio, r.Importe, r.DesdeAhorro)).ToList(),
                Retiradas.Where(r => r.Fecha < fin).Select(r => new RetiradaDeAhorro(inicio, r.Importe)).ToList(),
                Depositos.Where(d => d.Fecha < fin).Select(d => new DepositoDeAhorro(d.Fecha, d.Importe, d.MiembroId)).ToList(),
                Cuenta, 0m, [], Saldo, Frecuencia,
                Ingresos.Where(i => i.Fecha < fin).Select(i => new IngresoDeCuenta(i.Fecha, i.Importe)).ToList());
            return Task.FromResult(new HistoricoCuentaDetallado(
                historico, Ahorro, Aportaciones.Select(a => new AportacionRegistrada(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro)).ToList(),
                [], [], [], Miembros.ToDictionary(m => m.Id, m => m.Nombre)));
        }

        public Task<DatosMovimientosBanco> MovimientosBancoAsync(DateOnly hasta, CancellationToken ct)
            => Task.FromResult(new DatosMovimientosBanco(
                Vigentes(), Saldo, [],
                Reembolsos.Where(r => r.Fecha <= hasta).Select(r => new MovimientoCuenta(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto, r.DesdeAhorro)).ToList(),
                Ingresos.Where(i => i.Fecha <= hasta).Select(i => new MovimientoCuenta(i.Id, i.MiembroId, i.Fecha, i.Importe, i.Concepto)).ToList(),
                Depositos.Where(d => d.Fecha <= hasta).Select(d => new MovimientoCuenta(d.Id, d.MiembroId, d.Fecha, d.Importe, d.Concepto)).ToList(),
                Retiradas.Where(r => r.Fecha <= hasta).Select(r => new MovimientoCuenta(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList()));

        public Task<bool> ExisteMiembroAsync(Guid id, CancellationToken ct) => Task.FromResult(Miembros.Any(m => m.Id == id));

        public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Miembros.Any(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto));

        public FrecuenciaAportacion Frecuencia { get; set; } = FrecuenciaAportacion.Mensual;

        private List<AportacionVigente> Vigentes()
            => Aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe, a.Ahorro, Frecuencia)).ToList();

        public Task<IReadOnlyList<AportacionVigente>> AportacionesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AportacionVigente>>(Vigentes());

        public Task<FrecuenciaAportacion> FrecuenciaAsync(CancellationToken ct) => Task.FromResult(Frecuencia);

        public Task EstablecerFrecuenciaAsync(FrecuenciaAportacion frecuencia, CancellationToken ct)
        {
            Frecuencia = frecuencia;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DepositoDeAhorro>> DepositosAsync(Guid? excluir, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DepositoDeAhorro>>(
                Depositos.Where(d => d.Id != excluir).Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToList());

        public Task<IReadOnlyList<UsoDeAhorro>> UsosDeAhorroAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UsoDeAhorro>>(
                Retiradas.Select(r => new UsoDeAhorro(r.Fecha, r.Importe)).Concat(AhorroCuenta.UsosPorDeudas(DeudasDeDominio())).ToList());

        public async Task<DatosAhorro> DatosAhorroAsync(CancellationToken ct)
            => new(Vigentes(), await DepositosAsync(null, ct), Retiradas.Sum(r => r.Importe),
                AhorroCuenta.UsosPorDeudas(DeudasDeDominio()).Sum(u => u.Importe));

        // Los fakes solo modelan el fondo de Hogar: lo del ahorro se prueba en los tests de dominio y de la BD.
        public Task<decimal> GastadoACargoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct)
            => Task.FromResult((desdeAhorro ? GastadoPorAhorro : GastadoPor).GetValueOrDefault(miembroId));

        public Task<decimal> ReembolsadoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct)
            => Task.FromResult(
                (desdeAhorro ? 0m : ReembolsadoA.GetValueOrDefault(miembroId))
                + Reembolsos.Where(r => r.MiembroId == miembroId && r.DesdeAhorro == desdeAhorro).Sum(r => r.Importe));

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

        public List<MovimientoDeudaCuenta> Deudas { get; } = [];

        private IEnumerable<MovimientoDeuda> DeudasDeDominio()
            => Deudas.Select(d => new MovimientoDeuda(d.MiembroId, d.Fecha, d.Importe, d.Tipo, d.DesdeAhorro, d.Id, d.Concepto));

        public Task<IReadOnlyList<MovimientoDeuda>> DeudasAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MovimientoDeuda>>(DeudasDeDominio().ToList());

        public Task<MovimientoDeuda?> DeudaAsync(Guid id, CancellationToken ct)
            => Task.FromResult(DeudasDeDominio().FirstOrDefault(d => d.Id == id));

        public Task<bool> EliminarDeudaAsync(Guid id, CancellationToken ct) => Task.FromResult(Deudas.RemoveAll(d => d.Id == id) > 0);

        public Task GuardarDeudaAsync(MovimientoDeudaCuenta deuda, CancellationToken ct)
        {
            Deudas.Add(deuda);
            return Task.CompletedTask;
        }

        public Task<bool> EliminarReembolsoAsync(Guid id, CancellationToken ct) => Task.FromResult(Reembolsos.RemoveAll(r => r.Id == id) > 0);

        public List<IngresoCuentaComun> Ingresos { get; } = [];

        public Task GuardarIngresoAsync(IngresoCuentaComun ingreso, CancellationToken ct)
        {
            Ingresos.Add(ingreso);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IngresoCuentaComun>> IngresosAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IngresoCuentaComun>>(Ingresos.OrderByDescending(i => i.Fecha).ThenBy(i => i.Id).ToList());

        public Task<bool> EliminarIngresoAsync(Guid id, CancellationToken ct) => Task.FromResult(Ingresos.RemoveAll(i => i.Id == id) > 0);

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
