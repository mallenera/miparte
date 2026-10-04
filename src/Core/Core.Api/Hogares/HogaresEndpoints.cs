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

        return app;
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
        db.Miembros.Add(new Miembro
        {
            Id = Guid.NewGuid(),
            HogarId = hogar.Id,
            Nombre = nombreMiembro,
            Tipo = TipoMiembro.Adulto,
            UserId = userId,
        });
        await db.SaveChangesAsync(ct); // una sola transacción: o se crean ambos o ninguno

        return Results.Created($"/api/hogares/{hogar.Id}", new HogarResumen(hogar.Id, hogar.Nombre));
    }

    private static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);
}
