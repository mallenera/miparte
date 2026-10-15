using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Cierres;

/// <summary>
/// Cierre de mes: cualquier miembro con permiso congela los gastos de un mes (y con ellos su reparto y su liquidación) y solo
/// quien tenga permiso para reabrir puede reabrirlo. Solo traducen HTTP y delegan en <see cref="ServicioCierres"/>.
/// </summary>
public static class CierresMesEndpoints
{
    /// <summary>Registra los endpoints de /api/cierres-mes (listar, cerrar y reabrir); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCierresMes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cierres-mes").RequireAuthorization();
        g.MapGet("", ListarAsync);
        g.MapPost("", CerrarAsync).RequierePermiso(CatalogoPermisos.MesCerrar);
        g.MapDelete("{mes}", ReabrirAsync).RequierePermiso(CatalogoPermisos.MesReabrir);
        return app;
    }

    /// <summary>Convierte un cierre en su DTO, con el nombre de quien lo cerró.</summary>
    private static MesCerradoDto ADto(CierreConAutor c) => new(ApiComun.FormatoMes(c.Cierre.Mes), c.Cierre.CerradoEn, c.Autor);

    /// <summary>GET /api/cierres-mes: meses cerrados del hogar, del más reciente al más antiguo. Cualquier miembro. 409 sin hogar.</summary>
    private static async Task<IResult> ListarAsync(
        [FromServices] ServicioCierres servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        return Results.Ok((await servicio.ListarAsync(ct)).Select(ADto).ToList());
    }

    /// <summary>POST /api/cierres-mes: cierra un mes. Exige el permiso <c>mes.cerrar</c> (403 si no). 400 si el mes es inválido o aún no ha terminado; 409 sin hogar o si ya está cerrado. 201 si se cierra.</summary>
    private static async Task<IResult> CerrarAsync(
        CerrarMesRequest req, HttpContext ctx, [FromServices] ServicioCierres servicio, [FromServices] ServicioMiembros miembros,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (!Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out var userId)) return Results.Unauthorized();
        // Solo se lee (id de usuario y nombre): vale el miembro que ya resolvió el middleware, sin otra consulta.
        var yo = ctx.Items.TryGetValue(HogarActualMiddleware.ClaveMiembro, out var resuelto) && resuelto is Miembro m
            ? m
            : await miembros.MiembroActivoAsync(userId, ct);
        if (yo is null)
            return Results.Json(new { error = Mensajes.SoloMiembroCierra }, statusCode: StatusCodes.Status403Forbidden);
        if (!ApiComun.TryMes(req?.Mes, out var inicio)) return ApiComun.MesInvalido();

        var r = await servicio.CerrarAsync(hogarId, inicio, yo, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/cierres-mes/{ApiComun.FormatoMes(inicio)}", ADto(r.Valor!));
    }

    /// <summary>DELETE /api/cierres-mes/{mes}: reabre un mes cerrado. Exige el permiso <c>mes.reabrir</c> (403 si no). 400 si el mes es inválido; 404 si no estaba cerrado; 409 sin hogar. 204 si se reabre.</summary>
    private static async Task<IResult> ReabrirAsync(
        string mes, [FromServices] ServicioCierres servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        var r = await servicio.ReabrirAsync(inicio, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
