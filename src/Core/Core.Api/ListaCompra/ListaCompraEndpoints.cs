using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.ListaCompra;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Core.Mapeo;
using QRCoder;

namespace MiParte.Core.Api.ListaCompra;

/// <summary>
/// Endpoints de la lista de la compra. Los del hogar exigen sesión y hogar; los del enlace QR no requieren sesión: el token va en la
/// cabecera <see cref="CabeceraEnlace"/> y solo permite ver, añadir y marcar productos de ese hogar.
/// </summary>
public static class ListaCompraEndpoints
{
    /// <summary>Cabecera con el token del enlace QR en las rutas públicas.</summary>
    public const string CabeceraEnlace = "X-Enlace-Lista";

    /// <summary>Registra las rutas de la lista de la compra (ver la clase).</summary>
    public static IEndpointRouteBuilder MapListaCompra(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lista-compra", ListarAsync).RequireAuthorization();
        app.MapPut("/api/lista-compra/activacion", ActivarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HogarFunciones);
        app.MapPost("/api/lista-compra/productos", CrearProductoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.ListaCompra);
        app.MapPut("/api/lista-compra/productos/{id:guid}", MarcarProductoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.ListaCompra);
        app.MapDelete("/api/lista-compra/productos/{id:guid}", BorrarProductoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.ListaCompra);
        app.MapPost("/api/lista-compra/enlaces", CrearEnlaceAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.ListaCompra);
        app.MapDelete("/api/lista-compra/enlaces/{id:guid}", RevocarEnlaceAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.ListaCompra);
        app.MapPost("/api/lista-compra/qr", GenerarQrAsync).RequireAuthorization();

        app.MapGet("/api/lista-compra/publica", ListarPublicaAsync).AllowAnonymous().WithMetadata(new SinHogarActual());
        app.MapPost("/api/lista-compra/publica/productos", CrearProductoPublicoAsync)
            .AllowAnonymous().WithMetadata(new SinHogarActual()).RequireRateLimiting(LimitacionPeticiones.Costosa);
        app.MapPut("/api/lista-compra/publica/productos/{id:guid}", MarcarProductoPublicoAsync)
            .AllowAnonymous().WithMetadata(new SinHogarActual()).RequireRateLimiting(LimitacionPeticiones.Costosa);
        return app;
    }

    /// <summary>GET /api/lista-compra: si la lista está activada y sus productos. 409 sin hogar.</summary>
    private static async Task<IResult> ListarAsync([FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var productos = await servicio.ListarAsync(hogarId, ct);
        return Results.Ok(new ListaCompraResponse(await servicio.ActivaAsync(hogarId, ct), productos.Select(p => p.ADto()).ToList()));
    }

    /// <summary>PUT /api/lista-compra/activacion (permiso <c>hogar.funciones</c>): activa o desactiva la lista. 409 sin hogar.</summary>
    private static async Task<IResult> ActivarAsync(
        ActivarListaCompraRequest req, [FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        return Results.Ok(new { activa = await servicio.ActivarAsync(hogarId, req.Activa, ct) });
    }

    /// <summary>POST /api/lista-compra/productos: añade un producto. 201; 400 nombre o cantidad inválidos; 409 lista desactivada.</summary>
    private static async Task<IResult> CrearProductoAsync(
        CrearProductoCompraRequest req, [FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.AnadirAsync(hogarId, req.Nombre, req.Cantidad ?? 1, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/lista-compra/productos/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>PUT /api/lista-compra/productos/{id}: marca o desmarca como comprado. 404 si no es del hogar.</summary>
    private static async Task<IResult> MarcarProductoAsync(
        Guid id, MarcarProductoCompraRequest req, [FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.MarcarAsync(hogarId, id, req.Comprado, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(r.Valor!.ADto());
    }

    /// <summary>DELETE /api/lista-compra/productos/{id}: borra un producto. 204; 404 si no es del hogar.</summary>
    private static async Task<IResult> BorrarProductoAsync(
        Guid id, [FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.EliminarAsync(hogarId, id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>
    /// POST /api/lista-compra/enlaces (permiso <c>lista.compra</c>): crea un enlace QR que caduca a los 7 días. 201 con el token en
    /// claro, que no vuelve a verse; 409 si la lista no está activada.
    /// </summary>
    private static async Task<IResult> CrearEnlaceAsync(
        [FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (hogar.MiembroId is not { } miembroId) return ApiComun.Invalido("Solo un miembro del hogar puede crear enlaces de la lista.");
        var r = await servicio.CrearEnlaceAsync(hogarId, miembroId, null, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        var e = r.Valor!;
        return Results.Created($"/api/lista-compra/enlaces/{e.Id}", new EnlaceListaCreado(e.Id, e.Token, e.CaducaEn));
    }

    /// <summary>DELETE /api/lista-compra/enlaces/{id}: revoca un enlace. 204; 404 si no es del hogar.</summary>
    private static async Task<IResult> RevocarEnlaceAsync(
        Guid id, [FromServices] ServicioListaCompra servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.RevocarEnlaceAsync(hogarId, id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>
    /// POST /api/lista-compra/qr: codifica un texto (la URL de la lista) en un QR y lo devuelve como SVG. 400 si el texto está vacío o
    /// supera los 500 caracteres.
    /// </summary>
    private static IResult GenerarQrAsync(QrListaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Texto) || req.Texto.Length > 500)
            return ApiComun.Invalido("El texto del QR debe tener entre 1 y 500 caracteres.");
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(req.Texto, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(datos).GetGraphic(8, "#24161A", "#FFFFFF", true, SvgQRCode.SizingMode.ViewBoxAttribute);
        return Results.Ok(new QrListaResponse(svg));
    }

    /// <summary>GET /api/lista-compra/publica (sin sesión, cabecera <c>X-Enlace-Lista</c>): productos del hogar del enlace. 404 token desconocido; 409 enlace revocado o caducado, o lista desactivada.</summary>
    private static async Task<IResult> ListarPublicaAsync(HttpContext ctx, [FromServices] ServicioListaCompra servicio, CancellationToken ct)
    {
        var r = await servicio.PublicaAsync(Token(ctx), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Ok(new ListaCompraResponse(true, r.Valor!.Productos.Select(p => p.ADto()).ToList()));
    }

    /// <summary>POST /api/lista-compra/publica/productos (sin sesión): añade un producto con el enlace. 201; 400, 404 o 409 como en la ruta privada.</summary>
    private static async Task<IResult> CrearProductoPublicoAsync(
        CrearProductoCompraRequest req, HttpContext ctx, [FromServices] ServicioListaCompra servicio, CancellationToken ct)
    {
        var r = await servicio.AnadirPublicoAsync(Token(ctx), req.Nombre, req.Cantidad ?? 1, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/lista-compra/publica/productos/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>PUT /api/lista-compra/publica/productos/{id} (sin sesión): marca o desmarca un producto del hogar del enlace.</summary>
    private static async Task<IResult> MarcarProductoPublicoAsync(
        Guid id, MarcarProductoCompraRequest req, HttpContext ctx, [FromServices] ServicioListaCompra servicio, CancellationToken ct)
    {
        var r = await servicio.MarcarPublicoAsync(Token(ctx), id, req.Comprado, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(r.Valor!.ADto());
    }

    /// <summary>Token del enlace de la cabecera, o null si no viene.</summary>
    private static string? Token(HttpContext ctx) => ctx.Request.Headers[CabeceraEnlace].FirstOrDefault();
}
