using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Miembros;

/// <summary>
/// Endpoints de miembros del hogar actual (listar, añadir, actualizar, desactivar) y de invitaciones (crear y aceptar). Los
/// datos se filtran por hogar. Solo traducen HTTP (claims, DTOs, códigos de estado) y delegan en <see cref="ServicioMiembros"/>,
/// que aplica los permisos y las reglas del hogar.
/// </summary>
public static class MiembrosEndpoints
{
    /// <summary>Tiempo durante el que una invitación puede aceptarse desde su creación (7 días).</summary>
    public static readonly TimeSpan VigenciaInvitacion = TokenInvitacion.Vigencia;

    /// <summary>
    /// Registra GET/POST /api/miembros, PUT/DELETE /api/miembros/{id}, POST /api/invitaciones y
    /// POST /api/invitaciones/aceptar (este último sin hogar actual). Todos requieren autenticación.
    /// </summary>
    public static IEndpointRouteBuilder MapMiembros(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/miembros", ListarAsync).RequireAuthorization();
        app.MapPost("/api/miembros", CrearAsync).RequireAuthorization();
        app.MapPut("/api/miembros/{id:guid}", ActualizarAsync).RequireAuthorization();
        app.MapDelete("/api/miembros/{id:guid}", DesactivarAsync).RequireAuthorization();

        app.MapPost("/api/invitaciones", CrearInvitacionAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa);
        app.MapPost("/api/invitaciones/aceptar", AceptarInvitacionAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa)
            .WithMetadata(new SinHogarActual());

        return app;
    }

    /// <summary>SHA-256 en hexadecimal minúscula del token en UTF-8 (igual que aceptar_invitacion en SQL).</summary>
    public static string HashToken(string token) => TokenInvitacion.Hash(token);

    /// <summary>Obtiene el id del usuario autenticado del claim "sub"; false si falta o no es un GUID.</summary>
    internal static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);

    /// <summary>Traduce el fallo de un caso de uso a una respuesta JSON <c>{ error }</c> con su código HTTP.</summary>
    internal static IResult Error(ErrorAplicacion error) => Results.Json(
        new { error = error.Mensaje },
        statusCode: error.Tipo switch
        {
            TipoError.Prohibido => StatusCodes.Status403Forbidden,
            TipoError.NoEncontrado => StatusCodes.Status404NotFound,
            TipoError.Conflicto => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });

    /// <summary>
    /// GET /api/miembros: miembros activos del hogar actual ordenados por nombre; con <c>incluirInactivos=true</c> también
    /// los desactivados (para poner nombre a quien aparece en el historial). Cualquier miembro puede consultarlo.
    /// </summary>
    private static async Task<IResult> ListarAsync(
        HttpContext ctx, [FromServices] ServicioMiembros servicio, CancellationToken ct, bool incluirInactivos = false)
    {
        var miembros = await servicio.ListarAsync(incluirInactivos, ct);
        var usuario = TryUsuario(ctx, out var userId) ? userId : (Guid?)null;
        return Results.Ok(miembros.Select(m => m.ADto(usuario)).ToList());
    }

    /// <summary>
    /// POST /api/miembros (permiso <c>miembros.gestionar</c>): añade un miembro "adulto" o "a_cargo" sin usuario vinculado.
    /// Un a_cargo exige un responsable adulto activo y un adulto no puede tenerlo. 403 sin el permiso,
    /// 400 por datos inválidos, 201 con el miembro creado.
    /// </summary>
    private static async Task<IResult> CrearAsync(
        CrearMiembroRequest req, HttpContext ctx, [FromServices] ServicioMiembros servicio,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearAsync(userId, hogarId, new NuevoMiembro(req.Nombre, req.Tipo, req.ResponsableId), ct);
        if (r.Error is { } error) return Error(error);
        return Results.Created($"/api/miembros/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>
    /// PUT /api/miembros/{id}: cambia nombre, estado activo, rol, permisos o responsable. Sin <c>miembros.gestionar</c> solo se puede
    /// renombrarse a sí mismo (403 si intenta más). 404 si no existe; 400 por datos inválidos; 409 si
    /// dejaría al hogar sin administrador activo y vinculado, o si desactiva a un responsable de miembros
    /// a cargo activos.
    /// </summary>
    private static async Task<IResult> ActualizarAsync(
        Guid id, ActualizarMiembroRequest req, HttpContext ctx, [FromServices] ServicioMiembros servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.ActualizarAsync(
            userId, id, new CambiosMiembro(req.Nombre, req.Activo, req.ResponsableId, req.Rol, req.Permisos), ct);
        return r.Error is { } error ? Error(error) : Results.Ok(r.Valor!.ADto(userId));
    }

    /// <summary>DELETE /api/miembros/{id}: borrado lógico, equivale a PUT con activo=false (mismas reglas y códigos).</summary>
    private static async Task<IResult> DesactivarAsync(
        Guid id, HttpContext ctx, [FromServices] ServicioMiembros servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.DesactivarAsync(userId, id, ct);
        return r.Error is { } error ? Error(error) : Results.Ok(r.Valor!.ADto(userId));
    }

    /// <summary>
    /// POST /api/invitaciones (permiso <c>invitaciones.crear</c>): crea una invitación, opcionalmente para vincular un miembro
    /// existente (404 si no existe; 409 si está desactivado o ya vinculado). El token se devuelve en claro
    /// solo en esta respuesta (201); en base de datos se guarda únicamente su hash SHA-256.
    /// </summary>
    private static async Task<IResult> CrearInvitacionAsync(
        CrearInvitacionRequest? req, HttpContext ctx, [FromServices] ServicioMiembros servicio,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearInvitacionAsync(userId, hogarId, req?.MiembroId, ct);
        if (r.Error is { } error) return Error(error);
        var inv = r.Valor!;
        return Results.Created($"/api/invitaciones/{inv.Id}", new InvitacionCreada(inv.Id, inv.Token, inv.CaducaEn));
    }

    /// <summary>
    /// POST /api/invitaciones/aceptar (sin hogar actual): busca la invitación por hash del token y vincula al usuario a un
    /// miembro existente o crea uno nuevo con el nombre dado. 400 sin token/nombre; 404 token desconocido; 409 si está usada,
    /// caducada, el usuario ya está en el hogar o el miembro no está disponible. Devuelve el resumen del hogar.
    /// </summary>
    private static async Task<IResult> AceptarInvitacionAsync(
        AceptarInvitacionRequest req, HttpContext ctx, [FromServices] ServicioMiembros servicio, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();
        var r = await servicio.AceptarInvitacionAsync(userId, req.Token, req.Nombre, ct);
        if (r.Error is { } error) return Error(error);
        var h = r.Valor!;
        return Results.Ok(h.ADto());
    }
}
