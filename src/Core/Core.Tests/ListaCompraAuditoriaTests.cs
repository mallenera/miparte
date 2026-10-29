using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Infrastructure.Auditoria;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.ApiPostgresTests;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>El historial recoge los cambios de la lista de la compra y de sus enlaces QR, sin ruido y sin secretos.</summary>
public class ListaCompraAuditoriaTests
{
    private sealed record Contexto(WebApplicationFactory<Program> F, HttpClient Admin, HttpClient Anonimo);

    /// <summary>Hogar creado por la API (InMemory) con la lista de la compra activada.</summary>
    private static async Task<Contexto> Montar()
    {
        var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var hogar = await CrearHogar(Cliente(f, Token(Hs256(), user)), "Casa", "Ana");
        var c = Cliente(f, Token(Hs256(), user), hogar.Id);
        await Leer<JsonElement>(await c.PutAsJsonAsync("/api/lista-compra/activacion", new ActivarListaCompraRequest(true)));
        return new Contexto(f, c, Cliente(f, null));
    }

    private static async Task<List<EventoAuditoriaDto>> Eventos(HttpClient c, string entidad)
        => await Leer<List<EventoAuditoriaDto>>(await c.GetAsync($"/api/auditoria?entidad={entidad}"));

    private static Task<HttpResponseMessage> Publica(HttpClient c, HttpMethod metodo, string ruta, string token, object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta);
        peticion.Headers.Add("X-Enlace-Lista", token);
        if (cuerpo is not null) peticion.Content = JsonContent.Create(cuerpo);
        return c.SendAsync(peticion);
    }

    [Fact]
    public async Task ActivarLaLista_QuedaEnElHistorialDelHogar()
    {
        var x = await Montar();

        var evento = Assert.Single(await Eventos(x.Admin, "hogar"), e => e.Accion == "editar");

        Assert.True(evento.Despues!.Value.GetProperty("listaCompraActiva").GetBoolean());
        Assert.False(evento.Antes!.Value.GetProperty("listaCompraActiva").GetBoolean());
    }

    [Fact]
    public async Task AnadirYBorrarUnProducto_SeRegistran_PeroMarcarloComoCompradoNo()
    {
        var x = await Montar();
        var producto = await Leer<ProductoCompraDto>(
            await x.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche", 2)), HttpStatusCode.Created);

        await Leer<ProductoCompraDto>(await x.Admin.PutAsJsonAsync($"/api/lista-compra/productos/{producto.Id}", new MarcarProductoCompraRequest(true)));
        await Leer<ProductoCompraDto>(await x.Admin.PutAsJsonAsync($"/api/lista-compra/productos/{producto.Id}", new MarcarProductoCompraRequest(false)));
        Assert.Equal(HttpStatusCode.NoContent, (await x.Admin.DeleteAsync($"/api/lista-compra/productos/{producto.Id}")).StatusCode);

        var eventos = await Eventos(x.Admin, "producto_compra");
        Assert.Equal(["crear", "borrar"], eventos.OrderBy(e => e.Cuando).Select(e => e.Accion));
        var alta = eventos.Single(e => e.Accion == "crear");
        Assert.Equal("Leche", alta.Despues!.Value.GetProperty("nombre").GetString());
        Assert.Equal(2, alta.Despues!.Value.GetProperty("cantidad").GetInt32());
    }

    [Fact]
    public async Task CambiarLaCantidadDeUnProducto_SiSeAudita_PeroSinElCampoComprado()
    {
        var x = await Montar();
        var producto = await Leer<ProductoCompraDto>(
            await x.Admin.PostAsJsonAsync("/api/lista-compra/productos", new CrearProductoCompraRequest("Leche")), HttpStatusCode.Created);

        // No hay endpoint que cambie la cantidad: la regla del registro se comprueba directamente sobre el contexto.
        using var scope = x.F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var p = await db.ProductosListaCompra.IgnoreQueryFilters().FirstAsync(q => q.Id == producto.Id);
        p.Cantidad = 5;
        p.Comprado = true;

        var edicion = Assert.Single(RegistroAuditoria.Capturar(db.ChangeTracker, null));

        Assert.Equal("editar", edicion.Accion);
        Assert.Contains("cantidad", edicion.Despues);
        Assert.DoesNotContain("comprado", edicion.Despues);
    }

    [Fact]
    public async Task CrearYRevocarUnEnlace_SeRegistran_SinElTokenNiSuHash()
    {
        var x = await Montar();
        var enlace = await Leer<EnlaceListaCreado>(await x.Admin.PostAsync("/api/lista-compra/enlaces", null), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await x.Admin.DeleteAsync($"/api/lista-compra/enlaces/{enlace.Id}")).StatusCode);

        var eventos = (await Eventos(x.Admin, "enlace_lista_compra")).OrderBy(e => e.Cuando).ToList();

        Assert.Equal(["crear", "editar"], eventos.Select(e => e.Accion));
        Assert.True(eventos[1].Antes!.Value.GetProperty("activo").GetBoolean());
        Assert.False(eventos[1].Despues!.Value.GetProperty("activo").GetBoolean());
        var texto = JsonSerializer.Serialize(eventos);
        Assert.DoesNotContain(enlace.Token, texto);
        Assert.DoesNotContain("tokenHash", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TokenHash", texto);
    }

    [Fact]
    public async Task LoQueSeAnadePorElEnlace_QuedaSinAutor()
    {
        var x = await Montar();
        var enlace = await Leer<EnlaceListaCreado>(await x.Admin.PostAsync("/api/lista-compra/enlaces", null), HttpStatusCode.Created);

        await Leer<ProductoCompraDto>(
            await Publica(x.Anonimo, HttpMethod.Post, "/api/lista-compra/publica/productos", enlace.Token, new CrearProductoCompraRequest("Agua")),
            HttpStatusCode.Created);

        var evento = Assert.Single(await Eventos(x.Admin, "producto_compra"));
        Assert.Equal("crear", evento.Accion);
        Assert.Null(evento.UsuarioId);
        Assert.Null(evento.Autor); // el historial lo muestra como «Alguien»
    }
}
