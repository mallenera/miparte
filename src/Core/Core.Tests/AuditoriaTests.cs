using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Contracts;
using static MiParte.Core.Tests.ApiPostgresTests;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class AuditoriaTests
{
    private sealed record Contexto(
        WebApplicationFactory<Program> F, Guid Usuario, HttpClient Admin, Guid Hogar, MiembroDto Ana,
        PerfilRepartoDto Partes, CategoriaDto Categoria);

    /// <summary>Hogar creado por la API (InMemory): Ana es admin; se usan el perfil «Por partes» y la categoría «Alimentación».</summary>
    private static async Task<Contexto> Montar()
    {
        var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var hogar = await CrearHogar(Cliente(f, Token(Hs256(), user)), "Casa", "Ana");
        var c = Cliente(f, Token(Hs256(), user), hogar.Id);
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var partes = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Single(p => p.Nombre == "Por partes");
        var cat = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).Single(x => x.Nombre == "Alimentación");
        return new Contexto(f, user, c, hogar.Id, ana, partes, cat);
    }

    private static async Task<List<EventoAuditoriaDto>> Eventos(HttpClient c, string consulta = "")
        => await Leer<List<EventoAuditoriaDto>>(await c.GetAsync("/api/auditoria" + consulta));

    private static GastoRequest Gasto(Contexto x, decimal importe, string concepto = "Compra")
        => new(new DateOnly(2026, 10, 3), importe, x.Categoria.Id, x.Ana.Id, x.Partes.Id, concepto);

    [Fact]
    public async Task CrearHogar_RegistraUnSoloEvento_NoLaSemilla()
    {
        var x = await Montar();

        var e = Assert.Single(await Eventos(x.Admin));
        Assert.Equal(("crear", "hogar", "Ana"), (e.Accion, e.Entidad, e.Autor));
        Assert.Equal(x.Usuario, e.UsuarioId);
        Assert.Equal(x.Hogar, e.EntidadId);
        Assert.Null(e.Antes);
    }

    [Fact]
    public async Task Gasto_CrearEditarYBorrar_DejanRastroConAntesYDespues()
    {
        var x = await Montar();
        var g = await Leer<GastoResponse>(await x.Admin.PostAsJsonAsync("/api/gastos", Gasto(x, 100m)), HttpStatusCode.Created);
        await x.Admin.PutAsJsonAsync($"/api/gastos/{g.Id}", Gasto(x, 200m));
        await x.Admin.DeleteAsync($"/api/gastos/{g.Id}");

        var porGasto = await Eventos(x.Admin, $"?entidad=gasto&entidadId={g.Id}");
        Assert.Equal(["borrar", "editar", "crear"], porGasto.Select(e => e.Accion)); // más reciente primero
        var (borrar, editar, crear) = (porGasto[0], porGasto[1], porGasto[2]);

        Assert.Null(crear.Antes);
        Assert.Equal(100m, crear.Despues!.Value.GetProperty("importe").GetDecimal());
        Assert.Equal(1, crear.Despues.Value.GetProperty("repartos").GetArrayLength());

        // En la edición solo constan los campos que cambian: el concepto no se tocó.
        Assert.Equal(100m, editar.Antes!.Value.GetProperty("importe").GetDecimal());
        Assert.Equal(200m, editar.Despues!.Value.GetProperty("importe").GetDecimal());
        Assert.False(editar.Antes.Value.TryGetProperty("concepto", out _));

        Assert.Equal(200m, borrar.Antes!.Value.GetProperty("importe").GetDecimal());
        Assert.Null(borrar.Despues);
        Assert.All(porGasto, e => Assert.Equal("Ana", e.Autor));
    }

    [Fact]
    public async Task EditarGastoSinCambiosReales_NoGeneraEvento()
    {
        var x = await Montar();
        var g = await Leer<GastoResponse>(await x.Admin.PostAsJsonAsync("/api/gastos", Gasto(x, 50m)), HttpStatusCode.Created);

        await x.Admin.PutAsJsonAsync($"/api/gastos/{g.Id}", Gasto(x, 50m));

        Assert.Equal(["crear"], (await Eventos(x.Admin, $"?entidad=gasto&entidadId={g.Id}")).Select(e => e.Accion));
    }

    [Fact]
    public async Task Perfil_SoloCambiaElDetalle_QuedaRegistradoElDetalle()
    {
        var x = await Montar();
        var p = await Leer<PerfilRepartoDto>(await x.Admin.PostAsJsonAsync("/api/perfiles",
            new GuardarPerfilRequest("Mi perfil", "partes", [new PerfilDetalleDto(x.Ana.Id, 1)])), HttpStatusCode.Created);

        await x.Admin.PutAsJsonAsync($"/api/perfiles/{p.Id}",
            new GuardarPerfilRequest("Mi perfil", "partes", [new PerfilDetalleDto(x.Ana.Id, 3)]));

        var editar = (await Eventos(x.Admin, $"?entidad=perfil_reparto&entidadId={p.Id}")).First();
        Assert.Equal("editar", editar.Accion);
        Assert.Equal(1m, editar.Antes!.Value.GetProperty("detalle")[0].GetProperty("valor").GetDecimal());
        Assert.Equal(3m, editar.Despues!.Value.GetProperty("detalle")[0].GetProperty("valor").GetDecimal());
        Assert.False(editar.Antes.Value.TryGetProperty("nombre", out _));
    }

    [Fact]
    public async Task SoloUnAdminPuedeLeerElHistorial()
    {
        var x = await Montar();
        var inv = await Leer<InvitacionCreada>(
            await x.Admin.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null)), HttpStatusCode.Created);
        var otro = Guid.NewGuid();
        var cOtro = Cliente(x.F, Token(Hs256(), otro));
        Assert.Equal(HttpStatusCode.OK, (await cOtro.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Beto"))).StatusCode);

        var comoMiembro = Cliente(x.F, Token(Hs256(), otro), x.Hogar);
        var r = await comoMiembro.GetAsync("/api/auditoria");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(x.F, null).GetAsync("/api/auditoria")).StatusCode);
    }

    [Fact]
    public async Task ElHistorial_NuncaContieneElTokenNiSuHash()
    {
        var x = await Montar();
        var inv = await Leer<InvitacionCreada>(
            await x.Admin.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null)), HttpStatusCode.Created);
        await Cliente(x.F, Token(Hs256(), Guid.NewGuid()))
            .PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Beto"));

        var texto = await (await x.Admin.GetAsync("/api/auditoria?limite=200")).Content.ReadAsStringAsync();

        Assert.Contains("\"entidad\":\"invitacion\"", texto);
        Assert.DoesNotContain(inv.Token, texto);
        Assert.DoesNotContain(MiParte.Core.Api.Miembros.MiembrosEndpoints.HashToken(inv.Token), texto);
        Assert.DoesNotContain("tokenHash", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElHistorialDeUnHogar_NoSeVeDesdeOtro()
    {
        var x = await Montar();
        await x.Admin.PostAsJsonAsync("/api/gastos", Gasto(x, 10m));

        var otroUsuario = Guid.NewGuid();
        var otroHogar = await CrearHogar(Cliente(x.F, Token(Hs256(), otroUsuario)), "Otra casa", "Zoe");
        var eventos = await Eventos(Cliente(x.F, Token(Hs256(), otroUsuario), otroHogar.Id));

        var e = Assert.Single(eventos);
        Assert.Equal(otroHogar.Id, e.EntidadId);
    }

    [Fact]
    public async Task Listado_ValidaElLimiteYPagina()
    {
        var x = await Montar();
        for (var i = 1; i <= 3; i++) await x.Admin.PostAsJsonAsync("/api/gastos", Gasto(x, i * 10m));

        Assert.Equal(HttpStatusCode.BadRequest, (await x.Admin.GetAsync("/api/auditoria?limite=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await x.Admin.GetAsync("/api/auditoria?limite=201")).StatusCode);

        var primera = await Eventos(x.Admin, "?limite=2");
        Assert.Equal(2, primera.Count);
        var siguiente = await Eventos(x.Admin, Cursor(primera[^1], 50));
        Assert.DoesNotContain(siguiente, e => primera.Any(p => p.Id == e.Id));
        Assert.Equal(4, primera.Count + siguiente.Count); // hogar + 3 gastos
    }

    private static string Cursor(EventoAuditoriaDto ultimo, int limite)
        => $"?limite={limite}&hasta={Uri.EscapeDataString(ultimo.Cuando.ToString("O"))}&despuesDeId={ultimo.Id}";

    [Fact]
    public async Task Paginacion_NoPierdeEventosQueComparteInstante()
    {
        var x = await Montar();
        // Un mismo guardado con varias entidades produce eventos con idéntico «cuando».
        using (var scope = x.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            for (var i = 0; i < 5; i++)
                db.Categorias.Add(new Categoria { Id = Guid.NewGuid(), HogarId = x.Hogar, Nombre = "C" + i });
            await db.SaveChangesAsync();
        }
        var todos = await Eventos(x.Admin, "?limite=200");
        Assert.True(todos.GroupBy(e => e.Cuando).Any(g => g.Count() > 1), "el escenario debe tener empates de instante");

        var recorridos = new List<EventoAuditoriaDto>();
        var pagina = await Eventos(x.Admin, "?limite=1");
        while (pagina.Count > 0)
        {
            recorridos.AddRange(pagina);
            pagina = await Eventos(x.Admin, Cursor(pagina[^1], 1));
        }

        Assert.Equal(todos.Select(e => e.Id), recorridos.Select(e => e.Id)); // ni se pierde ni se repite ninguno, en el mismo orden
    }

    // --- PostgreSQL real: jsonb, ExecuteUpdate y la inmutabilidad de la tabla -----------------------------

    [SkippableFact]
    public async Task AceptarInvitacion_EnPostgres_RegistraUsarYCrearMiembro()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var admin = await e.CrearUsuarioAsync();
        var invitado = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(admin), "Casa auditoría", "Ana");
        var c = e.Cliente(admin, hogar.Id);
        var inv = await Leer<InvitacionCreada>(
            await c.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null)), HttpStatusCode.Created);

        await Leer<HogarResumen>(await e.Cliente(invitado).PostAsJsonAsync(
            "/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Beto")));

        var eventos = await Leer<List<EventoAuditoriaDto>>(await c.GetAsync("/api/auditoria?limite=200"));
        var usar = Assert.Single(eventos, x => x.Accion == "usar");
        Assert.Equal(invitado, usar.UsuarioId);
        Assert.Equal(inv.Id, usar.EntidadId);
        var alta = Assert.Single(eventos, x => x is { Accion: "crear", Entidad: "miembro" } && x.UsuarioId == invitado);
        Assert.Equal("Beto", alta.Despues!.Value.GetProperty("nombre").GetString());
        Assert.DoesNotContain(inv.Token, JsonSerializer.Serialize(eventos));
        Assert.Equal(0, eventos.Count(x => x.Entidad is "perfil_reparto" or "categoria")); // la semilla no se detalla
    }

    [SkippableFact]
    public async Task Auditoria_EnPostgres_EsDeSoloAnadir_AunParaElPropietario()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa inmutable", "Ana");

        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        foreach (var sql in new[]
                 {
                     "update public.auditoria set accion = 'borrar' where hogar_id = @h",
                     "delete from public.auditoria where hogar_id = @h",
                     "truncate public.auditoria",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("h", hogar.Id);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Contains("solo añadir", ex.MessageText);
        }

        // El borrado en cascada al eliminar el hogar sí está permitido.
        await using (var cmd = new NpgsqlCommand("delete from public.hogar where id = @h", conn))
        {
            cmd.Parameters.AddWithValue("h", hogar.Id);
            await cmd.ExecuteNonQueryAsync();
        }
        await using var cuenta = new NpgsqlCommand("select count(*) from public.auditoria where hogar_id = @h", conn);
        cuenta.Parameters.AddWithValue("h", hogar.Id);
        Assert.Equal(0L, await cuenta.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task Auditoria_EnPostgres_LosRolesDeClienteSoloLeen()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        async Task<bool> Tiene(string rol, string privilegio)
        {
            await using var cmd = new NpgsqlCommand($"select has_table_privilege('{rol}', 'public.auditoria', '{privilegio}')", conn);
            return (bool)(await cmd.ExecuteScalarAsync())!;
        }

        Assert.True(await Tiene("authenticated", "SELECT"));
        foreach (var p in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
            Assert.False(await Tiene("authenticated", p), $"authenticated no debe tener {p}");
        Assert.False(await Tiene("anon", "SELECT"));
    }
}
