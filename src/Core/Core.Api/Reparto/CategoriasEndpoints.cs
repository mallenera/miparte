using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Reparto;

public static class CategoriasEndpoints
{
    private const int MaxLongitudNombre = 100;

    public static IEndpointRouteBuilder MapCategorias(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categorias", ListarAsync).RequireAuthorization();
        app.MapPost("/api/categorias", CrearAsync).RequireAuthorization();
        app.MapPut("/api/categorias/{id:guid}", ActualizarAsync).RequireAuthorization();
        app.MapDelete("/api/categorias/{id:guid}", EliminarAsync).RequireAuthorization();
        return app;
    }

    private static CategoriaDto Dto(Categoria c) => new(c.Id, c.Nombre, c.CategoriaPadreId, c.PerfilRepartoId);

    private static async Task<IResult> ListarAsync([FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var lista = await db.Categorias.OrderBy(c => c.Nombre).ToListAsync(ct);
        return Results.Ok(lista.Select(Dto).ToList());
    }

    private static async Task<IResult> CrearAsync(
        GuardarCategoriaRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        var (error, nombre) = await ValidarAsync(req, null, db, ct);
        if (error is not null) return error;

        var cat = new Categoria
        {
            Id = Guid.NewGuid(),
            HogarId = hogar.HogarId!.Value,
            Nombre = nombre!,
            CategoriaPadreId = req.CategoriaPadreId,
            PerfilRepartoId = req.PerfilRepartoId,
        };
        db.Categorias.Add(cat);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Duplicada(); }
        return Results.Created($"/api/categorias/{cat.Id}", Dto(cat));
    }

    private static async Task<IResult> ActualizarAsync(
        Guid id, GuardarCategoriaRequest req, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var cat = await db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) return Results.NotFound();

        var (error, nombre) = await ValidarAsync(req, id, db, ct);
        if (error is not null) return error;

        cat.Nombre = nombre!;
        cat.CategoriaPadreId = req.CategoriaPadreId;
        cat.PerfilRepartoId = req.PerfilRepartoId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Duplicada(); }
        return Results.Ok(Dto(cat));
    }

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

    private static IResult Duplicada()
        => Results.Conflict(new { error = "Ya existe una categoría con ese nombre en ese nivel." });

    private static IResult Mal(string msg) => Results.BadRequest(new { error = msg });

    /// <summary>Valida el cuerpo; devuelve el nombre normalizado o el resultado de error.</summary>
    private static async Task<(IResult? Error, string? Nombre)> ValidarAsync(
        GuardarCategoriaRequest req, Guid? idActual, MiParteDbContext db, CancellationToken ct)
    {
        var nombre = req.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return (Mal("El nombre es obligatorio."), null);
        if (nombre.Length > MaxLongitudNombre)
            return (Mal($"El nombre admite como máximo {MaxLongitudNombre} caracteres."), null);

        if (req.PerfilRepartoId is { } perfilId && !await db.PerfilesReparto.AnyAsync(p => p.Id == perfilId, ct))
            return (Mal("El perfil de reparto no existe en este hogar."), null);

        if (req.CategoriaPadreId is { } padreId)
        {
            if (padreId == idActual) return (Mal("Una categoría no puede ser su propio padre."), null);

            var padres = await db.Categorias.ToDictionaryAsync(c => c.Id, c => c.CategoriaPadreId, ct);
            if (!padres.ContainsKey(padreId)) return (Mal("La categoría padre no existe en este hogar."), null);

            // Sin ciclos: subiendo desde el padre no debe aparecer la categoría que se edita.
            if (idActual is { } id)
            {
                var actual = (Guid?)padreId;
                var visitados = new HashSet<Guid>();
                while (actual is { } a && visitados.Add(a))
                {
                    if (a == id) return (Mal("La categoría padre crearía un ciclo."), null);
                    actual = padres.GetValueOrDefault(a);
                }
            }
        }

        var existe = await db.Categorias.AnyAsync(c =>
            c.CategoriaPadreId == req.CategoriaPadreId && c.Nombre == nombre && c.Id != idActual, ct);
        if (existe) return (Duplicada(), null);

        return (null, nombre);
    }
}
