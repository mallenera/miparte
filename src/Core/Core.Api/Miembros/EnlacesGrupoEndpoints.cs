using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Miembros;

/// <summary>
/// Endpoints de los enlaces de grupo reutilizables: listar, crear y revocar (permiso <c>enlaces.gestionar</c>) y unirse con el
/// token (sin hogar actual, sin permiso especial). Solo traducen HTTP y delegan en <see cref="ServicioEnlacesGrupo"/>.
/// </summary>
public static class EnlacesGrupoEndpoints
{
    /// <summary>
    /// Registra GET/POST /api/enlaces-grupo, DELETE /api/enlaces-grupo/{id} y POST /api/enlaces-grupo/unirse (este último sin hogar
    /// actual y con límite de peticiones, porque recibe un token). Todos requieren autenticación.
    /// </summary>
    public static IEndpointRouteBuilder MapEnlacesGrupo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/enlaces-grupo", ListarAsync)
            .RequireAuthorization()
            .RequierePermiso(CatalogoPermisos.EnlacesGestionar);
        app.MapPost("/api/enlaces-grupo", CrearAsync)
            .RequireAuthorization()
            .RequierePermiso(CatalogoPermisos.EnlacesGestionar);
        app.MapDelete("/api/enlaces-grupo/{id:guid}", RevocarAsync)
            .RequireAuthorization()
            .RequierePermiso(CatalogoPermisos.EnlacesGestionar);

        app.MapPost("/api/enlaces-grupo/unirse", UnirseAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa)
            .WithMetadata(new SinHogarActual());

        return app;
    }

    /// <summary>
    /// GET /api/enlaces-grupo (permiso <c>enlaces.gestionar</c>): todos los enlaces del hogar con su estado, sin token.
    /// </summary>
    private static async Task<IResult> ListarAsync(HttpContext ctx, [FromServices] ServicioEnlacesGrupo servicio, CancellationToken ct)
    {
        if (!MiembrosEndpoints.TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.ListarAsync(userId, ct);
        if (r.Error is { } error) return MiembrosEndpoints.Error(error);
        return Results.Ok(r.Valor!.Select(e => new EnlaceGrupoDto(
            e.Id, e.CreadoPor, e.CreadoEn, e.CaducaEn, e.Usos, ReglasEnlaceGrupo.TextoEstado(e.Estado))).ToList());
    }

    /// <summary>
    /// POST /api/enlaces-grupo (permiso <c>enlaces.gestionar</c>): crea un enlace reutilizable. El cuerpo es opcional; sin
    /// <c>caducaEn</c> no caduca. 201 con el token en claro, que no vuelve a verse; 400 si la caducidad ya pasó.
    /// </summary>
    private static async Task<IResult> CrearAsync(
        CrearEnlaceGrupoRequest? req, HttpContext ctx, [FromServices] ServicioEnlacesGrupo servicio,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!MiembrosEndpoints.TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.CrearAsync(userId, hogar.HogarId!.Value, req?.CaducaEn, ct);
        if (r.Error is { } error) return MiembrosEndpoints.Error(error);
        var e = r.Valor!;
        return Results.Created($"/api/enlaces-grupo/{e.Id}", new EnlaceGrupoCreado(e.Id, e.Token, e.CaducaEn));
    }

    /// <summary>
    /// DELETE /api/enlaces-grupo/{id} (permiso <c>enlaces.gestionar</c>): revoca el enlace; deja de admitir uniones, pero se conserva.
    /// 204 también si ya estaba revocado; 404 si no es de este hogar.
    /// </summary>
    private static async Task<IResult> RevocarAsync(Guid id, HttpContext ctx, [FromServices] ServicioEnlacesGrupo servicio, CancellationToken ct)
    {
        if (!MiembrosEndpoints.TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.RevocarAsync(userId, id, ct);
        return r.Error is { } error ? MiembrosEndpoints.Error(error) : Results.NoContent();
    }

    /// <summary>
    /// POST /api/enlaces-grupo/unirse (sin hogar actual): une al usuario al hogar del enlace como miembro nuevo con <c>nombre</c>, o
    /// vinculado a un adulto existente sin cuenta con <c>miembroId</c>. 400 sin token o sin nombre; 404 token desconocido o miembro
    /// ajeno; 409 enlace revocado o caducado, usuario ya en el hogar o miembro no disponible. Devuelve el hogar.
    /// </summary>
    private static async Task<IResult> UnirseAsync(
        UnirseEnlaceGrupoRequest req, HttpContext ctx, [FromServices] ServicioEnlacesGrupo servicio, CancellationToken ct)
    {
        if (!MiembrosEndpoints.TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.UnirseAsync(userId, req.Token, req.Nombre, req.MiembroId, ct);
        if (r.Error is { } error) return MiembrosEndpoints.Error(error);
        var h = r.Valor!;
        return Results.Ok(new HogarResumen(h.Id, h.Nombre, h.CuentaComunActiva, h.AhorroActivo, TextoTipoGrupo.ATexto(h.Tipo), h.RecurrentesActivas));
    }
}
