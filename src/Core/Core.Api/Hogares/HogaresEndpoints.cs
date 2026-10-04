using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>Marca endpoints que funcionan sin hogar seleccionado (alta y listado de hogares).</summary>
public sealed record SinHogarActual;

public static class HogaresEndpoints
{
    /// <summary>Tope por usuario, para que un registro gratuito no llene la base de datos.</summary>
    public const int MaxHogaresPorUsuario = 10;
    private const int MaxLongitudNombre = 100;

    public static IEndpointRouteBuilder MapHogares(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/hogares", ListarAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        app.MapPost("/api/hogares", CrearAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        app.MapGet("/api/hogares/{id:guid}", ObtenerAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        app.MapGet("/api/yo", YoAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        return app;
    }

    private static async Task<List<HogarResumen>> HogaresDelUsuario(MiParteDbContext db, Guid userId, CancellationToken ct)
        => await db.Miembros.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Activo)
            .Join(db.Hogares.IgnoreQueryFilters(), m => m.HogarId, h => h.Id,
                (_, h) => new HogarResumen(h.Id, h.Nombre))
            .Distinct()
            .OrderBy(h => h.Nombre)
            .ToListAsync(ct);

    private static async Task<IResult> YoAsync(
        HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        var hogares = await HogaresDelUsuario(db, userId, ct);

        // Hogar actual: el de la cabecera si es válido y propio; si no hay cabecera, el único hogar.
        HogarResumen? actual = null;
        if (ctx.Request.Headers.TryGetValue(HogarActualMiddleware.Cabecera, out var valor))
        {
            if (Guid.TryParse(valor.ToString(), out var pedido))
                actual = hogares.FirstOrDefault(h => h.Id == pedido);
        }
        else if (hogares.Count == 1)
        {
            actual = hogares[0];
        }

        return Results.Ok(new YoResponse(userId.ToString(), hogares, actual));
    }

    private static async Task<IResult> ObtenerAsync(
        Guid id, HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        // 404 tanto si no existe como si no eres miembro: no se revela la existencia.
        var hogar = (await HogaresDelUsuario(db, userId, ct)).FirstOrDefault(h => h.Id == id);
        return hogar is null ? Results.NotFound() : Results.Ok(hogar);
    }

    private static async Task<IResult> ListarAsync(
        HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        var hogares = await db.Miembros.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Activo)
            .Join(db.Hogares.IgnoreQueryFilters(), m => m.HogarId, h => h.Id,
                (_, h) => new HogarResumen(h.Id, h.Nombre))
            .OrderBy(h => h.Nombre)
            .ToListAsync(ct);

        return Results.Ok(hogares);
    }

    private static async Task<IResult> CrearAsync(
        CrearHogarRequest req, HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        var nombreHogar = req.NombreHogar?.Trim();
        var nombreMiembro = req.NombreMiembro?.Trim();
        if (string.IsNullOrEmpty(nombreHogar) || string.IsNullOrEmpty(nombreMiembro))
            return Results.BadRequest(new { error = "El nombre del hogar y el tuyo son obligatorios." });
        if (nombreHogar.Length > MaxLongitudNombre || nombreMiembro.Length > MaxLongitudNombre)
            return Results.BadRequest(new { error = $"Los nombres admiten como máximo {MaxLongitudNombre} caracteres." });

        var actuales = await db.Miembros.IgnoreQueryFilters().CountAsync(m => m.UserId == userId, ct);
        if (actuales >= MaxHogaresPorUsuario)
            return Results.Conflict(new { error = $"Has alcanzado el máximo de {MaxHogaresPorUsuario} hogares." });

        var hogar = new Hogar { Id = Guid.NewGuid(), Nombre = nombreHogar };
        db.Hogares.Add(hogar);
        var creador = new Miembro
        {
            Id = Guid.NewGuid(),
            HogarId = hogar.Id,
            Nombre = nombreMiembro,
            Tipo = TipoMiembro.Adulto,
            Rol = RolMiembro.Admin,
            UserId = userId,
        };
        db.Miembros.Add(creador);
        SemillaHogar.Sembrar(db, hogar.Id, creador.Id); // perfiles y categorías por defecto, como crear_hogar en SQL
        await db.SaveChangesAsync(ct); // una sola transacción: o se crea todo o nada

        return Results.Created($"/api/hogares/{hogar.Id}", new HogarResumen(hogar.Id, hogar.Nombre));
    }

    private static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);
}
