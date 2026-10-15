using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Reparto;

/// <summary>
/// Endpoints de perfiles de reparto del hogar (porcentaje, partes, cuenta común o individual). Solo traducen HTTP y
/// delegan en <see cref="ServicioPerfiles"/>.
/// </summary>
public static class PerfilesEndpoints
{
    /// <summary>Registra los endpoints de /api/perfiles (listar, obtener, crear, actualizar y eliminar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapPerfiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/perfiles", ListarAsync).RequireAuthorization();
        app.MapGet("/api/perfiles/{id:guid}", ObtenerAsync).RequireAuthorization();
        app.MapPost("/api/perfiles", CrearAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.PerfilesGestionar);
        app.MapPut("/api/perfiles/{id:guid}", ActualizarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.PerfilesGestionar);
        app.MapDelete("/api/perfiles/{id:guid}", EliminarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.PerfilesGestionar);
        return app;
    }

    /// <summary>Convierte el detalle del cuerpo en los valores que usa el caso de uso.</summary>
    private static List<ValorMiembro>? Valores(GuardarPerfilRequest r)
        => r.Detalle?.Select(d => new ValorMiembro(d.MiembroId, d.Valor)).ToList();

    /// <summary>GET /api/perfiles: devuelve los perfiles del hogar con su detalle, ordenados por nombre.</summary>
    private static async Task<IResult> ListarAsync([FromServices] ServicioPerfiles servicio, CancellationToken ct)
        => Results.Ok((await servicio.ListarAsync(ct)).Select(p => p.ADto()).ToList());

    /// <summary>GET /api/perfiles/{id}: devuelve un perfil con su detalle; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(Guid id, [FromServices] ServicioPerfiles servicio, CancellationToken ct)
    {
        var p = await servicio.ObtenerAsync(id, ct);
        return p is null ? Results.NotFound() : Results.Ok(p.ADto());
    }

    /// <summary>POST /api/perfiles: crea un perfil con su detalle. 201 si se crea; 400 si no supera la validación; 409 si el nombre ya existe.</summary>
    private static async Task<IResult> CrearAsync(
        GuardarPerfilRequest req, [FromServices] ServicioPerfiles servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        var r = await servicio.CrearAsync(hogar.HogarId!.Value, req.Nombre, req.Modo, Valores(req), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/perfiles/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>PUT /api/perfiles/{id}: sustituye nombre, modo y detalle del perfil. 404 si no existe; 400 si no es válido; 409 si el nombre ya existe.</summary>
    private static async Task<IResult> ActualizarAsync(
        Guid id, GuardarPerfilRequest req, [FromServices] ServicioPerfiles servicio, CancellationToken ct)
    {
        var r = await servicio.ActualizarAsync(id, req.Nombre, req.Modo, Valores(req), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(r.Valor!.ADto());
    }

    /// <summary>DELETE /api/perfiles/{id}: elimina el perfil y su detalle. 404 si no existe; 409 si lo usan categorías, gastos o gastos recurrentes; 204 si se elimina.</summary>
    private static async Task<IResult> EliminarAsync(Guid id, [FromServices] ServicioPerfiles servicio, CancellationToken ct)
    {
        var r = await servicio.EliminarAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
