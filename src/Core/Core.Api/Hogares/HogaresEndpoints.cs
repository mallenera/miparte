using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Hogares;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Core.Mapeo;

namespace MiParte.Core.Api.Hogares;

/// <summary>Marca endpoints que funcionan sin hogar seleccionado (alta y listado de hogares).</summary>
public sealed record SinHogarActual;

/// <summary>
/// Endpoints de hogares del usuario autenticado: listar, crear, obtener, eliminar y /api/yo. Solo traducen HTTP (claims, cabecera
/// del hogar, DTOs y códigos) y delegan en <see cref="ServicioHogares"/>.
/// </summary>
public static class HogaresEndpoints
{
    /// <summary>Tope por usuario, para que un registro gratuito no llene la base de datos.</summary>
    public const int MaxHogaresPorUsuario = ServicioHogares.MaxHogaresPorUsuario;

    /// <summary>
    /// Registra GET /api/hogares, POST /api/hogares, GET /api/hogares/{id} y GET /api/yo,
    /// todos con autorización y sin necesidad de hogar actual.
    /// </summary>
    public static IEndpointRouteBuilder MapHogares(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/hogares", ListarAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        app.MapPost("/api/hogares", CrearAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa)
            .WithMetadata(new SinHogarActual());

        app.MapGet("/api/hogares/{id:guid}", ObtenerAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        // No lleva SinHogarActual: actúa sobre el hogar de la cabecera X-Hogar-Id, y así el filtro de permisos sabe de qué hogar se trata.
        app.MapDelete("/api/hogar", EliminarAsync)
            .RequireAuthorization()
            .RequierePermiso(CatalogoPermisos.HogarEliminar)
            .RequireRateLimiting(LimitacionPeticiones.Costosa);

        app.MapGet("/api/yo", YoAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        return app;
    }

    /// <summary>
    /// DELETE /api/hogar?nombre=: elimina el hogar actual y, en cascada, todos sus datos (miembros, gastos, cuenta común, historial...).
    /// No tiene vuelta atrás. Exige el permiso <c>hogar.eliminar</c> (403) y que <paramref name="nombre"/> sea el nombre exacto del
    /// hogar (400), como confirmación. 409 sin hogar; 204 si se elimina.
    /// </summary>
    private static async Task<IResult> EliminarAsync(
        [FromQuery] string? nombre, [FromServices] ServicioHogares servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EliminarAsync(nombre, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>
    /// GET /api/yo: devuelve el id del usuario, sus hogares y el hogar actual (el de la cabecera
    /// X-Hogar-Id si es válido y propio, o el único hogar si no hay cabecera). 401 sin claim "sub".
    /// </summary>
    private static async Task<IResult> YoAsync(HttpContext ctx, [FromServices] ServicioHogares servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        var hogares = (await servicio.ListarAsync(userId, ct)).Select(h => h.ADto()).ToList();

        // Hogar actual: el de la cabecera si es válido y propio; si no hay cabecera, el único hogar.
        HogarResumen? actual = null;
        if (ctx.Request.Headers.TryGetValue(HogarActualMiddleware.Cabecera, out var valor))
        {
            if (Guid.TryParse(valor.ToString(), out var pedido))
                actual = hogares.FirstOrDefault(h => h.Id == pedido);
        }
        else if (hogares.Count == 1)
        {
            actual = hogares[0];
        }

        return Results.Ok(new YoResponse(userId.ToString(), hogares, actual));
    }

    /// <summary>
    /// GET /api/hogares/{id}: resumen de un hogar del usuario. 404 si no existe o no es miembro
    /// (no se revela su existencia); 401 sin claim "sub".
    /// </summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, HttpContext ctx, [FromServices] ServicioHogares servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        var hogar = await servicio.ObtenerAsync(userId, id, ct);
        return hogar is null ? Results.NotFound() : Results.Ok(hogar.ADto());
    }

    /// <summary>GET /api/hogares: hogares donde el usuario es miembro activo, ordenados por nombre. 401 sin claim "sub".</summary>
    private static async Task<IResult> ListarAsync(HttpContext ctx, [FromServices] ServicioHogares servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        return Results.Ok((await servicio.ListarAsync(userId, ct)).Select(h => h.ADto()).ToList());
    }

    /// <summary>
    /// POST /api/hogares: crea un hogar y a su creador como miembro adulto y admin, con la semilla por
    /// defecto, todo en una transacción. 400 si faltan nombres o superan la longitud máxima; 409 si el
    /// usuario ya está en <see cref="MaxHogaresPorUsuario"/> hogares; 201 con el resumen del hogar.
    /// Un <c>Tipo</c> omitido es «hogar»; uno desconocido da 400. El tipo se guarda y determina las categorías sembradas.
    /// </summary>
    private static async Task<IResult> CrearAsync(
        CrearHogarRequest req, HttpContext ctx, [FromServices] ServicioHogares servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        if (!TextoTipoGrupo.TryParse(req.Tipo, out var tipo)) return ApiComun.Invalido("El tipo de grupo no es válido.");

        var r = await servicio.CrearAsync(userId, req.NombreHogar, req.NombreMiembro, tipo, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/hogares/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>Obtiene el id del usuario autenticado del claim "sub"; false si falta o no es un GUID.</summary>
    private static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);
}
