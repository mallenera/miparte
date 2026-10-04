using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class CategoriasPerfilesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Contexto(WebApplicationFactory<Program> F, HttpClient C, Guid HogarId, Guid MiembroId) : IDisposable
    {
        public void Dispose() => F.Dispose();
    }

    /// <summary>Crea un hogar con un adulto y devuelve un cliente con el hogar seleccionado.</summary>
    private static async Task<Contexto> Preparar(WebApplicationFactory<Program>? f = null, Guid? user = null)
    {
        f ??= Crear(Secreto);
        var u = user ?? Guid.NewGuid();
        var sin = Cliente(f, Token(Hs256(), u));
        var r = await sin.PostAsJsonAsync("/api/hogares", new CrearHogarRequest("Casa", "Ana"));
        var hogar = (await r.Content.ReadFromJsonAsync<HogarResumen>(Web))!;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var m = await db.Miembros.IgnoreQueryFilters().SingleAsync(x => x.HogarId == hogar.Id);
        // Estos tests parten de un hogar sin categorías ni perfiles: quita la semilla de POST /api/hogares.
        db.Categorias.RemoveRange(await db.Categorias.IgnoreQueryFilters().Where(x => x.HogarId == hogar.Id).ToListAsync());
        db.PerfilesRepartoDetalle.RemoveRange(await db.PerfilesRepartoDetalle.IgnoreQueryFilters().Where(x => x.HogarId == hogar.Id).ToListAsync());
        db.PerfilesReparto.RemoveRange(await db.PerfilesReparto.IgnoreQueryFilters().Where(x => x.HogarId == hogar.Id).ToListAsync());
        await db.SaveChangesAsync();
        return new Contexto(f, Cliente(f, Token(Hs256(), u), hogar.Id), hogar.Id, m.Id);
    }

    private static async Task<Guid> AnadirMiembro(WebApplicationFactory<Program> f, Guid hogarId, bool activo = true)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var m = new Miembro { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = "Otro", Tipo = TipoMiembro.Adulto, Activo = activo };
        db.Miembros.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    private static Task<HttpResponseMessage> CrearPerfil(HttpClient c, string nombre, string modo, params PerfilDetalleDto[] detalle)
        => c.PostAsJsonAsync("/api/perfiles", new GuardarPerfilRequest(nombre, modo, detalle));

    private static async Task<Guid> IdPerfil(HttpClient c, string nombre, string modo, params PerfilDetalleDto[] detalle)
    {
        var r = await CrearPerfil(c, nombre, modo, detalle);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<PerfilRepartoDto>(Web))!.Id;
    }

    private static async Task<Guid> IdCategoria(HttpClient c, string nombre, Guid? padre = null, Guid? perfil = null)
    {
        var r = await c.PostAsJsonAsync("/api/categorias", new GuardarCategoriaRequest(nombre, padre, perfil));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<CategoriaDto>(Web))!.Id;
    }

    // ---- Perfiles ----

    [Fact]
    public async Task SinToken_401()
    {
        using var f = Crear(Secreto);
        var c = Cliente(f, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/perfiles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/categorias")).StatusCode);
    }

    [Fact]
    public async Task Perfil_HogarDeUnSoloAdulto_Porcentaje100_EsValido()
    {
        using var x = await Preparar();
        var r = await CrearPerfil(x.C, "Solo yo", "porcentaje", new PerfilDetalleDto(x.MiembroId, 100));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var p = (await r.Content.ReadFromJsonAsync<PerfilRepartoDto>(Web))!;
        Assert.Equal("porcentaje", p.Modo);
        Assert.Single(p.Detalle);

        var lista = await (await x.C.GetAsync("/api/perfiles")).Content.ReadFromJsonAsync<List<PerfilRepartoDto>>(Web);
        Assert.Equal(100m, lista!.Single().Detalle.Single().Valor);
    }

    [Fact]
    public async Task Perfil_Porcentaje_ExigeSuma100ConTolerancia()
    {
        using var x = await Preparar();
        var otro = await AnadirMiembro(x.F, x.HogarId);

        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Mal", "porcentaje",
            new PerfilDetalleDto(x.MiembroId, 60), new PerfilDetalleDto(otro, 30))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearPerfil(x.C, "Tercios", "porcentaje",
            new PerfilDetalleDto(x.MiembroId, 33.3333m), new PerfilDetalleDto(otro, 66.6667m))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearPerfil(x.C, "Casi", "porcentaje",
            new PerfilDetalleDto(x.MiembroId, 50.00005m), new PerfilDetalleDto(otro, 50))).StatusCode);
    }

    [Fact]
    public async Task Perfil_Partes_ValoresNoNegativosYAlgunoPositivo()
    {
        using var x = await Preparar();
        var otro = await AnadirMiembro(x.F, x.HogarId);

        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Neg", "partes",
            new PerfilDetalleDto(x.MiembroId, -1), new PerfilDetalleDto(otro, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Ceros", "partes",
            new PerfilDetalleDto(x.MiembroId, 0), new PerfilDetalleDto(otro, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearPerfil(x.C, "Dos a uno", "partes",
            new PerfilDetalleDto(x.MiembroId, 2), new PerfilDetalleDto(otro, 1))).StatusCode);
    }

    [Fact]
    public async Task Perfil_IngresosEIndividual_NoLlevanDetalle()
    {
        using var x = await Preparar();
        Assert.Equal(HttpStatusCode.Created, (await CrearPerfil(x.C, "Por ingresos", "ingresos")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearPerfil(x.C, "Individual", "individual")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Ing con detalle", "ingresos",
            new PerfilDetalleDto(x.MiembroId, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Ind con detalle", "individual",
            new PerfilDetalleDto(x.MiembroId, 1))).StatusCode);
    }

    [Fact]
    public async Task Perfil_Validaciones_400()
    {
        using var x = await Preparar();
        var inactivo = await AnadirMiembro(x.F, x.HogarId, activo: false);

        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "  ", "partes", new PerfilDetalleDto(x.MiembroId, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, new string('a', 101), "ingresos")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Modo raro", "otro")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Sin detalle", "partes")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Ajeno", "partes", new PerfilDetalleDto(Guid.NewGuid(), 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Inactivo", "partes",
            new PerfilDetalleDto(x.MiembroId, 1), new PerfilDetalleDto(inactivo, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(x.C, "Repetido", "partes",
            new PerfilDetalleDto(x.MiembroId, 1), new PerfilDetalleDto(x.MiembroId, 1))).StatusCode);
    }

    [Fact]
    public async Task Perfil_NombreDuplicado_409_YPutPermiteMismoNombre()
    {
        using var x = await Preparar();
        var id = await IdPerfil(x.C, "Igual", "ingresos");
        Assert.Equal(HttpStatusCode.Conflict, (await CrearPerfil(x.C, "Igual", "individual")).StatusCode);

        var otro = await IdPerfil(x.C, "Otro", "ingresos");
        // Mismo nombre que otro perfil: 409. Mismo nombre que el propio: OK.
        Assert.Equal(HttpStatusCode.Conflict, (await x.C.PutAsJsonAsync($"/api/perfiles/{otro}",
            new GuardarPerfilRequest("Igual", "ingresos", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await x.C.PutAsJsonAsync($"/api/perfiles/{id}",
            new GuardarPerfilRequest("Igual", "individual", null))).StatusCode);
    }

    [Fact]
    public async Task Perfil_Put_ReemplazaDetalle_Y404()
    {
        using var x = await Preparar();
        var otro = await AnadirMiembro(x.F, x.HogarId);
        var id = await IdPerfil(x.C, "P", "porcentaje", new PerfilDetalleDto(x.MiembroId, 100));

        var r = await x.C.PutAsJsonAsync($"/api/perfiles/{id}", new GuardarPerfilRequest("P2", "partes",
            [new PerfilDetalleDto(x.MiembroId, 1), new PerfilDetalleDto(otro, 3)]));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var p = (await r.Content.ReadFromJsonAsync<PerfilRepartoDto>(Web))!;
        Assert.Equal("P2", p.Nombre);
        Assert.Equal(2, p.Detalle.Count);

        var get = (await (await x.C.GetAsync($"/api/perfiles/{id}")).Content.ReadFromJsonAsync<PerfilRepartoDto>(Web))!;
        Assert.Equal("partes", get.Modo);
        Assert.Equal(2, get.Detalle.Count);

        Assert.Equal(HttpStatusCode.NotFound, (await x.C.PutAsJsonAsync($"/api/perfiles/{Guid.NewGuid()}",
            new GuardarPerfilRequest("Z", "ingresos", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"/api/perfiles/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Perfil_Delete_204_404_Y409SiLoUsanCategoriaOGasto()
    {
        using var x = await Preparar();
        var libre = await IdPerfil(x.C, "Libre", "ingresos");
        Assert.Equal(HttpStatusCode.NoContent, (await x.C.DeleteAsync($"/api/perfiles/{libre}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.DeleteAsync($"/api/perfiles/{libre}")).StatusCode);

        var conCat = await IdPerfil(x.C, "ConCat", "ingresos");
        await IdCategoria(x.C, "Luz", perfil: conCat);
        Assert.Equal(HttpStatusCode.Conflict, (await x.C.DeleteAsync($"/api/perfiles/{conCat}")).StatusCode);

        var conGasto = await IdPerfil(x.C, "ConGasto", "individual");
        using (var scope = x.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Gastos.Add(new Gasto
            {
                Id = Guid.NewGuid(), HogarId = x.HogarId, Fecha = new DateOnly(2026, 10, 1), Importe = 10,
                CategoriaId = Guid.NewGuid(), PagadoPor = x.MiembroId, PerfilRepartoId = conGasto,
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await x.C.DeleteAsync($"/api/perfiles/{conGasto}")).StatusCode);
    }

    // ---- Categorías ----

    [Fact]
    public async Task Categoria_CrudBasico()
    {
        using var x = await Preparar();
        var perfil = await IdPerfil(x.C, "Ingresos", "ingresos");
        var padre = await IdCategoria(x.C, "Hogar");
        var hija = await IdCategoria(x.C, "Luz", padre, perfil);

        var lista = await (await x.C.GetAsync("/api/categorias")).Content.ReadFromJsonAsync<List<CategoriaDto>>(Web);
        Assert.Equal(["Hogar", "Luz"], lista!.Select(c => c.Nombre));
        Assert.Equal(padre, lista![1].CategoriaPadreId);
        Assert.Equal(perfil, lista[1].PerfilRepartoId);

        var put = await x.C.PutAsJsonAsync($"/api/categorias/{hija}", new GuardarCategoriaRequest("Electricidad", padre, null));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var dto = (await put.Content.ReadFromJsonAsync<CategoriaDto>(Web))!;
        Assert.Equal("Electricidad", dto.Nombre);
        Assert.Null(dto.PerfilRepartoId);

        Assert.Equal(HttpStatusCode.NoContent, (await x.C.DeleteAsync($"/api/categorias/{hija}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.DeleteAsync($"/api/categorias/{hija}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.PutAsJsonAsync($"/api/categorias/{hija}",
            new GuardarCategoriaRequest("X", null, null))).StatusCode);
    }

    [Fact]
    public async Task Categoria_Validaciones_400()
    {
        using var x = await Preparar();
        Task<HttpResponseMessage> Post(string n, Guid? padre = null, Guid? perfil = null)
            => x.C.PostAsJsonAsync("/api/categorias", new GuardarCategoriaRequest(n, padre, perfil));

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(" ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new string('a', 101))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("Hija", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("ConPerfil", perfil: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(new string('a', 100))).StatusCode);
    }

    [Fact]
    public async Task Categoria_NombreDuplicadoEnMismoNivel_409_PeroPermitidoEnOtroNivel()
    {
        using var x = await Preparar();
        var padre = await IdCategoria(x.C, "Hogar");
        await IdCategoria(x.C, "Luz", padre);

        var dup = await x.C.PostAsJsonAsync("/api/categorias", new GuardarCategoriaRequest("Luz", padre, null));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await x.C.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("Hogar", null, null))).StatusCode); // raíz: padre null cuenta como igual
        Assert.Equal(HttpStatusCode.Created, (await x.C.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("Luz", null, null))).StatusCode);
    }

    [Fact]
    public async Task Categoria_SinCiclos()
    {
        using var x = await Preparar();
        var a = await IdCategoria(x.C, "A");
        var b = await IdCategoria(x.C, "B", a);
        var c = await IdCategoria(x.C, "C", b);

        Task<HttpResponseMessage> Put(Guid id, string n, Guid? padre)
            => x.C.PutAsJsonAsync($"/api/categorias/{id}", new GuardarCategoriaRequest(n, padre, null));

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(a, "A", a)).StatusCode);   // propio padre
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(a, "A", c)).StatusCode);   // A -> C -> B -> A
        Assert.Equal(HttpStatusCode.OK, (await Put(c, "C", a)).StatusCode);           // reubicar sin ciclo
    }

    [Fact]
    public async Task Categoria_Delete_409ConSubcategoriasOGastos()
    {
        using var x = await Preparar();
        var padre = await IdCategoria(x.C, "Padre");
        await IdCategoria(x.C, "Hija", padre);
        Assert.Equal(HttpStatusCode.Conflict, (await x.C.DeleteAsync($"/api/categorias/{padre}")).StatusCode);

        var conGasto = await IdCategoria(x.C, "Compra");
        var perfil = await IdPerfil(x.C, "Ind", "individual");
        using (var scope = x.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Gastos.Add(new Gasto
            {
                Id = Guid.NewGuid(), HogarId = x.HogarId, Fecha = new DateOnly(2026, 10, 1), Importe = 5,
                CategoriaId = conGasto, PagadoPor = x.MiembroId, PerfilRepartoId = perfil,
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await x.C.DeleteAsync($"/api/categorias/{conGasto}")).StatusCode);
    }

    // ---- Aislamiento ----

    [Fact]
    public async Task OtroHogar_NoVeNiToca_CategoriasNiPerfiles_YNoPuedeReferenciarlos()
    {
        using var a = await Preparar();
        using var b = await Preparar(a.F);
        var perfilA = await IdPerfil(a.C, "PA", "ingresos");
        var catA = await IdCategoria(a.C, "CA");

        Assert.Empty((await (await b.C.GetAsync("/api/perfiles")).Content.ReadFromJsonAsync<List<PerfilRepartoDto>>(Web))!);
        Assert.Empty((await (await b.C.GetAsync("/api/categorias")).Content.ReadFromJsonAsync<List<CategoriaDto>>(Web))!);
        Assert.Equal(HttpStatusCode.NotFound, (await b.C.DeleteAsync($"/api/perfiles/{perfilA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.C.DeleteAsync($"/api/categorias/{catA}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.C.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("X", catA, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.C.PostAsJsonAsync("/api/categorias",
            new GuardarCategoriaRequest("Y", null, perfilA))).StatusCode);
        // Miembro de otro hogar en el detalle.
        Assert.Equal(HttpStatusCode.BadRequest, (await CrearPerfil(b.C, "Z", "partes",
            new PerfilDetalleDto(a.MiembroId, 1))).StatusCode);
        // Mismo nombre en hogares distintos es válido.
        Assert.Equal(HttpStatusCode.Created, (await CrearPerfil(b.C, "PA", "ingresos")).StatusCode);
    }
}
