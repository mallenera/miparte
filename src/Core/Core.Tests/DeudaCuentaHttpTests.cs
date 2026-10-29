using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Cableado HTTP de la deuda de una persona con la cuenta común: permisos, validaciones, efecto en el estado y en el extracto
/// bancario. Sin PostgreSQL: la API arranca sobre la base en memoria de las demás pruebas de permisos.
/// </summary>
public class DeudaCuentaHttpTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Octubre = new(2026, 10, 5);

    private sealed record Escenario(HttpClient Admin, HttpClient Miembro, Guid Ana, Guid Beto);

    /// <summary>
    /// Hogar con cuenta común y ahorro, Ana (admin) aportando 300 al mes desde septiembre, de ellos 50 al ahorro si <paramref name="ahorro"/>,
    /// y Beto (miembro) con exactamente los permisos indicados. Ana adelantó un gasto de 30 a cargo de la cuenta (fondo de Hogar).
    /// </summary>
    private static async Task<Escenario> Montar(bool cuentaActiva = true, decimal ahorro = 0m, params string[] permisosDeBeto)
    {
        var f = Crear(Secreto);
        var (userAna, userBeto) = (Guid.NewGuid(), Guid.NewGuid());
        var (hogar, ana, beto, cat, perfil) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa", CuentaComunActiva = cuentaActiva, AhorroActivo = true });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = userAna, Rol = RolMiembro.Admin });
            db.Miembros.Add(new Miembro
            {
                Id = beto, HogarId = hogar, Nombre = "Beto", Tipo = TipoMiembro.Adulto, UserId = userBeto, Rol = RolMiembro.Miembro,
                Permisos = [.. permisosDeBeto],
            });
            db.Categorias.Add(new Categoria { Id = cat, HogarId = hogar, Nombre = "Comunidad", ACargoCuentaComun = true });
            db.PerfilesReparto.Add(new PerfilReparto { Id = perfil, HogarId = hogar, Nombre = "Cuenta común", Modo = ModoReparto.CuentaComun });
            db.AportacionesCuenta.Add(new AportacionCuenta
            {
                Id = Guid.NewGuid(), HogarId = hogar, MiembroId = ana, Desde = new DateOnly(2026, 9, 1), Importe = 300m, Ahorro = ahorro,
            });
            db.Gastos.Add(new Gasto
            {
                Id = Guid.NewGuid(), HogarId = hogar, Fecha = Octubre, Importe = 30m, CategoriaId = cat, PagadoPor = ana,
                PerfilRepartoId = perfil, Concepto = "Adelanto de Ana", ACargoCuentaComun = true,
            });
            await db.SaveChangesAsync();
        }
        return new Escenario(Cliente(f, Token(Hs256(), userAna)), Cliente(f, Token(Hs256(), userBeto)), ana, beto);
    }

    private static async Task<T> Leer<T>(HttpResponseMessage r, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        Assert.Equal(esperado, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<T>(Web))!;
    }

    private static Task<HttpResponseMessage> Registrar(
        HttpClient c, Guid miembro, string tipo, decimal importe, bool desdeAhorro = false, DateOnly? fecha = null)
        => c.PostAsJsonAsync("/api/cuenta-comun/deudas", new CrearDeudaCuentaRequest(miembro, tipo, importe, fecha ?? Octubre, null, desdeAhorro));

    private static async Task<CuentaComunResponse> Estado(HttpClient c)
        => await Leer<CuentaComunResponse>(await c.GetAsync("/api/cuenta-comun?mes=2026-10"));

    [Fact]
    public async Task SinElPermisoCuentaMovimientos_Responde403_YConElPermiso201()
    {
        var sin = await Montar(permisosDeBeto: []);
        var con = await Montar(permisosDeBeto: [CatalogoPermisos.CuentaMovimientos]);

        Assert.Equal(HttpStatusCode.Forbidden, (await Registrar(sin.Miembro, sin.Beto, "adelanto", 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Registrar(con.Miembro, con.Beto, "adelanto", 10m)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("prestamo")]
    [InlineData("ADELANTO")]
    public async Task TipoDesconocido_Es400(string tipo)
    {
        var e = await Montar();

        Assert.Equal(HttpStatusCode.BadRequest, (await Registrar(e.Admin, e.Ana, tipo, 10m)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ImporteNoPositivo_Es400(int importe)
    {
        var e = await Montar();

        Assert.Equal(HttpStatusCode.BadRequest, (await Registrar(e.Admin, e.Ana, "adelanto", importe)).StatusCode);
    }

    [Fact]
    public async Task MiembroAjeno_Es400_YCuentaInactiva_Es409()
    {
        var e = await Montar();
        var inactiva = await Montar(cuentaActiva: false);

        Assert.Equal(HttpStatusCode.BadRequest, (await Registrar(e.Admin, Guid.NewGuid(), "adelanto", 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Registrar(inactiva.Admin, inactiva.Ana, "adelanto", 10m)).StatusCode);
    }

    [Fact]
    public async Task Adelanto_BajaElEfectivo_YLaPersonaFiguraComoDeudora()
    {
        var e = await Montar();
        var antes = (await Estado(e.Admin)).Efectivo;

        var creado = await Leer<MovimientoDeudaCuentaDto>(await Registrar(e.Admin, e.Ana, "adelanto", 40m), HttpStatusCode.Created);
        var despues = await Estado(e.Admin);

        Assert.Equal("adelanto", creado.Tipo);
        Assert.Equal(antes - 40m, despues.Efectivo);
        var deuda = Assert.Single(despues.Deudas!);
        // Ana le debía 30 por su gasto: el adelanto de 40 se descuenta de eso y ella le debe 10 a la cuenta.
        Assert.Equal((e.Ana, 10m, false), (deuda.MiembroId, deuda.Importe, deuda.DesdeAhorro));
        Assert.Equal("Ana", deuda.Nombre);
    }

    [Fact]
    public async Task Devolucion_NoPuedeSuperarLaDeuda_YLaSaldaCuandoEsExacta()
    {
        // Beto no tiene gastos adelantados: lo que debe es lo que la cuenta le ha adelantado.
        var e = await Montar();
        await Registrar(e.Admin, e.Beto, "adelanto", 40m);

        var exceso = await Registrar(e.Admin, e.Beto, "devolucion", 100m);
        var cuerpo = await Leer<JsonElement>(exceso, HttpStatusCode.Conflict);
        Assert.Equal(40m, cuerpo.GetProperty("debe").GetDecimal());

        var antes = (await Estado(e.Admin)).Efectivo;
        await Leer<MovimientoDeudaCuentaDto>(await Registrar(e.Admin, e.Beto, "devolucion", 40m), HttpStatusCode.Created);
        var despues = await Estado(e.Admin);

        Assert.Empty(despues.Deudas!);
        Assert.Equal(antes + 40m, despues.Efectivo);
    }

    [Fact]
    public async Task Compensacion_SinDeudaOSinPendiente_Es409()
    {
        var e = await Montar();

        // Sin deuda no hay nada que compensar.
        Assert.Equal(HttpStatusCode.Conflict, (await Registrar(e.Admin, e.Ana, "compensacion", 5m)).StatusCode);

        // Beto debe 20 pero la cuenta no le debe nada (no adelantó gastos): tampoco hay con qué compensar.
        await Registrar(e.Admin, e.Beto, "adelanto", 20m);
        var r = await Registrar(e.Admin, e.Beto, "compensacion", 10m);
        var cuerpo = await Leer<JsonElement>(r, HttpStatusCode.Conflict);
        Assert.Equal(0m, cuerpo.GetProperty("pendiente").GetDecimal());
    }

    [Fact]
    public async Task Compensacion_RestaDeLoPendienteSinMoverElEfectivo()
    {
        var e = await Montar();
        await Registrar(e.Admin, e.Ana, "adelanto", 20m);
        var antes = await Estado(e.Admin);
        // La cuenta le debía 30 y le ha adelantado 20: le quedan 10 a su favor.
        Assert.Equal(10m, antes.Pendientes.Single().Importe);

        await Leer<MovimientoDeudaCuentaDto>(await Registrar(e.Admin, e.Ana, "compensacion", 20m), HttpStatusCode.Created);
        var despues = await Estado(e.Admin);

        Assert.Equal(antes.Efectivo, despues.Efectivo);
        Assert.Equal(10m, despues.Pendientes.Single().Importe);
        Assert.Empty(despues.Deudas!);
        // No puede compensar más de lo que queda pendiente.
        await Registrar(e.Admin, e.Ana, "adelanto", 50m);
        Assert.Equal(HttpStatusCode.Conflict, (await Registrar(e.Admin, e.Ana, "compensacion", 11m)).StatusCode);
    }

    [Fact]
    public async Task DelAhorro_AdelantoDevolucionYCompensacion_MuevenElAhorro()
    {
        var e = await Montar(ahorro: 50m);
        var inicial = (await Estado(e.Admin)).AhorroDisponible;
        Assert.Equal(100m, inicial); // dos meses de 50

        // Más de lo que hay en el ahorro no se puede adelantar.
        Assert.Equal(HttpStatusCode.Conflict, (await Registrar(e.Admin, e.Ana, "adelanto", 101m, desdeAhorro: true)).StatusCode);

        await Leer<MovimientoDeudaCuentaDto>(await Registrar(e.Admin, e.Ana, "adelanto", 60m, desdeAhorro: true), HttpStatusCode.Created);
        var tras = await Estado(e.Admin);
        Assert.Equal(40m, tras.AhorroDisponible);
        Assert.Equal(60m, tras.Deudas!.Single(d => d.DesdeAhorro).Importe);

        await Registrar(e.Admin, e.Ana, "devolucion", 25m, desdeAhorro: true);
        Assert.Equal(65m, (await Estado(e.Admin)).AhorroDisponible);
        // La deuda de Hogar y la del ahorro son independientes: no se puede devolver al fondo de Hogar lo que se debe al ahorro.
        Assert.Equal(HttpStatusCode.Conflict, (await Registrar(e.Admin, e.Ana, "devolucion", 5m)).StatusCode);
    }

    [Fact]
    public async Task ExtractoBancario_MuestraLosMovimientos_YCuadraConElEfectivo()
    {
        var e = await Montar();
        // Ana adelantó 30 y la cuenta le ha adelantado 40: ella debe 10 neto, así que la devolución es de 10.
        await Registrar(e.Admin, e.Ana, "adelanto", 40m);
        await Registrar(e.Admin, e.Ana, "devolucion", 10m);
        await Registrar(e.Admin, e.Ana, "compensacion", 10m);

        var banco = await Leer<MovimientosCuentaComunResponse>(
            await e.Admin.GetAsync("/api/cuenta-comun/movimientos?desde=2026-10-01&hasta=2026-10-31"));
        var estado = await Estado(e.Admin);

        Assert.Contains(banco.Movimientos, m => m.Tipo == "adelanto_persona" && m.Importe == -40m);
        Assert.Contains(banco.Movimientos, m => m.Tipo == "devolucion_deuda" && m.Importe == 10m);
        Assert.Contains(banco.Movimientos, m => m.Tipo == "compensacion_deuda" && m.Importe == 0m);
        Assert.Equal(estado.Efectivo, banco.EfectivoFinal);
    }

    // ----- Borrar movimientos de deuda y verlos por mes -----

    private static async Task<Guid> NuevaDeuda(HttpClient c, Guid miembro, string tipo, decimal importe, bool desdeAhorro = false, string? concepto = null, DateOnly? fecha = null)
    {
        var r = await c.PostAsJsonAsync("/api/cuenta-comun/deudas", new CrearDeudaCuentaRequest(miembro, tipo, importe, fecha ?? Octubre, concepto, desdeAhorro));
        return (await Leer<MovimientoDeudaCuentaDto>(r, HttpStatusCode.Created)).Id;
    }

    [Fact]
    public async Task ElEstadoDelMes_ListaLosMovimientosDeDeuda_ConIdConceptoYFondo()
    {
        var e = await Montar(ahorro: 50m);
        var adelanto = await NuevaDeuda(e.Admin, e.Ana, "adelanto", 40m, concepto: "Fontanero");
        await NuevaDeuda(e.Admin, e.Ana, "devolucion", 10m, desdeAhorro: false, fecha: new DateOnly(2026, 10, 6));
        await NuevaDeuda(e.Admin, e.Ana, "adelanto", 10m, desdeAhorro: true, fecha: new DateOnly(2026, 9, 12)); // de otro mes

        var octubre = await Estado(e.Admin);

        Assert.Equal(["adelanto", "devolucion"], octubre.MovimientosDeuda!.Select(m => m.Tipo));
        var primero = octubre.MovimientosDeuda![0];
        Assert.Equal((adelanto, "Fontanero", false, 40m), (primero.Id, primero.Concepto, primero.DesdeAhorro, primero.Importe));
        // Septiembre solo tiene el suyo.
        var septiembre = await Leer<CuentaComunResponse>(await e.Admin.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal([true], septiembre.MovimientosDeuda!.Select(m => m.DesdeAhorro));
    }

    [Fact]
    public async Task ElDetalleDelMes_PoneElAdelantoAntesQueLaDevolucionDelMismoDia()
    {
        var e = await Montar();
        // Tres movimientos del mismo día: los ids son aleatorios, así que el orden tiene que salir del tipo y no del azar.
        for (var vuelta = 0; vuelta < 3; vuelta++)
        {
            await NuevaDeuda(e.Admin, e.Ana, "adelanto", 40m, fecha: Octubre);
            await NuevaDeuda(e.Admin, e.Ana, "devolucion", 10m, fecha: Octubre);
            await NuevaDeuda(e.Admin, e.Ana, "compensacion", 5m, fecha: Octubre);
        }

        var tipos = (await Estado(e.Admin)).MovimientosDeuda!.Select(m => m.Tipo);

        Assert.Equal(["adelanto", "adelanto", "adelanto", "devolucion", "devolucion", "devolucion", "compensacion", "compensacion", "compensacion"], tipos);
    }

    [Fact]
    public async Task BorrarUnAdelanto_LoQuita_YElEfectivoYLaDeudaVuelven()
    {
        var e = await Montar();
        var antes = await Estado(e.Admin);
        var adelanto = await NuevaDeuda(e.Admin, e.Ana, "adelanto", 40m);

        var r = await e.Admin.DeleteAsync($"/api/cuenta-comun/deudas/{adelanto}");
        var despues = await Estado(e.Admin);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(antes.Efectivo, despues.Efectivo);
        Assert.Empty(despues.Deudas!);
        Assert.Empty(despues.MovimientosDeuda!);
    }

    [Fact]
    public async Task BorrarUnAdelantoYaDevuelto_Es409ConLoQueFalta_YLaDevolucionSePuedeBorrar()
    {
        var e = await Montar();
        var adelanto = await NuevaDeuda(e.Admin, e.Ana, "adelanto", 50m);
        var devolucion = await NuevaDeuda(e.Admin, e.Ana, "devolucion", 20m);

        var rechazado = await e.Admin.DeleteAsync($"/api/cuenta-comun/deudas/{adelanto}");
        var cuerpo = await Leer<JsonElement>(rechazado, HttpStatusCode.Conflict);
        Assert.Equal(20m, cuerpo.GetProperty("falta").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, (await e.Admin.DeleteAsync($"/api/cuenta-comun/deudas/{devolucion}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Admin.DeleteAsync($"/api/cuenta-comun/deudas/{adelanto}")).StatusCode);
    }

    [Fact]
    public async Task BorrarUnaDeudaQueNoExiste_Es404_YSinElPermiso403()
    {
        var sin = await Montar(permisosDeBeto: []);
        var con = await Montar(permisosDeBeto: [CatalogoPermisos.CuentaMovimientos]);
        var adelanto = await NuevaDeuda(sin.Admin, sin.Beto, "adelanto", 10m);

        Assert.Equal(HttpStatusCode.NotFound, (await con.Miembro.DeleteAsync($"/api/cuenta-comun/deudas/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Miembro.DeleteAsync($"/api/cuenta-comun/deudas/{adelanto}")).StatusCode);
        Assert.Single((await Estado(sin.Admin)).Deudas!);
    }

    [Fact]
    public async Task UnaDeudaDeOtroHogar_NoSePuedeBorrar()
    {
        var uno = await Montar();
        var otro = await Montar();
        var ajena = await NuevaDeuda(otro.Admin, otro.Beto, "adelanto", 10m);

        var r = await uno.Admin.DeleteAsync($"/api/cuenta-comun/deudas/{ajena}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Single((await Estado(otro.Admin)).Deudas!);
    }

    [Fact]
    public async Task BorrarUnaDevolucionDelAhorroYaUsada_Es409()
    {
        var e = await Montar(ahorro: 50m); // dos meses de 50: 100 de ahorro
        await NuevaDeuda(e.Admin, e.Ana, "adelanto", 100m, desdeAhorro: true);
        var devolucion = await NuevaDeuda(e.Admin, e.Ana, "devolucion", 100m, desdeAhorro: true);
        // Se vuelve a usar el ahorro repuesto: ya no se puede quitar lo devuelto.
        await NuevaDeuda(e.Admin, e.Beto, "adelanto", 100m, desdeAhorro: true);

        var r = await e.Admin.DeleteAsync($"/api/cuenta-comun/deudas/{devolucion}");

        var cuerpo = await Leer<JsonElement>(r, HttpStatusCode.Conflict);
        Assert.Equal(100m, cuerpo.GetProperty("falta").GetDecimal());
    }

    [Fact]
    public async Task RebajarElAhorroDeUnaAportacion_TieneEnCuentaLoAdelantadoDesdeElAhorro()
    {
        var e = await Montar(ahorro: 50m);
        await NuevaDeuda(e.Admin, e.Ana, "adelanto", 100m, desdeAhorro: true); // gasta todo el ahorro de septiembre y octubre

        var r = await e.Admin.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 300m, 0m));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task ElGastoDesdeElAhorro_TieneEnCuentaLoAdelantadoPorLaCuentaDesdeElAhorro()
    {
        var e = await Montar(ahorro: 50m);
        await NuevaDeuda(e.Admin, e.Ana, "adelanto", 90m, desdeAhorro: true); // quedan 10 de ahorro

        var r = await e.Admin.PostAsJsonAsync("/api/cuenta-comun/deudas",
            new CrearDeudaCuentaRequest(e.Ana, "adelanto", 20m, Octubre, null, DesdeAhorro: true));

        var cuerpo = await Leer<JsonElement>(r, HttpStatusCode.Conflict);
        Assert.Equal(10m, cuerpo.GetProperty("disponible").GetDecimal());
    }

    [Fact]
    public async Task ResumenMensual_RecogeLosMovimientos_ConElMismoEfectivoQueElEstado()
    {
        var e = await Montar();
        await Registrar(e.Admin, e.Ana, "adelanto", 40m);
        await Registrar(e.Admin, e.Ana, "devolucion", 10m);

        var estado = await Estado(e.Admin);
        var hogar = estado.Resumen!.Hogar;

        Assert.Equal(estado.Efectivo, hogar.Disponible);
        Assert.Equal(hogar.Inicial + hogar.Ingresos + hogar.IngresosSueltos - hogar.Gastos - hogar.Reembolsos, hogar.Disponible);
        Assert.True(hogar.Gastos >= 40m);
        Assert.True(hogar.IngresosSueltos >= 10m);
    }
}
