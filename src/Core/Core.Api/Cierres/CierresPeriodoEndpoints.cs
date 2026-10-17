using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Application.Periodos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Core.Mapeo;

namespace MiParte.Core.Api.Cierres;

/// <summary>
/// Cierre del periodo propio de un viaje o evento: cerrarlo congela sus gastos y deja la liquidación final; reabrirlo lo
/// vuelve a dejar editable. Solo traducen HTTP y delegan en <see cref="ServicioPeriodos"/>.
/// </summary>
public static class CierresPeriodoEndpoints
{
    /// <summary>Registra POST /api/hogar/cerrar-periodo y POST /api/hogar/reabrir-periodo; ambos requieren autorización y permiso.</summary>
    public static IEndpointRouteBuilder MapCierresPeriodo(this IEndpointRouteBuilder app)
    {
        // Cerrar lo puede hacer cualquier miembro con mes.cerrar, como un mes; reabrir queda reservado a hogar.funciones, como reabrir un mes a un admin.
        app.MapPost("/api/hogar/cerrar-periodo", CerrarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.MesCerrar);
        app.MapPost("/api/hogar/reabrir-periodo", ReabrirAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HogarFunciones);
        return app;
    }

    /// <summary>
    /// POST /api/hogar/cerrar-periodo (permiso <c>mes.cerrar</c>): cierra el periodo propio del hogar. Un periodo abierto termina hoy; uno de
    /// rango conserva su fin. Los gastos del rango quedan bloqueados (409) y los gastos pendientes se liquidan en un ciclo final.
    /// 400 si el hogar no tiene periodo propio o aún no ha empezado; 403 sin el permiso o sin ser miembro; 409 sin hogar, si ya está
    /// cerrado o si otro cierre acaba de hacerse; 200 con el periodo cerrado y la liquidación final.
    /// </summary>
    private static async Task<IResult> CerrarAsync(
        HttpContext ctx, [FromServices] ServicioPeriodos servicio, [FromServices] ServicioMiembros miembros,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out var userId)) return Results.Unauthorized();
        // Igual que el cierre de mes: vale el miembro que ya resolvió el middleware; si no está, se busca por usuario.
        var yo = ctx.Items.TryGetValue(HogarActualMiddleware.ClaveMiembro, out var resuelto) && resuelto is Miembro m
            ? m
            : await miembros.MiembroActivoAsync(userId, ct);
        if (yo is null)
            return Results.Json(new { error = Mensajes.SoloMiembroCierra }, statusCode: StatusCodes.Status403Forbidden);

        var r = await servicio.CerrarAsync(yo, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        var resultado = r.Valor!;
        return Results.Ok(new CierrePeriodoResponse(resultado.Periodo.ATexto(), resultado.CerradoEn, resultado.Ciclo?.ADto()));
    }

    /// <summary>
    /// POST /api/hogar/reabrir-periodo (permiso <c>hogar.funciones</c>): reabre un periodo cerrado; sus gastos vuelven a poder cambiarse.
    /// El periodo queda como rango con la fecha de fin que tenía. 403 sin el permiso; 404 si no estaba cerrado; 409 sin hogar; 204 si se reabre.
    /// </summary>
    private static async Task<IResult> ReabrirAsync(
        [FromServices] ServicioPeriodos servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.ReabrirAsync(ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
