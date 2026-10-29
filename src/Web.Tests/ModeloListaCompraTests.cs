using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la lista de la compra probada sin renderizar componentes.</summary>
public class ModeloListaCompraTests
{
    private static readonly Guid Leche = Guid.NewGuid(), Pan = Guid.NewGuid();

    private static readonly ProductoCompraDto LecheDto = new(Leche, "Leche", 2, false);
    private static readonly ProductoCompraDto PanDto = new(Pan, "Pan", 1, true);

    private static (ModeloListaCompra Modelo, ApiFalsa Api) Crear(string? token = null, bool activa = true)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/lista-compra", HttpStatusCode.OK, new ListaCompraResponse(activa, [LecheDto, PanDto]))
            .Responde("POST /api/lista-compra/productos", HttpStatusCode.Created, new ProductoCompraDto(Guid.NewGuid(), "Huevos", 1, false));
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloListaCompra(cliente, token), api);
    }

    [Fact]
    public async Task CargarAsync_SeparaLosPendientesDeLosComprados()
    {
        var (m, _) = Crear();

        await m.CargarAsync();

        Assert.True(m.Activa);
        Assert.Equal([Leche], m.Pendientes.Select(p => p.Id));
        Assert.Equal([Pan], m.Comprados.Select(p => p.Id));
    }

    [Fact]
    public void NuevoValido_ExigeNombreYUnaCantidadEntre1Y999()
    {
        var (m, _) = Crear();
        m.Nombre = "   ";
        Assert.False(m.NuevoValido);

        m.Nombre = "Tomates";
        m.Cantidad = "1000";
        Assert.False(m.NuevoValido);

        m.Cantidad = "";
        Assert.True(m.NuevoValido);
    }

    [Fact]
    public async Task AnadirAsync_SinCantidad_EnviaUnoYLimpiaElFormulario()
    {
        var (m, api) = Crear();
        await m.CargarAsync();
        m.Nombre = " Huevos ";
        m.Cantidad = "";

        var aviso = await m.AnadirAsync();

        Assert.Equal("Producto añadido", aviso);
        Assert.Contains("\"cantidad\":1", api.Cuerpos["POST /api/lista-compra/productos"]);
        Assert.Contains("\"nombre\":\"Huevos\"", api.Cuerpos["POST /api/lista-compra/productos"]);
        Assert.Equal("", m.Nombre);
    }

    [Fact]
    public void UrlPublica_ComponeLaRutaDeCompraConElToken()
    {
        var url = ModeloListaCompra.UrlPublica("https://mi.app/", "abc123");

        Assert.Equal("https://mi.app/compra/abc123", url);
    }
}
