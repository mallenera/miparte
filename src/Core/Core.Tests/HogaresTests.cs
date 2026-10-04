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
    private sealed record Yo(string? UserId, Guid? HogarId);

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
        Assert.Equal(creado.Id, yo!.HogarId);

        using var scope = f.Services.CreateScope();
        var m = await scope.ServiceProvider.GetRequiredService<MiParteDbContext>()
            .Miembros.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(user, m.UserId);
        Assert.Equal(TipoMiembro.Adulto, m.Tipo);
        Assert.Equal(creado.Id, m.HogarId);
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

        // ...pero el resto sí: hay que elegir.
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/yo")).StatusCode);
        var elegido = lista![0].Id;
        var cc = ConToken(f, Guid.Parse((await ObtenerSub(c))), elegido);
        Assert.Equal(HttpStatusCode.OK, (await cc.GetAsync("/api/yo")).StatusCode);
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
}
