using System.Net.Http;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>
/// Lista de la compra: el hogar (productos, activación y enlaces QR) y el acceso público por enlace (sin sesión, con el token en la
/// cabecera <c>X-Enlace-Lista</c>).
/// </summary>
public sealed partial class CoreApiClient
{
    /// <summary>Lista de la compra del hogar: si está activada y sus productos (<c>GET /api/lista-compra</c>).</summary>
    public Task<ListaCompraResponse> ObtenerListaCompraAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<ListaCompraResponse>("api/lista-compra", ct);

    /// <summary>Activa o desactiva la lista de la compra; solo con <c>hogar.funciones</c> (<c>PUT /api/lista-compra/activacion</c>).</summary>
    /// <param name="activa">Activa (true) o desactiva (false).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<JsonElement> ActivarListaCompraAsync(bool activa, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<JsonElement>(HttpMethod.Put, "api/lista-compra/activacion", new ActivarListaCompraRequest(activa), ct);

    /// <summary>Añade un producto a la lista (<c>POST /api/lista-compra/productos</c>).</summary>
    /// <param name="peticion">Nombre y cantidad.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ProductoCompraDto> CrearProductoCompraAsync(CrearProductoCompraRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ProductoCompraDto>(HttpMethod.Post, "api/lista-compra/productos", peticion, ct);

    /// <summary>Marca o desmarca un producto como comprado (<c>PUT /api/lista-compra/productos/{id}</c>).</summary>
    /// <param name="id">Producto.</param>
    /// <param name="comprado">Comprado (true) o pendiente (false).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ProductoCompraDto> MarcarProductoCompraAsync(Guid id, bool comprado, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ProductoCompraDto>(HttpMethod.Put, $"api/lista-compra/productos/{id}", new MarcarProductoCompraRequest(comprado), ct);

    /// <summary>Borra un producto de la lista (<c>DELETE /api/lista-compra/productos/{id}</c>).</summary>
    /// <param name="id">Producto a borrar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarProductoCompraAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/lista-compra/productos/{id}", ct);

    /// <summary>Crea un enlace QR para añadir productos sin cuenta (<c>POST /api/lista-compra/enlaces</c>). El token solo viene aquí.</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<EnlaceListaCreado> CrearEnlaceListaCompraAsync(CancellationToken ct = default) =>
        _nucleo.EnviarAsync<EnlaceListaCreado>(HttpMethod.Post, "api/lista-compra/enlaces", null, ct);

    /// <summary>Revoca un enlace QR (<c>DELETE /api/lista-compra/enlaces/{id}</c>).</summary>
    /// <param name="id">Enlace a revocar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task RevocarEnlaceListaCompraAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/lista-compra/enlaces/{id}", ct);

    /// <summary>Genera el SVG de un QR con el texto indicado (<c>POST /api/lista-compra/qr</c>).</summary>
    /// <param name="texto">Texto a codificar, normalmente la URL de la lista pública.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<QrListaResponse> GenerarQrListaCompraAsync(string texto, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<QrListaResponse>(HttpMethod.Post, "api/lista-compra/qr", new QrListaRequest(texto), ct);

    /// <summary>Productos de la lista a la que da acceso un enlace (<c>GET /api/lista-compra/publica</c>, sin sesión).</summary>
    /// <param name="token">Token del enlace QR.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ListaCompraResponse> ObtenerListaPublicaAsync(string token, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ListaCompraResponse>(HttpMethod.Get, "api/lista-compra/publica", null, ct, token);

    /// <summary>Añade un producto con un enlace QR, sin sesión (<c>POST /api/lista-compra/publica/productos</c>).</summary>
    /// <param name="token">Token del enlace QR.</param>
    /// <param name="peticion">Nombre y cantidad.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ProductoCompraDto> AnadirProductoPublicoAsync(string token, CrearProductoCompraRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ProductoCompraDto>(HttpMethod.Post, "api/lista-compra/publica/productos", peticion, ct, token);

    /// <summary>Marca o desmarca un producto con un enlace QR, sin sesión (<c>PUT /api/lista-compra/publica/productos/{id}</c>).</summary>
    /// <param name="token">Token del enlace QR.</param>
    /// <param name="id">Producto.</param>
    /// <param name="comprado">Comprado (true) o pendiente (false).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ProductoCompraDto> MarcarProductoPublicoAsync(string token, Guid id, bool comprado, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ProductoCompraDto>(HttpMethod.Put, $"api/lista-compra/publica/productos/{id}", new MarcarProductoCompraRequest(comprado), ct, token);
}
