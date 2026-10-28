using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using MiParte.Contracts;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Pruebas de extremo a extremo de Core.Api contra PostgreSQL real (sin InMemory), para detectar lo que
/// InMemory no ve: consultas que no se traducen a SQL, orden de INSERT frente a claves foráneas y
/// restricciones del esquema. Se saltan sin MIPARTE_TEST_DB; en CI siempre se ejecutan.
/// Cada prueba crea sus propios usuarios y hogares y los borra al terminar.
/// </summary>
public class ApiPostgresTests
{
    internal static readonly string? Cadena = Environment.GetEnvironmentVariable("MIPARTE_TEST_DB");
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>URL de Supabase de las pruebas: debe coincidir con la de AutenticacionTests (emisor del token).</summary>
    private const string UrlSupabase = "https://proyecto.supabase.co";

    /// <summary>
    /// Arranca Core.Api con la base de datos real y gestiona los usuarios de prueba (filas en auth.users).
    /// Al liberarse borra los hogares de esos usuarios (cascada) y los propios usuarios.
    /// </summary>
    internal sealed class Entorno : IAsyncDisposable
    {
        private readonly List<Guid> _usuarios = [];

        /// <summary>Fábrica de la aplicación conectada a MIPARTE_TEST_DB.</summary>
        public WebApplicationFactory<Program> Fabrica { get; } = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:Default", Cadena);
                b.UseSetting("Supabase:Url", UrlSupabase);
                b.UseSetting("Supabase:JwtSecret", Secreto);
            });

        /// <summary>Inserta un usuario en auth.users (real o stub) y devuelve su id.</summary>
        public async Task<Guid> CrearUsuarioAsync()
        {
            var id = Guid.NewGuid();
            await using var conn = new NpgsqlConnection(Cadena);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("insert into auth.users (id) values (@u)", conn);
            cmd.Parameters.AddWithValue("u", id);
            await cmd.ExecuteNonQueryAsync();
            _usuarios.Add(id);
            return id;
        }

        /// <summary>Cliente HTTP autenticado como el usuario, con el hogar seleccionado si se indica.</summary>
        public HttpClient Cliente(Guid usuario, Guid? hogar = null)
            => AutenticacionTests.Cliente(Fabrica, Token(Hs256(), usuario), hogar);

        /// <summary>Borra los datos de la prueba y libera la aplicación.</summary>
        public async ValueTask DisposeAsync()
        {
            Fabrica.Dispose();
            if (_usuarios.Count == 0) return;
            await using var conn = new NpgsqlConnection(Cadena);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                delete from public.hogar where id in (select hogar_id from public.miembro where user_id = any(@u));
                delete from auth.users where id = any(@u);
                """, conn);
            cmd.Parameters.AddWithValue("u", _usuarios.ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Comprueba el código HTTP y deserializa el cuerpo; si falla, muestra el cuerpo en el mensaje.</summary>
    internal static async Task<T> Leer<T>(HttpResponseMessage r, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        if (r.StatusCode != esperado)
            throw new Xunit.Sdk.XunitException(
                $"Se esperaba {(int)esperado} y llegó {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Web))!;
    }

    /// <summary>Crea un hogar por la API y devuelve su resumen.</summary>
    internal static async Task<HogarResumen> CrearHogar(HttpClient c, string nombre, string miembro)
        => await Leer<HogarResumen>(
            await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest(nombre, miembro)), HttpStatusCode.Created);

    [SkippableFact]
    public async Task CrearHogar_SiembraPerfilesCategoriasYCreadorAdmin_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();

        var hogar = await CrearHogar(e.Cliente(user), "Casa integración", "Ana");

        var c = e.Cliente(user, hogar.Id);
        Assert.Equal(5, (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Count);
        Assert.Equal(6, (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).Count);
        var miembros = await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"));
        var ana = Assert.Single(miembros);
        Assert.Equal("admin", ana.Rol);
        Assert.True(ana.Vinculado);
    }

    [SkippableFact]
    public async Task CuentaComun_ActivacionYCategoriaACargo_SePersistenEnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa cuenta", "Ana");
        var c = e.Cliente(user, hogar.Id);

        Assert.False((await Leer<CuentaComunResponse>(await c.GetAsync("/api/cuenta-comun?mes=2027-01"))).Activa);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("Comunidad", null, null, true))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true))).StatusCode);
        Assert.True((await Leer<CuentaComunResponse>(await c.GetAsync("/api/cuenta-comun?mes=2027-01"))).Activa);

        var cuenta = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Single(p => p.Modo == "cuenta_comun");
        var cat = await Leer<CategoriaDto>(await c.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("Comunidad", null, null, true)), HttpStatusCode.Created);
        Assert.True(cat.ACargoCuentaComun);
        Assert.Equal(cuenta.Id, cat.PerfilRepartoId);
        Assert.True((await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).Single(x => x.Id == cat.Id).ACargoCuentaComun);

        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(ana.Id, new DateOnly(2027, 1, 1), 300m, 50m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/cuenta-comun/ahorro/activacion", new ActivarAhorroRequest(true))).StatusCode);
        var aport = await c.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(ana.Id, new DateOnly(2027, 1, 1), 300m, 50m));
        Assert.Equal(HttpStatusCode.OK, aport.StatusCode);
        var resumen = await Leer<ResumenMensualResponse>(await c.GetAsync("/api/resumen?mes=2027-01"));
        Assert.Equal(250m, resumen.CuentaComun!.Saldo);
        Assert.Equal(50m, resumen.CuentaComun.AhorroDisponible);
    }

    [SkippableFact]
    public async Task DeudasDeLaCuenta_SeRegistranYCuadranEnEstadoYBanco_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa deudas", "Ana");
        var c = e.Cliente(user, hogar.Id);
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true)));
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(ana.Id, new DateOnly(2026, 9, 1), 300m, 0m)));
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/deudas",
            new CrearDeudaCuentaRequest(ana.Id, "adelanto", 40m, new DateOnly(2026, 10, 5), "Fontanero")), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/deudas",
            new CrearDeudaCuentaRequest(ana.Id, "devolucion", 15m, new DateOnly(2026, 10, 6), null)), HttpStatusCode.Created);
        // Ya debe 25: una devolución de 100 supera la deuda y una compensación no tiene pendiente de reembolso que restar.
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/deudas",
            new CrearDeudaCuentaRequest(ana.Id, "devolucion", 100m, new DateOnly(2026, 10, 7), null)), HttpStatusCode.Conflict);
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/deudas",
            new CrearDeudaCuentaRequest(ana.Id, "compensacion", 5m, new DateOnly(2026, 10, 7), null)), HttpStatusCode.Conflict);

        var octubre = await Leer<CuentaComunResponse>(await c.GetAsync("/api/cuenta-comun?mes=2026-10"));
        // Aportado 600 (sep. y oct.), menos el adelanto de 40 y más la devolución de 15.
        Assert.Equal(575m, octubre.Efectivo);
        var deuda = Assert.Single(octubre.Deudas!);
        Assert.Equal(25m, deuda.Importe);
        Assert.Equal(octubre.Efectivo, (await Leer<MovimientosCuentaComunResponse>(
            await c.GetAsync("/api/cuenta-comun/movimientos?desde=2026-10-01&hasta=2026-10-31"))).EfectivoFinal);
    }

    [SkippableFact]
    public async Task CuentaComunCompleta_ResumenYEstadoRespondenOk_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa diag", "Ana");
        var c = e.Cliente(user, hogar.Id);
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfilCuenta = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Single(p => p.Modo == "cuenta_comun");
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true)));
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/ahorro/activacion", new ActivarAhorroRequest(true)));
        var cat = await Leer<CategoriaDto>(await c.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("Comunidad", null, null, true)), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(ana.Id, new DateOnly(2026, 9, 1), 300m, 50m)));
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/saldo-inicial", new FijarSaldoInicialRequest(new DateOnly(2026, 8, 15), 1000m)));
        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos", new GastoRequest(new DateOnly(2026, 9, 10), 100m, cat.Id, ana.Id, perfilCuenta.Id, "A")), HttpStatusCode.Created);
        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos", new GastoRequest(new DateOnly(2026, 10, 5), 40m, cat.Id, null, perfilCuenta.Id, "B")), HttpStatusCode.Created);
        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos", new GastoRequest(new DateOnly(2026, 10, 6), 20m, cat.Id, null, perfilCuenta.Id, "C", PagadoDesdeAhorro: true)), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/reembolsos", new CrearReembolsoRequest(ana.Id, 30m, new DateOnly(2026, 10, 7), "R")), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro", new CrearRetiradaAhorroRequest(ana.Id, 10m, new DateOnly(2026, 10, 8), "T")), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(ana.Id, 5m, new DateOnly(2026, 9, 20), "D")), HttpStatusCode.Created);

        var luis = await Leer<MiembroDto>(await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Luis", "adulto")), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(luis.Id, new DateOnly(2026, 10, 1), 200m, 0m)));
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(luis.Id, 7m, new DateOnly(2026, 10, 2), null)), HttpStatusCode.Created);
        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos", new GastoRequest(new DateOnly(2026, 10, 9), 15m, cat.Id, luis.Id, perfilCuenta.Id, "D")), HttpStatusCode.Created);
        await Leer<JsonElement>(await c.PostAsJsonAsync("/api/cuenta-comun/ingresos", new CrearIngresoCuentaComunRequest(luis.Id, 25m, new DateOnly(2026, 10, 3), "Cena")), HttpStatusCode.Created);
        foreach (var mes in new[] { "2026-06", "2026-08", "2026-09", "2026-10", "2026-11" })
        {
            await Leer<JsonElement>(await c.GetAsync($"/api/cuenta-comun?mes={mes}"));
            await Leer<JsonElement>(await c.GetAsync($"/api/resumen?mes={mes}"));
        }

        // Extracto bancario de octubre: antes había saldo inicial 1000 + aportación de Ana 300 + depósito 5; el gasto A y el D los
        // adelanta una persona, así que no salen. El efectivo y el ahorro al final cuadran con el estado de la cuenta del mes.
        var mov = await Leer<MovimientosCuentaComunResponse>(await c.GetAsync("/api/cuenta-comun/movimientos?desde=2026-10-01&hasta=2026-10-31"));
        Assert.Equal(1305m, mov.SaldoAnterior);
        Assert.Equal(["aportacion", "aportacion", "deposito_ahorro", "ingreso", "gasto", "gasto_ahorro", "reembolso", "retirada_ahorro"], mov.Movimientos.Select(m => m.Tipo));
        Assert.Equal(532m, mov.Entradas);
        Assert.Equal(100m, mov.Salidas);
        Assert.Equal(1737m, mov.SaldoFinal);
        var octubre = await Leer<CuentaComunResponse>(await c.GetAsync("/api/cuenta-comun?mes=2026-10"));
        Assert.Equal(25m, octubre.Extracto!.Ingresos);
        Assert.Equal(octubre.Efectivo, mov.EfectivoFinal);
        Assert.Equal(octubre.AhorroDisponible, mov.AhorroFinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cuenta-comun/movimientos?desde=2026-10-31&hasta=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/cuenta-comun/movimientos?desde=2026-10-01")).StatusCode);
    }

    [SkippableFact]
    public async Task Yo_ListaVariosHogares_SinCabeceraYConCabecera_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var h1 = await CrearHogar(e.Cliente(user), "Casa A", "Ana");
        var h2 = await CrearHogar(e.Cliente(user), "Casa B", "Ana");

        var sin = await Leer<YoResponse>(await e.Cliente(user).GetAsync("/api/yo"));
        Assert.Equal(2, sin.Hogares.Count);
        Assert.Null(sin.HogarActual);

        var con = await Leer<YoResponse>(await e.Cliente(user, h2.Id).GetAsync("/api/yo"));
        Assert.Equal(h2.Id, con.HogarActual!.Id);

        var lista = await Leer<List<HogarResumen>>(await e.Cliente(user).GetAsync("/api/hogares"));
        Assert.Equal([h1.Id, h2.Id], lista.Select(h => h.Id)); // la API ordena por nombre: "Casa A", "Casa B"
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente(user).GetAsync($"/api/hogares/{h1.Id}")).StatusCode);
    }

    [SkippableFact]
    public async Task EliminarHogar_BorraEnCascadaTodosSusDatos_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa a borrar", "Ana");
        var otro = await CrearHogar(e.Cliente(user), "Casa que se queda", "Ana");
        var c = e.Cliente(user, hogar.Id);

        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Single(p => p.Modo == "partes");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).Single(x => x.Nombre == "Alimentación");
        await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos",
            new GastoRequest(new DateOnly(2020, 3, 3), 20m, categoria.Id, ana.Id, perfil.Id, "Compra")), HttpStatusCode.Created);
        // Un mes cerrado con gastos y el historial (solo de añadir) no deben impedir el borrado en cascada.
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/cierres-mes", new CerrarMesRequest("2020-03"))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync("/api/hogar?nombre=otro")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/hogar?nombre=Casa a borrar")).StatusCode);

        var lista = await Leer<List<HogarResumen>>(await e.Cliente(user).GetAsync("/api/hogares"));
        Assert.Equal([otro.Id], lista.Select(h => h.Id));
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        foreach (var tabla in new[] { "miembro", "gasto", "gasto_reparto", "categoria", "perfil_reparto", "mes_cerrado", "auditoria" })
        {
            await using var cmd = new NpgsqlCommand($"select count(*) from public.{tabla} where hogar_id = @h", conn);
            cmd.Parameters.AddWithValue("h", hogar.Id);
            Assert.Equal(0L, await cmd.ExecuteScalarAsync());
        }
    }

    [SkippableFact]
    public async Task Gastos_FiltraPorRangoDeFechasInclusivoYValidaLosParametros()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa rango", "Ana");
        var c = e.Cliente(user, hogar.Id);

        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Single(p => p.Modo == "partes");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).First();
        foreach (var dia in new[] { 30, 1, 15 }) // 30 de septiembre y 1 y 15 de octubre
        {
            var fecha = dia == 30 ? new DateOnly(2026, 9, 30) : new DateOnly(2026, 10, dia);
            await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos",
                new GastoRequest(fecha, 10m, categoria.Id, ana.Id, perfil.Id, null)), HttpStatusCode.Created);
        }

        var rango = await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?desde=2026-09-30&hasta=2026-10-01"));
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30)], rango.Select(g => g.Fecha));
        Assert.Single(await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?desde=2026-10-15")));
        Assert.Equal(2, (await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?hasta=2026-10-01"))).Count);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gastos?desde=2026-10")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gastos?desde=2026-10-02&hasta=2026-10-01")).StatusCode);
    }

    [SkippableFact]
    public async Task Gasto_Resumen_Liquidacion_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa números", "Ana");
        var c = e.Cliente(user, hogar.Id);

        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles")))
            .Single(p => p.Modo == "partes");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias")))
            .Single(x => x.Nombre == "Alimentación");

        var gasto = await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos",
            new GastoRequest(new DateOnly(2026, 10, 3), 100.01m, categoria.Id, ana.Id, perfil.Id, "Compra")),
            HttpStatusCode.Created);
        Assert.Equal(100.01m, gasto.Repartos.Sum(r => r.ImporteAsumido));

        Assert.Single(await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?mes=2026-10")));

        var resumen = await Leer<ResumenMensualResponse>(await c.GetAsync("/api/resumen?mes=2026-10"));
        Assert.Equal(100.01m, resumen.GastosTotales);

        var liquidacion = await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-10"));
        Assert.Empty(liquidacion.Transferencias); // hogar de un solo adulto: nada que liquidar
    }

    [SkippableFact]
    public async Task Invitacion_Aceptar_VinculaMiembro_YNoSePuedeReutilizar_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var admin = await e.CrearUsuarioAsync();
        var invitado = await e.CrearUsuarioAsync();
        var otro = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(admin), "Casa invitaciones", "Ana");

        var invitacion = await Leer<InvitacionCreada>(
            await e.Cliente(admin, hogar.Id).PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null)),
            HttpStatusCode.Created);

        var aceptada = await Leer<HogarResumen>(await e.Cliente(invitado).PostAsJsonAsync(
            "/api/invitaciones/aceptar", new AceptarInvitacionRequest(invitacion.Token, "Beto")));
        Assert.Equal(hogar.Id, aceptada.Id);

        var miembros = await Leer<List<MiembroDto>>(await e.Cliente(admin, hogar.Id).GetAsync("/api/miembros"));
        Assert.Equal(2, miembros.Count);
        Assert.Contains(miembros, m => m.Nombre == "Beto" && m.Rol == "miembro" && m.Vinculado);

        // El mismo código no se puede usar dos veces, ni por otra persona.
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente(otro).PostAsJsonAsync(
            "/api/invitaciones/aceptar", new AceptarInvitacionRequest(invitacion.Token, "Carla"))).StatusCode);
    }
}
