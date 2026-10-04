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
        Assert.Equal(4, (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Count);
        Assert.Equal(6, (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).Count);
        var miembros = await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"));
        var ana = Assert.Single(miembros);
        Assert.Equal("admin", ana.Rol);
        Assert.True(ana.Vinculado);
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
    public async Task Ingreso_Gasto_Resumen_Liquidacion_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa números", "Ana");
        var c = e.Cliente(user, hogar.Id);

        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles")))
            .Single(p => p.Modo == "ingresos");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias")))
            .Single(x => x.Nombre == "Alimentación");

        await Leer<IngresoResponse>(await c.PostAsJsonAsync("/api/ingresos",
            new IngresoRequest(ana.Id, new DateOnly(2026, 10, 1), 2000m, "Nómina")), HttpStatusCode.Created);

        var gasto = await Leer<GastoResponse>(await c.PostAsJsonAsync("/api/gastos",
            new GastoRequest(new DateOnly(2026, 10, 3), 100.01m, categoria.Id, ana.Id, perfil.Id, "Compra")),
            HttpStatusCode.Created);
        Assert.Equal(100.01m, gasto.Repartos.Sum(r => r.ImporteAsumido));

        Assert.Single(await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?mes=2026-10")));

        var resumen = await Leer<ResumenMensualResponse>(await c.GetAsync("/api/resumen?mes=2026-10"));
        Assert.Equal(2000m, resumen.IngresosTotales);
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
