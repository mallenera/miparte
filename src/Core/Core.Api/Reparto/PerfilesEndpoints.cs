using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Reparto;

public static class PerfilesEndpoints
{
    private const int MaxLongitudNombre = 100;
    private const decimal ToleranciaPorcentaje = 0.0001m;

    public static IEndpointRouteBuilder MapPerfiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/perfiles", ListarAsync).RequireAuthorization();
        app.MapGet("/api/perfiles/{id:guid}", ObtenerAsync).RequireAuthorization();
        app.MapPost("/api/perfiles", CrearAsync).RequireAuthorization();
        app.MapPut("/api/perfiles/{id:guid}", ActualizarAsync).RequireAuthorization();
        app.MapDelete("/api/perfiles/{id:guid}", EliminarAsync).RequireAuthorization();
        return app;
    }

    internal static string ModoATexto(ModoReparto m) => m switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.Ingresos => "ingresos",
        _ => "individual",
    };

    private static bool TryModo(string? texto, out ModoReparto modo)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case "porcentaje": modo = ModoReparto.Porcentaje; return true;
            case "partes": modo = ModoReparto.Partes; return true;
            case "ingresos": modo = ModoReparto.Ingresos; return true;
            case "individual": modo = ModoReparto.Individual; return true;
            default: modo = default; return false;
        }
    }

    private static PerfilRepartoDto Dto(PerfilReparto p) => new(
        p.Id, p.Nombre, ModoATexto(p.Modo),
        p.Detalles.OrderBy(d => d.MiembroId).Select(d => new PerfilDetalleDto(d.MiembroId, d.Valor)).ToList());

    private static async Task<IResult> ListarAsync([FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var perfiles = await db.PerfilesReparto.Include(p => p.Detalles).OrderBy(p => p.Nombre).ToListAsync(ct);
        return Results.Ok(perfiles.Select(Dto).ToList());
    }

    private static async Task<IResult> ObtenerAsync(Guid id, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var p = await db.PerfilesReparto.Include(x => x.Detalles).FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? Results.NotFound() : Results.Ok(Dto(p));
    }

    private static async Task<IResult> CrearAsync(
        GuardarPerfilRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        var (error, nombre, modo, detalle) = await ValidarAsync(req, null, db, ct);
        if (error is not null) return error;

        var hogarId = hogar.HogarId!.Value;
        var perfil = new PerfilReparto { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = nombre!, Modo = modo };
        perfil.Detalles.AddRange(detalle!.Select(d => Nuevo(hogarId, perfil.Id, d)));
        db.PerfilesReparto.Add(perfil);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Duplicado(); }
        return Results.Created($"/api/perfiles/{perfil.Id}", Dto(perfil));
    }

    private static async Task<IResult> ActualizarAsync(
        Guid id, GuardarPerfilRequest req, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var perfil = await db.PerfilesReparto.Include(p => p.Detalles).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (perfil is null) return Results.NotFound();

        var (error, nombre, modo, detalle) = await ValidarAsync(req, id, db, ct);
        if (error is not null) return error;

        perfil.Nombre = nombre!;
        perfil.Modo = modo;
        db.PerfilesRepartoDetalle.RemoveRange(perfil.Detalles);
        perfil.Detalles.Clear();
        foreach (var d in detalle!)
        {
            var nuevo = Nuevo(perfil.HogarId, perfil.Id, d);
            perfil.Detalles.Add(nuevo);
            db.PerfilesRepartoDetalle.Add(nuevo);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Duplicado(); }
        return Results.Ok(Dto(perfil));
    }

    private static async Task<IResult> EliminarAsync(Guid id, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var perfil = await db.PerfilesReparto.Include(p => p.Detalles).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (perfil is null) return Results.NotFound();

        if (await db.Categorias.AnyAsync(c => c.PerfilRepartoId == id, ct)
            || await db.Gastos.AnyAsync(g => g.PerfilRepartoId == id, ct)
            || await db.GastosRecurrentes.AnyAsync(g => g.PerfilRepartoId == id, ct))
            return Results.Conflict(new { error = "El perfil está en uso por categorías, gastos o gastos recurrentes." });

        db.PerfilesRepartoDetalle.RemoveRange(perfil.Detalles);
        db.PerfilesReparto.Remove(perfil);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "El perfil está en uso." }); }
        return Results.NoContent();
    }

    private static PerfilRepartoDetalle Nuevo(Guid hogarId, Guid perfilId, PerfilDetalleDto d) => new()
    {
        Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = perfilId, MiembroId = d.MiembroId, Valor = d.Valor,
    };

    private static IResult Duplicado() => Results.Conflict(new { error = "Ya existe un perfil con ese nombre." });

    private static IResult Mal(string msg) => Results.BadRequest(new { error = msg });

    private static async Task<(IResult? Error, string? Nombre, ModoReparto Modo, IReadOnlyList<PerfilDetalleDto>? Detalle)> ValidarAsync(
        GuardarPerfilRequest req, Guid? idActual, MiParteDbContext db, CancellationToken ct)
    {
        var nombre = req.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return (Mal("El nombre es obligatorio."), null, default, null);
        if (nombre.Length > MaxLongitudNombre)
            return (Mal($"El nombre admite como máximo {MaxLongitudNombre} caracteres."), null, default, null);
        if (!TryModo(req.Modo, out var modo))
            return (Mal("Modo no válido: use porcentaje, partes, ingresos o individual."), null, default, null);

        var detalle = req.Detalle ?? [];
        if (modo is ModoReparto.Ingresos or ModoReparto.Individual)
        {
            if (detalle.Count > 0)
                return (Mal($"El modo {ModoATexto(modo)} no lleva detalle."), null, default, null);
        }
        else
        {
            if (detalle.Count == 0) return (Mal("El detalle es obligatorio en este modo."), null, default, null);
            if (detalle.Select(d => d.MiembroId).Distinct().Count() != detalle.Count)
                return (Mal("Hay miembros repetidos en el detalle."), null, default, null);

            var ids = detalle.Select(d => d.MiembroId).ToList();
            var activos = await db.Miembros.Where(m => m.Activo && ids.Contains(m.Id)).CountAsync(ct);
            if (activos != ids.Count)
                return (Mal("El detalle solo puede incluir miembros activos del hogar."), null, default, null);

            if (detalle.Any(d => d.Valor < 0)) return (Mal("Los valores no pueden ser negativos."), null, default, null);
            if (modo == ModoReparto.Porcentaje)
            {
                if (Math.Abs(detalle.Sum(d => d.Valor) - 100m) > ToleranciaPorcentaje)
                    return (Mal("Los porcentajes deben sumar 100."), null, default, null);
            }
            else if (detalle.All(d => d.Valor == 0))
                return (Mal("Alguna parte debe ser mayor que 0."), null, default, null);
        }

        if (await db.PerfilesReparto.AnyAsync(p => p.Nombre == nombre && p.Id != idActual, ct))
            return (Duplicado(), null, default, null);

        return (null, nombre, modo, detalle);
    }
}
