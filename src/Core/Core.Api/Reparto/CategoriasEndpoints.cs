using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Reparto;

/// <summary>
/// Endpoints de categorías de gasto del hogar (jerárquicas, con perfil de reparto opcional). Solo traducen HTTP y delegan en
/// <see cref="ServicioCategorias"/>.
/// </summary>
public static class CategoriasEndpoints
{
    /// <summary>Registra los endpoints de /api/categorias (listar, crear, actualizar y eliminar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCategorias(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categorias", ListarAsync).RequireAuthorization();
        app.MapPost("/api/categorias", CrearAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CategoriasGestionar);
        app.MapPut("/api/categorias/{id:guid}", ActualizarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CategoriasGestionar);
        app.MapDelete("/api/categorias/{id:guid}", EliminarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CategoriasGestionar);
        return app;
    }

    /// <summary>Convierte el cuerpo de la petición en los datos del caso de uso.</summary>
    private static DatosCategoria Datos(GuardarCategoriaRequest r)
        => new(r.Nombre, r.CategoriaPadreId, r.PerfilRepartoId, r.ACargoCuentaComun, r.PredeterminadoGastos);

    /// <summary>GET /api/categorias: devuelve las categorías del hogar ordenadas por nombre.</summary>
    private static async Task<IResult> ListarAsync([FromServices] ServicioCategorias servicio, CancellationToken ct)
        => Results.Ok((await servicio.ListarAsync(ct)).Select(c => c.ADto()).ToList());

    /// <summary>POST /api/categorias: crea una categoría en el hogar actual. 201 si se crea; 400 si el cuerpo no es válido; 409 si ya existe una con ese nombre en ese nivel.</summary>
    private static async Task<IResult> CrearAsync(
        GuardarCategoriaRequest req, [FromServices] ServicioCategorias servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearAsync(hogarId, Datos(req), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/categorias/{r.Valor!.Id}", r.Valor.ADto());
    }

    /// <summary>PUT /api/categorias/{id}: actualiza nombre, categoría padre y perfil de reparto. 404 si no existe; 400 si no es válido (incluye ciclos); 409 si el nombre está duplicado en ese nivel.</summary>
    private static async Task<IResult> ActualizarAsync(
        Guid id, GuardarCategoriaRequest req, [FromServices] ServicioCategorias servicio, CancellationToken ct)
    {
        var r = await servicio.ActualizarAsync(id, Datos(req), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(r.Valor!.ADto());
    }

    /// <summary>
    /// DELETE /api/categorias/{id}?moverA={id}: elimina la categoría, pasando antes sus gastos y gastos recurrentes a <c>moverA</c>.
    /// 404 si no existe; 409 si tiene subcategorías, o gastos y no se indica <c>moverA</c> (con <c>enUso: true</c>); 400 si el
    /// destino no es válido; 204 si se elimina.
    /// </summary>
    private static async Task<IResult> EliminarAsync(
        Guid id, [FromQuery] Guid? moverA, [FromServices] ServicioCategorias servicio, CancellationToken ct)
    {
        var r = await servicio.EliminarAsync(id, moverA, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
