using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>
/// Endpoints de gastos del hogar; cada gasto guarda su reparto calculado en el momento de crearlo o editarlo. Solo validan el
/// formato HTTP, delegan en <see cref="ServicioGastos"/> y traducen su resultado.
/// </summary>
public static class GastosEndpoints
{
    /// <summary>Registra los endpoints de /api/gastos (listar, obtener, crear, editar y borrar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapGastos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos").RequireAuthorization();
        g.MapGet("", ListarAsync);
        g.MapGet("{id:guid}", ObtenerAsync);
        g.MapPost("", CrearAsync).RequierePermiso(CatalogoPermisos.GastosCrear);
        g.MapPut("{id:guid}", EditarAsync).RequierePermiso(CatalogoPermisos.GastosEditar);
        g.MapDelete("{id:guid}", BorrarAsync).RequierePermiso(CatalogoPermisos.GastosBorrar);
        return app;
    }

    /// <summary>Convierte el cuerpo de la petición en los datos del caso de uso.</summary>
    private static DatosGasto Datos(GastoRequest r)
        => new(r.Fecha, r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.Concepto, r.PagadoDesdeAhorro, r.Personal);

    /// <summary>GET /api/gastos: lista los gastos, filtrables por mes (YYYY-MM), rango de fechas (<c>desde</c> y <c>hasta</c>, YYYY-MM-DD, ambos incluidos), categoría, miembro (quien paga o asume una parte mayor que 0) y texto del concepto, de más reciente a más antiguo. 409 sin hogar; 400 si no se indica ninguno de <c>mes</c>, <c>desde</c> y <c>hasta</c> (hace falta al menos uno, para no devolver todo el histórico), si el mes o una fecha es inválida o <c>desde</c> es posterior a <c>hasta</c>.</summary>
    private static async Task<IResult> ListarAsync(
        [FromQuery] string? mes, [FromQuery] string? desde, [FromQuery] string? hasta,
        [FromQuery] Guid? categoriaId, [FromQuery] Guid? miembroId, [FromQuery] string? buscar,
        [FromServices] ServicioGastos servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (ConsultasApi.FiltroGastos(mes, desde, hasta, categoriaId, miembroId, buscar, out var filtro) is { } error)
            return ApiComun.Invalido(error);

        return Results.Ok((await servicio.ListarAsync(filtro, ct)).Select(g => g.ADto()));
    }

    /// <summary>GET /api/gastos/{id}: devuelve un gasto con su reparto. 409 sin hogar; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, [FromServices] ServicioGastos servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var g = await servicio.ObtenerAsync(id, ct);
        return g is null ? Results.NotFound() : Results.Ok(g.ADto());
    }

    /// <summary>POST /api/gastos: crea el gasto y guarda su reparto en la misma transacción. 201 si se crea; 409 sin hogar; 400 si falla la validación o el reparto; 409 si el mes está cerrado, si se paga desde el ahorro y no alcanza o si va a cargo de la cuenta común y el hogar no la ha activado.</summary>
    private static async Task<IResult> CrearAsync(
        GastoRequest req, [FromServices] ServicioGastos servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearAsync(hogarId, Datos(req), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/gastos/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>PUT /api/gastos/{id}: actualiza el gasto y recalcula solo su reparto, sin tocar el resto del histórico. 409 sin hogar; 404 si no existe; 400 si no es válido.</summary>
    private static async Task<IResult> EditarAsync(
        Guid id, GastoRequest req, [FromServices] ServicioGastos servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EditarAsync(id, Datos(req), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(r.Valor!.ADto());
    }

    /// <summary>DELETE /api/gastos/{id}: borra el gasto y su reparto. 409 sin hogar o si el mes está cerrado; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarAsync(
        Guid id, [FromServices] ServicioGastos servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.BorrarAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
