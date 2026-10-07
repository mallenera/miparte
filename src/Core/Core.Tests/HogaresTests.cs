using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class HogaresTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private sealed record Yo(string? UserId, List<HogarResumen> Hogares, HogarResumen? HogarActual);

    private static HttpClient ConToken(WebApplicationFactory<Program> f, Guid user, Guid? hogar = null)
        => Cliente(f, Token(Hs256(), user), hogar);

    [Fact]
    public async Task UsuarioNuevo_PuedeCrearSuPrimerHogar_YQuedaComoMiembro()
    {
        using var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var c = ConToken(f, user);

        var r = await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Casa", "Ana"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var creado = await r.Content.ReadFromJsonAsync<HogarResumen>(Web);
        Assert.Equal("Casa", creado!.Nombre);

        // Ya tiene hogar: /api/yo lo resuelve sin cabecera.
        var yo = await (await c.GetAsync("/api/yo")).Content.ReadFromJsonAsync<Yo>(Web);
        Assert.Equal(creado.Id, yo!.HogarActual!.Id);

        using var scope = f.Services.CreateScope();
        var m = await scope.ServiceProvider.GetRequiredService<MiParteDbContext>()
            .Miembros.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(user, m.UserId);
        Assert.Equal(TipoMiembro.Adulto, m.Tipo);
        Assert.Equal(creado.Id, m.HogarId);
    }

    [Fact]
    public async Task CrearHogar_SiembraPerfilesCategoriasYDetalleDelCreador_ComoAdmin()
    {
        using var f = Crear(Secreto);
        var c = ConToken(f, Guid.NewGuid());
        var creado = await (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Casa", "Ana")))
            .Content.ReadFromJsonAsync<HogarResumen>(Web);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var miembro = await db.Miembros.IgnoreQueryFilters().SingleAsync(m => m.HogarId == creado!.Id);
        Assert.Equal(RolMiembro.Admin, miembro.Rol);

        var perfiles = await db.PerfilesReparto.IgnoreQueryFilters().Where(p => p.HogarId == creado!.Id).ToListAsync();
        Assert.Equal(4, perfiles.Count);
        var porNombre = perfiles.ToDictionary(p => p.Nombre);
        Assert.Equal(ModoReparto.CuentaComun, porNombre[SemillaHogar.PerfilCuentaComun].Modo);
        Assert.Equal(ModoReparto.Partes, porNombre[SemillaHogar.PerfilPartes].Modo);
        Assert.Equal(ModoReparto.Porcentaje, porNombre[SemillaHogar.PerfilPorcentaje].Modo);
        Assert.Equal(ModoReparto.Individual, porNombre[SemillaHogar.PerfilIndividual].Modo);

        var categorias = await db.Categorias.IgnoreQueryFilters().Where(x => x.HogarId == creado!.Id).ToListAsync();
        Assert.Equal(6, categorias.Count);
        foreach (var (nombre, perfil) in SemillaHogar.Categorias)
            Assert.Equal(porNombre[perfil].Id, categorias.Single(x => x.Nombre == nombre).PerfilRepartoId);
        Assert.Equal(5, categorias.Count(x => x.PerfilRepartoId == porNombre[SemillaHogar.PerfilPartes].Id));

        var detalles = await db.PerfilesRepartoDetalle.IgnoreQueryFilters().Where(d => d.HogarId == creado!.Id).ToListAsync();
        Assert.Equal(2, detalles.Count);
        Assert.All(detalles, d => Assert.Equal(miembro.Id, d.MiembroId));
        Assert.Equal(1m, detalles.Single(d => d.PerfilId == porNombre[SemillaHogar.PerfilPartes].Id).Valor);
        Assert.Equal(100m, detalles.Single(d => d.PerfilId == porNombre[SemillaHogar.PerfilPorcentaje].Id).Valor);
    }

    [Fact]
    public async Task UsuarioPuedeCrearVariosHogares_YListarlosSinCabecera()
    {
        using var f = Crear(Secreto);
        var c = ConToken(f, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Casa", "Ana"))).StatusCode);
        // Con un hogar, crear otro sigue siendo posible aunque no haya cabecera.
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Piso de la playa", "Ana"))).StatusCode);

        // Con dos hogares, los endpoints de hogares no exigen seleccionar uno...
        var lista = await (await c.GetAsync("/api/hogares")).Content.ReadFromJsonAsync<List<HogarResumen>>(Web);
        Assert.Equal(["Casa", "Piso de la playa"], lista!.Select(h => h.Nombre));
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Tercero", "Ana"))).StatusCode);

        // ...y /api/yo tampoco (lista hogares, hogar actual null); el resto sí exige elegir (ver AutenticacionTests).
        var yoSin = await (await c.GetAsync("/api/yo")).Content.ReadFromJsonAsync<Yo>(Web);
        Assert.Equal(3, yoSin!.Hogares.Count);
        Assert.Null(yoSin.HogarActual);
        var elegido = lista![0].Id;
        var cc = ConToken(f, Guid.Parse((await ObtenerSub(c))), elegido);
        var yoCon = await (await cc.GetAsync("/api/yo")).Content.ReadFromJsonAsync<Yo>(Web);
        Assert.Equal(elegido, yoCon!.HogarActual!.Id);
    }

    private static Task<string> ObtenerSub(HttpClient c)
    {
        var token = c.DefaultRequestHeaders.Authorization!.Parameter!;
        var sub = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).Subject;
        return Task.FromResult(sub);
    }

    [Fact]
    public async Task ListaSoloLosHogaresDelUsuario()
    {
        using var f = Crear(Secreto);
        var ana = ConToken(f, Guid.NewGuid());
        var beto = ConToken(f, Guid.NewGuid());
        await ana.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("De Ana", "Ana"));
        await beto.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("De Beto", "Beto"));

        var lista = await (await ana.GetAsync("/api/hogares")).Content.ReadFromJsonAsync<List<HogarResumen>>(Web);

        Assert.Equal("De Ana", Assert.Single(lista!).Nombre);
    }

    [Theory]
    [InlineData("", "Ana")]
    [InlineData("Casa", "   ")]
    public async Task NombresVacios_400(string hogar, string miembro)
    {
        using var f = Crear(Secreto);
        var r = await ConToken(f, Guid.NewGuid()).PostAsJsonAsync("/api/hogares", new CrearHogarRequest(hogar, miembro));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task NombreDemasiadoLargo_400()
    {
        using var f = Crear(Secreto);
        var r = await ConToken(f, Guid.NewGuid()).PostAsJsonAsync("/api/hogares", new CrearHogarRequest(new string('x', 101), "Ana"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task SinToken_401()
    {
        using var f = Crear(Secreto);
        var anonimo = Cliente(f, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Casa", "Ana"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/hogares")).StatusCode);
    }

    [Fact]
    public async Task MaximoDeHogaresPorUsuario_409()
    {
        using var f = Crear(Secreto);
        var c = ConToken(f, Guid.NewGuid());
        for (var i = 0; i < HogaresEndpoints.MaxHogaresPorUsuario; i++)
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest($"H{i}", "Ana"))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Uno más", "Ana"))).StatusCode);
    }

    [Fact]
    public async Task Post_DevuelveLocationQueApuntaAGetPorId()
    {
        using var f = Crear(Secreto);
        var c = ConToken(f, Guid.NewGuid());

        var r = await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Casa", "Ana"));
        var creado = await r.Content.ReadFromJsonAsync<HogarResumen>(Web);
        var get = await c.GetAsync(r.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(creado, await get.Content.ReadFromJsonAsync<HogarResumen>(Web));
    }

    [Fact]
    public async Task GetPorId_HogarAjenoOInexistente_404()
    {
        using var f = Crear(Secreto);
        var ana = ConToken(f, Guid.NewGuid());
        var beto = ConToken(f, Guid.NewGuid());
        var deAna = await (await ana.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("De Ana", "Ana")))
            .Content.ReadFromJsonAsync<HogarResumen>(Web);

        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync($"/api/hogares/{deAna!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync($"/api/hogares/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task GetPorId_SinToken_401_YFuncionaConVariosHogaresSinCabecera()
    {
        using var f = Crear(Secreto);
        var c = ConToken(f, Guid.NewGuid());
        var a = await (await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("A", "Ana"))).Content.ReadFromJsonAsync<HogarResumen>(Web);
        await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("B", "Ana"));

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/hogares/{a!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, null).GetAsync($"/api/hogares/{a.Id}")).StatusCode);
    }

    [Fact]
    public async Task Yo_CabeceraInvalidaOAjena_HogarActualNull_SinError()
    {
        using var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var c = ConToken(f, user);
        await c.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("A", "Ana"));

        var ajeno = await ConToken(f, user, Guid.NewGuid()).GetAsync("/api/yo");
        Assert.Equal(HttpStatusCode.OK, ajeno.StatusCode);
        var yo = await ajeno.Content.ReadFromJsonAsync<Yo>(Web);
        Assert.Null(yo!.HogarActual);
        Assert.Single(yo.Hogares);
    }
}
