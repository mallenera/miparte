using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// El acceso (miembros activos y permisos de cada usuario) se cachea para no consultarlo en cada petición. Estas pruebas
/// fijan lo que no puede ocurrir por culpa de la caché: que un permiso retirado o una baja sigan valiendo tras el cambio.
/// </summary>
public class AccesoCacheadoTests
{
    private sealed record Escenario(WebApplicationFactory<Program> F, Guid Hogar, Guid Ana, Guid Beto, Guid BetoId)
    {
        public HttpClient ComoAna => Cliente(F, Token(Hs256(), Ana), Hogar);
        public HttpClient ComoBeto => Cliente(F, Token(Hs256(), Beto), Hogar);
    }

    private static async Task<Guid> Sembrar(WebApplicationFactory<Program> f, Guid hogar, Guid user, string nombre, RolMiembro rol)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        if (!await db.Hogares.IgnoreQueryFilters().AnyAsync(h => h.Id == hogar)) db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa" });
        var id = Guid.NewGuid();
        db.Miembros.Add(new Miembro { Id = id, HogarId = hogar, Nombre = nombre, Tipo = TipoMiembro.Adulto, UserId = user, Rol = rol });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Escenario> Crear()
    {
        var f = AutenticacionTests.Crear(Secreto);
        var hogar = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        await Sembrar(f, hogar, ana, "Ana", RolMiembro.Admin);
        var betoId = await Sembrar(f, hogar, beto, "Beto", RolMiembro.Miembro);
        return new Escenario(f, hogar, ana, beto, betoId);
    }

    /// <summary>POST de un gasto vacío: el filtro de permisos responde antes que la validación, así que 403 = sin permiso y 400 = con permiso.</summary>
    private static async Task<HttpStatusCode> IntentarCrearGasto(HttpClient c)
        => (await c.PostAsJsonAsync("/api/gastos", new GastoRequest(default, 0m, Guid.Empty, null, Guid.Empty, null))).StatusCode;

    [Fact]
    public async Task Quitar_un_permiso_surte_efecto_en_la_siguiente_peticion_aunque_estuviera_cacheado()
    {
        var s = await Crear();
        using var _ = s.F;
        Assert.Equal(HttpStatusCode.BadRequest, await IntentarCrearGasto(s.ComoBeto)); // permiso por defecto; queda cacheado

        var quitar = await s.ComoAna.PutAsJsonAsync($"/api/miembros/{s.BetoId}",
            new ActualizarMiembroRequest(Permisos: CatalogoPermisos.Todos.Select(p => p.Clave).Where(c => c != CatalogoPermisos.GastosCrear).ToList()));
        Assert.Equal(HttpStatusCode.OK, quitar.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, await IntentarCrearGasto(s.ComoBeto));
    }

    [Fact]
    public async Task Desactivar_a_un_miembro_le_corta_el_acceso_en_la_siguiente_peticion()
    {
        var s = await Crear();
        using var _ = s.F;
        Assert.Equal(HttpStatusCode.OK, (await s.ComoBeto.GetAsync("/api/miembros")).StatusCode); // queda cacheado

        var baja = await s.ComoAna.PutAsJsonAsync($"/api/miembros/{s.BetoId}", new ActualizarMiembroRequest(Activo: false));
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);

        Assert.NotEqual(HttpStatusCode.OK, (await s.ComoBeto.GetAsync("/api/miembros")).StatusCode);
    }

    [Fact]
    public async Task Quien_acepta_una_invitacion_ve_el_hogar_sin_esperar_a_que_caduque_la_cache()
    {
        var s = await Crear();
        using var _ = s.F;
        var nuevo = Guid.NewGuid();
        var comoNuevo = Cliente(s.F, Token(Hs256(), nuevo), null);
        Assert.Empty((await comoNuevo.GetFromJsonAsync<List<MiembroDto>>("/api/miembros"))!); // sin hogares: queda cacheado vacío

        var invitacion = await s.ComoAna.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest());
        var token = (await invitacion.Content.ReadFromJsonAsync<InvitacionCreada>())!.Token;
        var acepta = await comoNuevo.PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(token, "Carla"));
        Assert.Equal(HttpStatusCode.OK, acepta.StatusCode);

        Assert.Equal(3, (await comoNuevo.GetFromJsonAsync<List<MiembroDto>>("/api/miembros"))!.Count);
    }

    [Fact]
    public async Task Un_cambio_hecho_directamente_en_la_base_de_datos_no_se_ve_hasta_que_caduca_la_cache()
    {
        var s = await Crear();
        using var _ = s.F;
        Assert.Equal(HttpStatusCode.OK, (await s.ComoBeto.GetAsync("/api/miembros")).StatusCode);

        using (var scope = s.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            var beto = await db.Miembros.IgnoreQueryFilters().FirstAsync(m => m.Id == s.BetoId);
            beto.Activo = false;
            await db.SaveChangesAsync();
        }

        // Es el desfase acotado (Vigencia) que se acepta a cambio de no consultar en cada petición.
        Assert.Equal(HttpStatusCode.OK, (await s.ComoBeto.GetAsync("/api/miembros")).StatusCode);
    }
}
