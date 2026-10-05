using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Api.Miembros;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class MiembrosTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> Sembrar(WebApplicationFactory<Program> f, Guid hogar, Guid? user, string nombre,
        RolMiembro rol = RolMiembro.Miembro, TipoMiembro tipo = TipoMiembro.Adulto, Guid? responsable = null, bool activo = true)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        if (!await db.Hogares.IgnoreQueryFilters().AnyAsync(h => h.Id == hogar))
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa" });
        var id = Guid.NewGuid();
        db.Miembros.Add(new Miembro
        {
            Id = id, HogarId = hogar, Nombre = nombre, Tipo = tipo, UserId = user, Rol = rol,
            ResponsableId = responsable, Activo = activo,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static HttpClient Con(WebApplicationFactory<Program> f, Guid user, Guid? hogar = null)
        => Cliente(f, Token(Hs256(), user), hogar);

    private static async Task<T> Leer<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>(Web))!;

    // Escenario base: hogar con un admin (ana) y un miembro normal (beto).
    private sealed record Escenario(WebApplicationFactory<Program> F, Guid Hogar, Guid Ana, Guid AnaId, Guid Beto, Guid BetoId)
    {
        public HttpClient ComoAna => Con(F, Ana, Hogar);
        public HttpClient ComoBeto => Con(F, Beto, Hogar);
    }

    private static async Task<Escenario> Crear()
    {
        var f = AutenticacionTests.Crear(Secreto);
        var hogar = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        var anaId = await Sembrar(f, hogar, ana, "Ana", RolMiembro.Admin);
        var betoId = await Sembrar(f, hogar, beto, "Beto");
        return new Escenario(f, hogar, ana, anaId, beto, betoId);
    }

    // ---- Listado ----

    [Fact]
    public async Task Listar_DevuelveSoloActivosDelHogar()
    {
        var s = await Crear();
        using var _ = s.F;
        await Sembrar(s.F, s.Hogar, null, "Inactivo", activo: false);
        await Sembrar(s.F, Guid.NewGuid(), Guid.NewGuid(), "DeOtroHogar");

        var lista = await Leer<List<MiembroDto>>(await s.ComoBeto.GetAsync("/api/miembros"));

        Assert.Equal(["Ana", "Beto"], lista.Select(m => m.Nombre));
        Assert.Equal("admin", lista[0].Rol);
        Assert.True(lista[0].Vinculado);
    }

    [Fact]
    public async Task SinToken_401()
    {
        using var f = AutenticacionTests.Crear(Secreto);
        var anonimo = Cliente(f, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/miembros")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest("x"))).StatusCode);
    }

    // ---- Alta ----

    [Fact]
    public async Task Crear_Admin_AdultoACargo_201()
    {
        var s = await Crear();
        using var _ = s.F;

        var adulto = await s.ComoAna.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Carla", "adulto"));
        Assert.Equal(HttpStatusCode.Created, adulto.StatusCode);
        var nino = await s.ComoAna.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Nico", "a_cargo", s.AnaId));
        Assert.Equal(HttpStatusCode.Created, nino.StatusCode);
        var dto = await Leer<MiembroDto>(nino);
        Assert.Equal(s.AnaId, dto.ResponsableId);
        Assert.False(dto.Vinculado);
        Assert.Equal("miembro", dto.Rol);
    }

    [Fact]
    public async Task Crear_NoAdmin_403()
    {
        var s = await Crear();
        using var _ = s.F;
        var r = await s.ComoBeto.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Carla", "adulto"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Crear_Validaciones_400()
    {
        var s = await Crear();
        using var _ = s.F;
        var c = s.ComoAna;
        var otroAdulto = await Sembrar(s.F, Guid.NewGuid(), null, "Ajeno"); // de otro hogar
        var nino = await Sembrar(s.F, s.Hogar, null, "Nico", tipo: TipoMiembro.ACargo, responsable: s.AnaId);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("  ", "adulto"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest(new string('x', 101), "adulto"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("X", "otro"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("X", "a_cargo"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("X", "a_cargo", otroAdulto))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("X", "a_cargo", nino))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("X", "adulto", s.AnaId))).StatusCode);
    }

    [Fact]
    public async Task Perfil_ConDetalleDeACargo_400()
    {
        var s = await Crear();
        using var _ = s.F;
        var nino = await Sembrar(s.F, s.Hogar, null, "Nico", tipo: TipoMiembro.ACargo, responsable: s.AnaId);

        var r = await s.ComoAna.PostAsJsonAsync("/api/perfiles", new GuardarPerfilRequest("Con niño", "porcentaje",
            [new PerfilDetalleDto(s.AnaId, 50), new PerfilDetalleDto(nino, 50)]));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ---- Actualización ----

    [Fact]
    public async Task Actualizar_NoAdmin_PuedeRenombrarseASiMismo()
    {
        var s = await Crear();
        using var _ = s.F;
        var r = await s.ComoBeto.PutAsJsonAsync($"/api/miembros/{s.BetoId}", new ActualizarMiembroRequest(Nombre: "  Alberto "));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Alberto", (await Leer<MiembroDto>(r)).Nombre);
    }

    [Fact]
    public async Task Actualizar_NoAdmin_NoPuedeCambiarRolNiEstadoNiOtros_403()
    {
        var s = await Crear();
        using var _ = s.F;
        var c = s.ComoBeto;

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/api/miembros/{s.BetoId}", new ActualizarMiembroRequest(Rol: "admin"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/api/miembros/{s.BetoId}", new ActualizarMiembroRequest(Activo: false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/api/miembros/{s.AnaId}", new ActualizarMiembroRequest(Nombre: "Hack"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync($"/api/miembros/{s.AnaId}")).StatusCode);
    }

    [Fact]
    public async Task Actualizar_Inexistente_OAjeno_404()
    {
        var s = await Crear();
        using var _ = s.F;
        var ajeno = await Sembrar(s.F, Guid.NewGuid(), Guid.NewGuid(), "Ajeno");

        Assert.Equal(HttpStatusCode.NotFound, (await s.ComoAna.PutAsJsonAsync($"/api/miembros/{Guid.NewGuid()}", new ActualizarMiembroRequest(Nombre: "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.ComoAna.PutAsJsonAsync($"/api/miembros/{ajeno}", new ActualizarMiembroRequest(Nombre: "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.ComoAna.DeleteAsync($"/api/miembros/{ajeno}")).StatusCode);
    }

    [Fact]
    public async Task Actualizar_Admin_PromocionaYDesactivaConDeleteLogico()
    {
        var s = await Crear();
        using var _ = s.F;

        var promo = await s.ComoAna.PutAsJsonAsync($"/api/miembros/{s.BetoId}", new ActualizarMiembroRequest(Rol: "admin"));
        Assert.Equal("admin", (await Leer<MiembroDto>(promo)).Rol);

        var del = await s.ComoAna.DeleteAsync($"/api/miembros/{s.BetoId}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.False((await Leer<MiembroDto>(del)).Activo);

        var lista = await Leer<List<MiembroDto>>(await s.ComoAna.GetAsync("/api/miembros"));
        Assert.Equal("Ana", Assert.Single(lista).Nombre);

        // Sigue existiendo en BD (borrado lógico).
        using var scope = s.F.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<MiParteDbContext>()
            .Miembros.IgnoreQueryFilters().AnyAsync(m => m.Id == s.BetoId && !m.Activo));
    }

    [Fact]
    public async Task UltimoAdmin_NoSePuedeDesactivarNiDegradar_409()
    {
        var s = await Crear();
        using var _ = s.F;
        var c = s.ComoAna;

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/miembros/{s.AnaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/miembros/{s.AnaId}", new ActualizarMiembroRequest(Rol: "miembro"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/miembros/{s.AnaId}", new ActualizarMiembroRequest(Activo: false))).StatusCode);

        // Con otro admin ya puede degradarse.
        await c.PutAsJsonAsync($"/api/miembros/{s.BetoId}", new ActualizarMiembroRequest(Rol: "admin"));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/miembros/{s.AnaId}", new ActualizarMiembroRequest(Rol: "miembro"))).StatusCode);
        // Ahora Beto es el último.
        Assert.Equal(HttpStatusCode.Conflict, (await s.ComoBeto.DeleteAsync($"/api/miembros/{s.BetoId}")).StatusCode);
    }

    [Fact]
    public async Task UnAdminSinUsuarioNoCuentaComoOtroAdmin_409()
    {
        var s = await Crear();
        using var _ = s.F;
        await Sembrar(s.F, s.Hogar, null, "Fantasma", RolMiembro.Admin); // admin sin user_id

        Assert.Equal(HttpStatusCode.Conflict, (await s.ComoAna.DeleteAsync($"/api/miembros/{s.AnaId}")).StatusCode);
    }

    [Fact]
    public async Task Desactivar_ResponsableDeACargoActivo_409_YResponsableValido()
    {
        var s = await Crear();
        using var _ = s.F;
        var nino = await Sembrar(s.F, s.Hogar, null, "Nico", tipo: TipoMiembro.ACargo, responsable: s.BetoId);

        Assert.Equal(HttpStatusCode.Conflict, (await s.ComoAna.DeleteAsync($"/api/miembros/{s.BetoId}")).StatusCode);

        // Reasignar el responsable y entonces sí.
        var re = await s.ComoAna.PutAsJsonAsync($"/api/miembros/{nino}", new ActualizarMiembroRequest(ResponsableId: s.AnaId));
        Assert.Equal(HttpStatusCode.OK, re.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.ComoAna.DeleteAsync($"/api/miembros/{s.BetoId}")).StatusCode);

        // Responsable inválido (un adulto no tiene responsable; el de a_cargo debe ser adulto activo).
        Assert.Equal(HttpStatusCode.BadRequest, (await s.ComoAna.PutAsJsonAsync($"/api/miembros/{s.AnaId}", new ActualizarMiembroRequest(ResponsableId: s.AnaId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.ComoAna.PutAsJsonAsync($"/api/miembros/{nino}", new ActualizarMiembroRequest(ResponsableId: s.BetoId))).StatusCode);
    }

    // ---- Invitaciones ----

    private static async Task<InvitacionCreada> Invitar(HttpClient admin, Guid? miembroId = null)
    {
        var r = await admin.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(miembroId));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await Leer<InvitacionCreada>(r);
    }

    [Fact]
    public async Task CrearInvitacion_GuardaSoloElHash_YCaducaA7Dias()
    {
        var s = await Crear();
        using var _ = s.F;

        var inv = await Invitar(s.ComoAna);

        Assert.Equal(43, inv.Token.Length); // 32 bytes en base64url sin relleno
        Assert.DoesNotContain(inv.Token, ['+', '/', '=']);
        Assert.InRange(inv.CaducaEn, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));

        using var scope = s.F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var fila = await db.Invitaciones.IgnoreQueryFilters().SingleAsync();
        Assert.NotEqual(inv.Token, fila.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", fila.TokenHash);
        Assert.Equal(MiembrosEndpoints.HashToken(inv.Token), fila.TokenHash);
        Assert.Equal(s.Ana, fila.CreadaPor);
        Assert.Equal(s.Hogar, fila.HogarId);
    }

    [Fact]
    public async Task CrearInvitacion_HashCoincideConSha256HexConocido()
    {
        // SHA-256("abc") en hex minúscula, igual que encode(sha256(convert_to(token,'UTF8')),'hex') en SQL.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", MiembrosEndpoints.HashToken("abc"));
    }

    [Fact]
    public async Task CrearInvitacion_NoAdmin_403()
    {
        var s = await Crear();
        using var _ = s.F;
        Assert.Equal(HttpStatusCode.Forbidden, (await s.ComoBeto.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest())).StatusCode);
    }

    [Fact]
    public async Task CrearInvitacion_MiembroDestinoInvalido()
    {
        var s = await Crear();
        using var _ = s.F;
        var ajeno = await Sembrar(s.F, Guid.NewGuid(), null, "Ajeno");
        var inactivo = await Sembrar(s.F, s.Hogar, null, "Baja", activo: false);
        var c = s.ComoAna;

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(ajeno))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(s.BetoId))).StatusCode); // ya vinculado
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(inactivo))).StatusCode);
    }

    [Fact]
    public async Task Aceptar_SinMiembroDestino_CreaMiembroAdulto_YDevuelveHogar()
    {
        var s = await Crear();
        using var _ = s.F;
        var inv = await Invitar(s.ComoAna);
        var nuevo = Guid.NewGuid();

        // Sin cabecera de hogar y sin ser miembro aún.
        var r = await Con(s.F, nuevo).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, " Carla "));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await Leer<HogarResumen>(r);
        Assert.Equal(s.Hogar, h.Id);

        using var scope = s.F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var m = await db.Miembros.IgnoreQueryFilters().SingleAsync(x => x.UserId == nuevo);
        Assert.Equal("Carla", m.Nombre);
        Assert.Equal(TipoMiembro.Adulto, m.Tipo);
        Assert.Equal(RolMiembro.Miembro, m.Rol);
        Assert.Equal(s.Hogar, m.HogarId);
        var fila = await db.Invitaciones.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(nuevo, fila.UsadaPor);
        Assert.NotNull(fila.UsadaEn);
    }

    [Fact]
    public async Task Aceptar_ConMiembroDestino_VinculaUserId()
    {
        var s = await Crear();
        using var _ = s.F;
        var destino = await Sembrar(s.F, s.Hogar, null, "Carla");
        var inv = await Invitar(s.ComoAna, destino);
        var nuevo = Guid.NewGuid();

        var r = await Con(s.F, nuevo).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var scope = s.F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        Assert.Equal(nuevo, (await db.Miembros.IgnoreQueryFilters().SingleAsync(x => x.Id == destino)).UserId);
        Assert.Equal(3, await db.Miembros.IgnoreQueryFilters().CountAsync()); // no crea uno extra
    }

    [Fact]
    public async Task Aceptar_TokenUsado_409()
    {
        var s = await Crear();
        using var _ = s.F;
        var inv = await Invitar(s.ComoAna);

        Assert.Equal(HttpStatusCode.OK, (await Con(s.F, Guid.NewGuid()).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Uno"))).StatusCode);
        var otra = await Con(s.F, Guid.NewGuid()).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Dos"));

        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
    }

    [Fact]
    public async Task Aceptar_TokenCaducado_409()
    {
        var s = await Crear();
        using var _ = s.F;
        var inv = await Invitar(s.ComoAna);
        using (var scope = s.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            var fila = await db.Invitaciones.IgnoreQueryFilters().SingleAsync();
            fila.CaducaEn = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var r = await Con(s.F, Guid.NewGuid()).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Tarde"));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Aceptar_TokenInexistenteOVacio()
    {
        var s = await Crear();
        using var _ = s.F;
        var c = Con(s.F, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest("nope", "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest("  ", "X"))).StatusCode);
    }

    [Fact]
    public async Task Aceptar_UsuarioYaMiembro_409_YSinNombre_400()
    {
        var s = await Crear();
        using var _ = s.F;
        var inv = await Invitar(s.ComoAna);

        // Beto ya es miembro de ese hogar.
        Assert.Equal(HttpStatusCode.Conflict, (await Con(s.F, s.Beto).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Beto2"))).StatusCode);
        // Sin nombre cuando no hay miembro destino: 400 y la invitación sigue sin gastar.
        var nuevo = Con(s.F, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await nuevo.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nuevo.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Ok"))).StatusCode);
    }

    [Fact]
    public async Task Aceptar_MiembroDestinoYaVinculadoEnElInterin_409()
    {
        var s = await Crear();
        using var _ = s.F;
        var destino = await Sembrar(s.F, s.Hogar, null, "Carla");
        var inv = await Invitar(s.ComoAna, destino);
        using (var scope = s.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            (await db.Miembros.IgnoreQueryFilters().SingleAsync(m => m.Id == destino)).UserId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        var r = await Con(s.F, Guid.NewGuid()).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task HogarConUnSoloAdulto_EsValido()
    {
        var f = AutenticacionTests.Crear(Secreto);
        using var _ = f;
        var hogar = Guid.NewGuid();
        var user = Guid.NewGuid();
        await Sembrar(f, hogar, user, "Solo", RolMiembro.Admin);

        var lista = await Leer<List<MiembroDto>>(await Con(f, user, hogar).GetAsync("/api/miembros"));
        Assert.Single(lista);
    }
}
