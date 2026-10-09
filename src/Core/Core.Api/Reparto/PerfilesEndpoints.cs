using MiParte.Core.Domain;
using MiParte.Core.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Reparto;

/// <summary>Endpoints de perfiles de reparto del hogar (porcentaje, partes, cuenta común o individual).</summary>
public static class PerfilesEndpoints
{
    /// <summary>Longitud máxima del nombre de un perfil.</summary>
    private const int MaxLongitudNombre = 100;
    /// <summary>Tolerancia admitida al comprobar que los porcentajes suman 100.</summary>
    private const decimal ToleranciaPorcentaje = 0.0001m;

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

    /// <summary>Convierte el modo de reparto al texto usado en la API (porcentaje, partes, cuenta_comun o individual).</summary>
    internal static string ModoATexto(ModoReparto m) => m switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.CuentaComun => "cuenta_comun",
        _ => "individual",
    };

    /// <summary>Interpreta el texto del modo (sin distinguir mayúsculas ni espacios laterales); devuelve false si no es válido.</summary>
    private static bool TryModo(string? texto, out ModoReparto modo)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case "porcentaje": modo = ModoReparto.Porcentaje; return true;
            case "partes": modo = ModoReparto.Partes; return true;
            case "cuenta_comun": modo = ModoReparto.CuentaComun; return true;
            case "individual": modo = ModoReparto.Individual; return true;
            default: modo = default; return false;
        }
    }

    /// <summary>Convierte un perfil en su DTO de respuesta, con el detalle ordenado por miembro.</summary>
    private static PerfilRepartoDto Dto(PerfilReparto p) => new(
        p.Id, p.Nombre, ModoATexto(p.Modo),
        p.Detalles.OrderBy(d => d.MiembroId).Select(d => new PerfilDetalleDto(d.MiembroId, d.Valor)).ToList());

    /// <summary>GET /api/perfiles: devuelve los perfiles del hogar con su detalle, ordenados por nombre.</summary>
    private static async Task<IResult> ListarAsync([FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var perfiles = await db.PerfilesReparto.Include(p => p.Detalles).OrderBy(p => p.Nombre).ToListAsync(ct);
        return Results.Ok(perfiles.Select(Dto).ToList());
    }

    /// <summary>GET /api/perfiles/{id}: devuelve un perfil con su detalle; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(Guid id, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        var p = await db.PerfilesReparto.Include(x => x.Detalles).FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? Results.NotFound() : Results.Ok(Dto(p));
    }

    /// <summary>POST /api/perfiles: crea un perfil con su detalle. 201 si se crea; 400 si no supera la validación; 409 si el nombre ya existe.</summary>
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

    /// <summary>PUT /api/perfiles/{id}: sustituye nombre, modo y detalle del perfil. 404 si no existe; 400 si no es válido; 409 si el nombre ya existe.</summary>
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

    /// <summary>DELETE /api/perfiles/{id}: elimina el perfil y su detalle. 404 si no existe; 409 si lo usan categorías, gastos o gastos recurrentes; 204 si se elimina.</summary>
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

    /// <summary>Crea una fila de detalle de perfil para el miembro y valor indicados.</summary>
    private static PerfilRepartoDetalle Nuevo(Guid hogarId, Guid perfilId, PerfilDetalleDto d) => new()
    {
        Id = Guid.NewGuid(), HogarId = hogarId, PerfilId = perfilId, MiembroId = d.MiembroId, Valor = d.Valor,
    };

    /// <summary>Respuesta 409 para un perfil con nombre repetido.</summary>
    private static IResult Duplicado() => Results.Conflict(new { error = "Ya existe un perfil con ese nombre." });

    /// <summary>Respuesta 400 con el mensaje de error indicado.</summary>
    private static IResult Mal(string msg) => Results.BadRequest(new { error = msg });

    /// <summary>Valida el cuerpo y devuelve nombre normalizado, modo y detalle, o el resultado de error. Cuenta común e individual no admiten detalle; porcentaje y partes exigen adultos activos sin repetir y valores no negativos, con porcentajes que suman 100 o alguna parte mayor que 0. El nombre no puede repetirse.</summary>
    private static async Task<(IResult? Error, string? Nombre, ModoReparto Modo, IReadOnlyList<PerfilDetalleDto>? Detalle)> ValidarAsync(
        GuardarPerfilRequest req, Guid? idActual, MiParteDbContext db, CancellationToken ct)
    {
        var nombre = req.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return (Mal("El nombre es obligatorio."), null, default, null);
        if (nombre.Length > MaxLongitudNombre)
            return (Mal($"El nombre admite como máximo {MaxLongitudNombre} caracteres."), null, default, null);
        if (!TryModo(req.Modo, out var modo))
            return (Mal("Modo no válido: use porcentaje, partes, cuenta_comun o individual."), null, default, null);

        var detalle = req.Detalle ?? [];
        if (modo is ModoReparto.CuentaComun or ModoReparto.Individual)
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
            var activos = await db.Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto && ids.Contains(m.Id)).CountAsync(ct);
            if (activos != ids.Count)
                return (Mal("El detalle solo puede incluir adultos activos del hogar."), null, default, null);

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
