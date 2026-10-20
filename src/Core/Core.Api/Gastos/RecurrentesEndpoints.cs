using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>
/// Endpoints de gastos recurrentes (plantillas mensuales) y su generación por mes. Solo validan el formato HTTP, delegan en
/// <see cref="ServicioRecurrentes"/> y traducen su resultado.
/// </summary>
public static class RecurrentesEndpoints
{
    /// <summary>Registra los endpoints de /api/gastos-recurrentes (CRUD y generar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapGastosRecurrentes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos-recurrentes").RequireAuthorization();
        var escritura = g.MapGroup("").RequierePermiso(CatalogoPermisos.RecurrentesGestionar);
        g.MapGet("", ListarAsync);
        g.MapGet("{id:guid}", ObtenerAsync);
        escritura.MapPost("", CrearAsync);
        escritura.MapPut("{id:guid}", EditarAsync);
        escritura.MapDelete("{id:guid}", BorrarAsync);
        escritura.MapPost("generar", GenerarAsync).RequireRateLimiting(LimitacionPeticiones.Costosa);
        app.MapPut("/api/hogar/recurrentes", ActivarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HogarFunciones);
        return app;
    }

    /// <summary>Convierte el cuerpo de la petición en los datos del caso de uso.</summary>
    private static DatosRecurrente Datos(GastoRecurrenteRequest r)
        => new(r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.DiaMes, r.Concepto, r.Activo, r.Personal);

    /// <summary>GET /api/gastos-recurrentes: lista las plantillas ordenadas por día del mes. 409 sin hogar.</summary>
    private static async Task<IResult> ListarAsync(
        [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        return Results.Ok((await servicio.ListarAsync(ct)).Select(r => r.ADto()));
    }

    /// <summary>GET /api/gastos-recurrentes/{id}: devuelve una plantilla. 409 sin hogar; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.ObtenerAsync(id, ct);
        return r is null ? Results.NotFound() : Results.Ok(r.ADto());
    }

    /// <summary>POST /api/gastos-recurrentes: crea una plantilla. 201 si se crea; 409 sin hogar; 400 si no es válida.</summary>
    private static async Task<IResult> CrearAsync(
        GastoRecurrenteRequest req, [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearAsync(hogarId, Datos(req), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/gastos-recurrentes/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>PUT /api/gastos-recurrentes/{id}: actualiza la plantilla sin modificar los gastos ya generados. 409 sin hogar; 404 si no existe; 400 si no es válida.</summary>
    private static async Task<IResult> EditarAsync(
        Guid id, GastoRecurrenteRequest req, [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EditarAsync(id, Datos(req), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(r.Valor!.ADto());
    }

    /// <summary>DELETE /api/gastos-recurrentes/{id}: borra la plantilla. 409 sin hogar o si ya tiene gastos generados (hay que desactivarla); 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarAsync(
        Guid id, [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.BorrarAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>
    /// PUT /api/hogar/recurrentes (permiso <c>hogar.funciones</c>): activa o desactiva los gastos recurrentes del hogar. Desactivarlos no
    /// borra las plantillas, solo bloquea crear plantillas y generar gastos. 403 sin el permiso; 409 sin hogar; 200 con el estado resultante.
    /// </summary>
    private static async Task<IResult> ActivarAsync(
        ActivarRecurrentesRequest req, [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        return Results.Ok(new { activas = await servicio.ActivarAsync(req.Activas, ct) });
    }

    /// <summary>Crea los gastos del mes para las plantillas activas que aún no tengan uno. Idempotente. 409 si las recurrentes están desactivadas en el hogar o el mes está cerrado.</summary>
    private static async Task<IResult> GenerarAsync(
        [FromQuery] string? mes, [FromServices] ServicioRecurrentes servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        var r = await servicio.GenerarAsync(inicio, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Ok(new GenerarRecurrentesResponse(ApiComun.FormatoMes(r.Valor!.Mes), r.Valor.Creados, r.Valor.YaExistentes));
    }
}
