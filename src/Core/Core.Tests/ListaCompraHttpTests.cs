using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Cableado HTTP de la lista de la compra: permisos, rutas públicas por enlace QR, aislamiento entre hogares y QR. Sin PostgreSQL:
/// la API arranca sobre la base en memoria de las demás pruebas de permisos.
/// </summary>
public class ListaCompraHttpTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Escenario(
        WebApplicationFactory<Program> Fabrica, HttpClient Admin, HttpClient Miembro, HttpClient Anonimo, Guid Hogar, Guid AdminId);

    /// <summary>
    /// Hogar con la lista de la compra activada, un admin (Ana) y un miembro (Beto) con exactamente los permisos indicados.
    /// </summary>
    private static async Task<Escenario> Montar(bool activa = true, params string[] permisosDeBeto)
    {
        var f = Crear(Secreto);
        var (userAna, userBeto) = (Guid.NewGuid(), Guid.NewGuid());
        var (hogar, ana, beto) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa", ListaCompraActiva = activa });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = userAna, Rol = RolMiembro.Admin });
            db.Miembros.Add(new Miembro
            {
                Id = beto, HogarId = hogar, Nombre = "Beto", Tipo = TipoMiembro.Adulto, UserId = userBeto, Rol = RolMiembro.Miembro,
                Permisos = [.. permisosDeBeto],
            });
            await db.SaveChangesAsync();
        }
        return new Escenario(f, Cliente(f, Token(Hs256(), userAna)), Cliente(f, Token(Hs256(), userBeto)), Cliente(f, null), hogar, ana);
    }

    /// <summary>Segundo hogar en la misma aplicación, con su admin, para comprobar el aislamiento.</summary>
    private static async Task<HttpClient> OtroHogarAsync(WebApplicationFactory<Program> f)
    {
        var (user, hogar, miembro) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Otra casa", ListaCompraActiva = true });
        db.Miembros.Add(new Miembro { Id = miembro, HogarId = hogar, Nombre = "Eva", Tipo = TipoMiembro.Adulto, UserId = user, Rol = RolMiembro.Admin });
        await db.SaveChangesAsync();
        return Cliente(f, Token(Hs256(), user));
    }

    private static async Task<T> Leer<T>(HttpResponseMessage r, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        Assert.Equal(esperado, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<T>(Web))!;
    }

    /// <summary>Petición sin sesión del enlace QR; el token va en la cabecera <c>X-Enlace-Lista</c> si se indica.</summary>
    private static Task<HttpResponseMessage> Publica(HttpClient c, HttpMethod metodo, string ruta, string? token, object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta);
        if (token is not null) peticion.Headers.Add("X-Enlace-Lista", token);
        if (cuerpo is not null) peticion.Content = JsonContent.Create(cuerpo);
        return c.SendAsync(peticion);
    }

    private static async Task<EnlaceListaCreado> CrearEnlace(HttpClient c)
        => await Leer<EnlaceListaCreado>(await c.PostAsync("/api/lista-compra/enlaces", null), HttpStatusCode.Created);

    [Fact]
    public async Task Productos_SinElPermisoListaCompra_Responden403_YConElPermiso201()
    {
        var sin = await Montar(true);
        var con = await Montar(true, CatalogoPermisos.ListaCompra);

        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Miembro.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await con.Miembro.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche"))).StatusCode);
    }

    [Fact]
    public async Task Enlaces_SinElPermisoListaCompra_Responden403()
    {
        var e = await Montar(true);

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PostAsync("/api/lista-compra/enlaces", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.DeleteAsync($"/api/lista-compra/enlaces/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Activacion_ExigeHogarFunciones_YSeVeEnLaLista()
    {
        var e = await Montar(activa: false, CatalogoPermisos.ListaCompra);

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync("/api/lista-compra/activacion", new ActivarListaCompraRequest(true))).StatusCode);
        Assert.False((await Leer<ListaCompraResponse>(await e.Admin.GetAsync("/api/lista-compra"))).Activa);

        Assert.Equal(HttpStatusCode.OK, (await e.Admin.PutAsJsonAsync("/api/lista-compra/activacion", new ActivarListaCompraRequest(true))).StatusCode);

        Assert.True((await Leer<ListaCompraResponse>(await e.Admin.GetAsync("/api/lista-compra"))).Activa);
    }

    [Fact]
    public async Task YoDevuelveLaActivacionDeLaListaEnElHogar()
    {
        var e = await Montar(true);

        var yo = await Leer<YoResponse>(await e.Admin.GetAsync("/api/yo"));

        Assert.True(yo.Hogares.Single().ListaCompraActiva);
    }

    [Fact]
    public async Task ListaDesactivada_NoAdmiteProductosNiEnlaces_PeroSeLee()
    {
        var e = await Montar(activa: false);

        Assert.Equal(HttpStatusCode.Conflict, (await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Admin.PostAsync("/api/lista-compra/enlaces", null)).StatusCode);
        Assert.Empty((await Leer<ListaCompraResponse>(await e.Admin.GetAsync("/api/lista-compra"))).Productos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Producto_SinNombre_Es400(string nombre)
    {
        var e = await Montar(true);

        var r = await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest(nombre));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task Producto_ConCantidadFueraDeRango_Es400(int cantidad)
    {
        var e = await Montar(true);

        var r = await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche", cantidad));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Producto_SinCantidad_SeGuardaConUno()
    {
        var e = await Montar(true);

        var p = await Leer<ProductoCompraDto>(await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Pan")), HttpStatusCode.Created);

        Assert.Equal(1, p.Cantidad);
        Assert.False(p.Comprado);
    }

    [Fact]
    public async Task MarcarYBorrar_ActuanSoloSobreProductosDelHogar()
    {
        var e = await Montar(true);
        var otro = await OtroHogarAsync(e.Fabrica);
        var ajeno = await Leer<ProductoCompraDto>(await otro.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Ajeno")), HttpStatusCode.Created);
        var propio = await Leer<ProductoCompraDto>(await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Mío")), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.NotFound, (await e.Admin.PutAsJsonAsync($"/api/lista-compra/productos/{ajeno.Id}", new MarcarProductoCompraRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Admin.DeleteAsync($"/api/lista-compra/productos/{ajeno.Id}")).StatusCode);

        var marcado = await Leer<ProductoCompraDto>(await e.Admin.PutAsJsonAsync($"/api/lista-compra/productos/{propio.Id}", new MarcarProductoCompraRequest(true)));
        Assert.True(marcado.Comprado);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Admin.DeleteAsync($"/api/lista-compra/productos/{propio.Id}")).StatusCode);
        Assert.Empty((await Leer<ListaCompraResponse>(await e.Admin.GetAsync("/api/lista-compra"))).Productos);
    }

    [Fact]
    public async Task Lista_OrdenaLosPendientesAntesQueLosComprados()
    {
        var e = await Montar(true);
        var primero = await Leer<ProductoCompraDto>(await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Primero")), HttpStatusCode.Created);
        await Leer<ProductoCompraDto>(await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Segundo")), HttpStatusCode.Created);
        await Leer<ProductoCompraDto>(await e.Admin.PutAsJsonAsync($"/api/lista-compra/productos/{primero.Id}", new MarcarProductoCompraRequest(true)));

        var lista = await Leer<ListaCompraResponse>(await e.Admin.GetAsync("/api/lista-compra"));

        Assert.Equal(["Segundo", "Primero"], lista.Productos.Select(p => p.Nombre));
    }

    [Fact]
    public async Task Publica_SinCabeceraOConTokenDesconocido_Responde404()
    {
        var e = await Montar(true);

        Assert.Equal(HttpStatusCode.NotFound, (await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", "no-existe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Publica(e.Anonimo, HttpMethod.Post, "/api/lista-compra/publica/productos", "no-existe", new CrearProductoCompraRequest("Leche"))).StatusCode);
    }

    [Fact]
    public async Task Publica_SinSesion_VeAnadeYMarca_SoloEnSuHogar()
    {
        var e = await Montar(true);
        var otro = await OtroHogarAsync(e.Fabrica);
        var propio = await Leer<ProductoCompraDto>(await e.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche", 2)), HttpStatusCode.Created);
        var ajeno = await Leer<ProductoCompraDto>(await otro.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Ajeno")), HttpStatusCode.Created);
        var enlace = await CrearEnlace(e.Admin);

        var vista = await Leer<ListaCompraResponse>(await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", enlace.Token));
        Assert.Equal(["Leche"], vista.Productos.Select(p => p.Nombre));

        var nuevo = await Leer<ProductoCompraDto>(
            await Publica(e.Anonimo, HttpMethod.Post, "/api/lista-compra/publica/productos", enlace.Token, new CrearProductoCompraRequest("Pan", 3)),
            HttpStatusCode.Created);
        Assert.Equal(3, nuevo.Cantidad);

        var marcado = await Leer<ProductoCompraDto>(
            await Publica(e.Anonimo, HttpMethod.Put, $"/api/lista-compra/publica/productos/{propio.Id}", enlace.Token, new MarcarProductoCompraRequest(true)));
        Assert.True(marcado.Comprado);

        // Con el enlace de un hogar no se toca nada de otro.
        Assert.Equal(HttpStatusCode.NotFound,
            (await Publica(e.Anonimo, HttpMethod.Put, $"/api/lista-compra/publica/productos/{ajeno.Id}", enlace.Token, new MarcarProductoCompraRequest(true))).StatusCode);
        var delOtro = await Leer<ListaCompraResponse>(await otro.GetAsync("/api/lista-compra"));
        Assert.False(delOtro.Productos.Single().Comprado);
        Assert.Equal(2, (await Leer<ListaCompraResponse>(await e.Admin.GetAsync("/api/lista-compra"))).Productos.Count);
    }

    [Fact]
    public async Task Publica_ValidaNombreYCantidad()
    {
        var e = await Montar(true);
        var enlace = await CrearEnlace(e.Admin);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Publica(e.Anonimo, HttpMethod.Post, "/api/lista-compra/publica/productos", enlace.Token, new CrearProductoCompraRequest(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Publica(e.Anonimo, HttpMethod.Post, "/api/lista-compra/publica/productos", enlace.Token, new CrearProductoCompraRequest("Leche", 0))).StatusCode);
    }

    [Fact]
    public async Task Publica_ConElEnlaceRevocado_Responde409_ConLaListaDesactivada_Tambien()
    {
        var e = await Montar(true);
        var enlace = await CrearEnlace(e.Admin);
        var otroEnlace = await CrearEnlace(e.Admin);

        Assert.Equal(HttpStatusCode.NoContent, (await e.Admin.DeleteAsync($"/api/lista-compra/enlaces/{enlace.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", enlace.Token)).StatusCode);
        // Revocar uno no afecta a los demás.
        Assert.Equal(HttpStatusCode.OK, (await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", otroEnlace.Token)).StatusCode);

        await e.Admin.PutAsJsonAsync("/api/lista-compra/activacion", new ActivarListaCompraRequest(false));
        Assert.Equal(HttpStatusCode.Conflict, (await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", otroEnlace.Token)).StatusCode);
    }

    [Fact]
    public async Task Enlace_NuevoCaduca_ASieteDias_YElTokenNoSeGuardaEnClaro()
    {
        var e = await Montar(true);

        var enlace = await CrearEnlace(e.Admin);

        Assert.InRange(enlace.CaducaEn - DateTimeOffset.UtcNow, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7.1));
        using var scope = e.Fabrica.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var guardado = db.EnlacesListaCompra.IgnoreQueryFilters().Single(x => x.Id == enlace.Id);
        Assert.NotEqual(enlace.Token, guardado.TokenHash);
        Assert.Equal(TokenInvitacion.Hash(enlace.Token), guardado.TokenHash);
    }

    [Fact]
    public async Task Revocar_UnEnlaceDeOtroHogar_Responde404()
    {
        var e = await Montar(true);
        var otro = await OtroHogarAsync(e.Fabrica);
        var ajeno = await CrearEnlace(otro);

        var r = await e.Admin.DeleteAsync($"/api/lista-compra/enlaces/{ajeno.Id}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Publica(e.Anonimo, HttpMethod.Get, "/api/lista-compra/publica", ajeno.Token)).StatusCode);
    }

    [Fact]
    public async Task Qr_DevuelveUnSvgSinEstilosNiScripts_YValidaElTexto()
    {
        var e = await Montar(true);

        var qr = await Leer<QrListaResponse>(await e.Admin.PostAsJsonAsync("/api/lista-compra/qr", new QrListaRequest("https://app.test/compra/abc")));

        Assert.Contains("<svg", qr.Svg);
        // Sin viewBox el SVG no se escala al ancho de la tarjeta y se recorta (el QR dejaría de poder escanearse).
        Assert.Contains("viewBox=", qr.Svg);
        Assert.DoesNotContain(" width=\"", qr.Svg.Split('>')[0], StringComparison.Ordinal);
        // La CSP del front prohíbe atributos style y scripts: el QR se inserta tal cual en la página.
        Assert.DoesNotContain("style=", qr.Svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", qr.Svg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Admin.PostAsJsonAsync("/api/lista-compra/qr", new QrListaRequest(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Admin.PostAsJsonAsync("/api/lista-compra/qr", new QrListaRequest(new string('a', 501)))).StatusCode);
    }

    [Fact]
    public async Task RutasPrivadas_SinSesion_Responden401()
    {
        var e = await Montar(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Anonimo.GetAsync("/api/lista-compra")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Anonimo.PostAsync("/api/lista-compra/enlaces", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Anonimo.PostAsJsonAsync("/api/lista-compra/qr", new QrListaRequest("x"))).StatusCode);
    }
}
