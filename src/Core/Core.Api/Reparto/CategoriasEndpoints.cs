using MiParte.Core.Domain;
using MiParte.Core.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Reparto;

/// <summary>Endpoints de categorías de gasto del hogar (jerárquicas, con perfil de reparto opcional).</summary>
public static class CategoriasEndpoints
{
    /// <summary>Longitud máxima del nombre de una categoría.</summary>
    private const int MaxLongitudNombre = 100;

    /// <summary>Registra los endpoints de /api/categorias (listar, crear, actualizar y eliminar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCategorias(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categorias", ListarAsync).RequireAuthorization();
        app.MapPost("/api/categorias", CrearAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CategoriasGestionar);
        app.MapPut("/api/categorias/{id:guid}", ActualizarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CategoriasGestionar);
        app.MapDelete("/api/categorias/{id:guid}", EliminarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CategoriasGestionar);
        return app;
    }

    /// <summary>Convierte una categoría en su DTO de respuesta.</summary>
    private static CategoriaDto Dto(Categoria c) => new(c.Id, c.Nombre, c.CategoriaPadreId, c.PerfilRepartoId, c.ACargoCuentaComun);

    /// <summary>GET /api/categorias: devuelve las categorías del hogar ordenadas por nombre.</summary>
    private static async Task<IResult> ListarAsync([FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var lista = await db.Categorias.OrderBy(c => c.Nombre).ToListAsync(ct);
        return Results.Ok(lista.Select(Dto).ToList());
    }

    /// <summary>POST /api/categorias: crea una categoría en el hogar actual. 201 si se crea; 400 si el cuerpo no es válido; 409 si ya existe una con ese nombre en ese nivel.</summary>
    private static async Task<IResult> CrearAsync(
        GuardarCategoriaRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        var (error, nombre, perfilId, aCargo) = await ValidarAsync(req, null, db, ct);
        if (error is not null) return error;

        var cat = new Categoria
        {
            Id = Guid.NewGuid(),
            HogarId = hogar.HogarId!.Value,
            Nombre = nombre!,
            CategoriaPadreId = req.CategoriaPadreId,
            PerfilRepartoId = perfilId,
            ACargoCuentaComun = aCargo,
        };
        db.Categorias.Add(cat);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Duplicada(); }
        return Results.Created($"/api/categorias/{cat.Id}", Dto(cat));
    }

    /// <summary>PUT /api/categorias/{id}: actualiza nombre, categoría padre y perfil de reparto. 404 si no existe; 400 si no es válido (incluye ciclos); 409 si el nombre está duplicado en ese nivel.</summary>
    private static async Task<IResult> ActualizarAsync(
        Guid id, GuardarCategoriaRequest req, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var cat = await db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) return Results.NotFound();

        var (error, nombre, perfilId, aCargo) = await ValidarAsync(req, id, db, ct);
        if (error is not null) return error;

        cat.Nombre = nombre!;
        cat.CategoriaPadreId = req.CategoriaPadreId;
        cat.PerfilRepartoId = perfilId;
        cat.ACargoCuentaComun = aCargo;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Duplicada(); }
        return Results.Ok(Dto(cat));
    }

    /// <summary>DELETE /api/categorias/{id}: elimina la categoría. 404 si no existe; 409 si tiene subcategorías, gastos o gastos recurrentes asociados; 204 si se elimina.</summary>
    private static async Task<IResult> EliminarAsync(
        Guid id, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var cat = await db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) return Results.NotFound();

        if (await db.Categorias.AnyAsync(c => c.CategoriaPadreId == id, ct))
            return Results.Conflict(new { error = "La categoría tiene subcategorías." });
        if (await db.Gastos.AnyAsync(g => g.CategoriaId == id, ct)
            || await db.GastosRecurrentes.AnyAsync(g => g.CategoriaId == id, ct))
            return Results.Conflict(new { error = "La categoría tiene gastos asociados." });

        db.Categorias.Remove(cat);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "La categoría está en uso." }); }
        return Results.NoContent();
    }

    /// <summary>Respuesta 409 para una categoría con nombre repetido en el mismo nivel.</summary>
    private static IResult Duplicada()
        => Results.Conflict(new { error = "Ya existe una categoría con ese nombre en ese nivel." });

    /// <summary>Respuesta 400 con el mensaje de error indicado.</summary>
    private static IResult Mal(string msg) => Results.BadRequest(new { error = msg });

    /// <summary>
    /// Valida el cuerpo; devuelve el resultado de error o el nombre normalizado, el perfil por defecto definitivo y si va a cargo de la cuenta común.
    /// «A cargo de la cuenta común» y el perfil de cuenta común son la misma decisión: uno implica el otro, y sin perfil se asigna el de cuenta común del hogar.
    /// </summary>
    private static async Task<(IResult? Error, string? Nombre, Guid? PerfilId, bool ACargo)> ValidarAsync(
        GuardarCategoriaRequest req, Guid? idActual, MiParteDbContext db, CancellationToken ct)
    {
        var nombre = req.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return (Mal("El nombre es obligatorio."), null, null, false);
        if (nombre.Length > MaxLongitudNombre)
            return (Mal($"El nombre admite como máximo {MaxLongitudNombre} caracteres."), null, null, false);

        PerfilReparto? perfil = null;
        if (req.PerfilRepartoId is { } perfilId)
        {
            perfil = await db.PerfilesReparto.FirstOrDefaultAsync(p => p.Id == perfilId, ct);
            if (perfil is null) return (Mal("El perfil de reparto no existe en este hogar."), null, null, false);
        }

        var aCargo = req.ACargoCuentaComun || perfil?.Modo == ModoReparto.CuentaComun;
        if (aCargo)
        {
            if (perfil is null)
            {
                perfil = await db.PerfilesReparto.Where(p => p.Modo == ModoReparto.CuentaComun)
                    .OrderBy(p => p.Nombre).ThenBy(p => p.Id).FirstOrDefaultAsync(ct);
                if (perfil is null) return (Mal("El hogar no tiene un perfil de cuenta común: créalo antes de marcar la categoría."), null, null, false);
            }
            else if (perfil.Modo != ModoReparto.CuentaComun)
            {
                return (Mal("Una categoría a cargo de la cuenta común debe usar el perfil de cuenta común."), null, null, false);
            }

            // Pedir el indicador en una categoría que no lo tenía exige la cuenta activada; editar una ya marcada, o elegir su perfil de cuenta común a secas, no.
            var yaMarcada = idActual is { } actual && await db.Categorias.AnyAsync(c => c.Id == actual && c.ACargoCuentaComun, ct);
            if (req.ACargoCuentaComun && !yaMarcada && await CuentaComunEndpoints.ExigirActivaAsync(db, ct) is { } inactiva) return (inactiva, null, null, false);
        }

        if (req.CategoriaPadreId is { } padreId)
        {
            if (padreId == idActual) return (Mal("Una categoría no puede ser su propio padre."), null, null, false);

            var padres = await db.Categorias.ToDictionaryAsync(c => c.Id, c => c.CategoriaPadreId, ct);
            if (!padres.ContainsKey(padreId)) return (Mal("La categoría padre no existe en este hogar."), null, null, false);

            // Sin ciclos: subiendo desde el padre no debe aparecer la categoría que se edita.
            if (idActual is { } id)
            {
                var actual = (Guid?)padreId;
                var visitados = new HashSet<Guid>();
                while (actual is { } a && visitados.Add(a))
                {
                    if (a == id) return (Mal("La categoría padre crearía un ciclo."), null, null, false);
                    actual = padres.GetValueOrDefault(a);
                }
            }
        }

        var existe = await db.Categorias.AnyAsync(c =>
            c.CategoriaPadreId == req.CategoriaPadreId && c.Nombre == nombre && c.Id != idActual, ct);
        if (existe) return (Duplicada(), null, null, false);

        return (null, nombre, perfil?.Id, aCargo);
    }
}
