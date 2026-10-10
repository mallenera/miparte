using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Auditoria;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Hogares;

/// <summary>Marca endpoints que funcionan sin hogar seleccionado (alta y listado de hogares).</summary>
public sealed record SinHogarActual;

/// <summary>
/// Endpoints de hogares del usuario autenticado: listar, crear, obtener y /api/yo. Todos exigen
/// autenticación y funcionan sin hogar seleccionado (<see cref="SinHogarActual"/>).
/// </summary>
public static class HogaresEndpoints
{
    /// <summary>Tope por usuario, para que un registro gratuito no llene la base de datos.</summary>
    public const int MaxHogaresPorUsuario = 10;

    /// <summary>Longitud máxima del nombre de hogar y del nombre de miembro.</summary>
    private const int MaxLongitudNombre = 100;

    /// <summary>
    /// Registra GET /api/hogares, POST /api/hogares, GET /api/hogares/{id} y GET /api/yo,
    /// todos con autorización y sin necesidad de hogar actual.
    /// </summary>
    public static IEndpointRouteBuilder MapHogares(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/hogares", ListarAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        app.MapPost("/api/hogares", CrearAsync)
            .RequireAuthorization()
            .RequireRateLimiting(LimitacionPeticiones.Costosa)
            .WithMetadata(new SinHogarActual());

        app.MapGet("/api/hogares/{id:guid}", ObtenerAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        // No lleva SinHogarActual: actúa sobre el hogar de la cabecera X-Hogar-Id, y así el filtro de permisos sabe de qué hogar se trata.
        app.MapDelete("/api/hogar", EliminarAsync)
            .RequireAuthorization()
            .RequierePermiso(CatalogoPermisos.HogarEliminar)
            .RequireRateLimiting(LimitacionPeticiones.Costosa);

        app.MapGet("/api/yo", YoAsync)
            .RequireAuthorization()
            .WithMetadata(new SinHogarActual());

        return app;
    }

    /// <summary>
    /// DELETE /api/hogar?nombre=: elimina el hogar actual y, en cascada, todos sus datos (miembros, gastos, cuenta común, historial...).
    /// No tiene vuelta atrás. Exige el permiso <c>hogar.eliminar</c> (403) y que <paramref name="nombre"/> sea el nombre exacto del
    /// hogar (400), como confirmación. 409 sin hogar; 204 si se elimina.
    /// </summary>
    private static async Task<IResult> EliminarAsync(
        [FromQuery] string? nombre, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } id) return ApiComun.SinHogar();
        var h = await db.Hogares.FirstAsync(ct);
        if (!string.Equals(nombre?.Trim(), h.Nombre, StringComparison.Ordinal))
            return ApiComun.Invalido("Escribe el nombre exacto del hogar para confirmar que quieres eliminarlo.");

        if (db.Database.IsRelational())
        {
            // Borrado directo: la base de datos elimina en cascada el resto de filas (y la auditoría del hogar, que solo admite ese borrado).
            await db.Hogares.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        }
        else
        {
            // Proveedor InMemory (tests): no admite ExecuteDelete ni borrado en cascada; se quitan el hogar y sus miembros.
            db.Miembros.RemoveRange(await db.Miembros.ToListAsync(ct));
            db.Hogares.Remove(h);
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }

    /// <summary>
    /// Hogares donde el usuario es miembro activo. Se proyecta a columnas y se mapea después:
    /// un Distinct sobre un record no se traduce a SQL en Npgsql (InMemory sí lo admitía).
    /// No hace falta Distinct: miembro tiene unique (hogar_id, user_id).
    /// </summary>
    private static async Task<List<HogarResumen>> HogaresDelUsuario(MiParteDbContext db, Guid userId, CancellationToken ct)
    {
        var filas = await db.Hogares.IgnoreQueryFilters()
            .Where(h => db.Miembros.IgnoreQueryFilters()
                .Any(m => m.HogarId == h.Id && m.UserId == userId && m.Activo))
            .OrderBy(h => h.Nombre)
            .Select(h => new { h.Id, h.Nombre, h.CuentaComunActiva, h.AhorroActivo })
            .ToListAsync(ct);
        return filas.Select(h => new HogarResumen(h.Id, h.Nombre, h.CuentaComunActiva, h.AhorroActivo)).ToList();
    }

    /// <summary>
    /// GET /api/yo: devuelve el id del usuario, sus hogares y el hogar actual (el de la cabecera
    /// X-Hogar-Id si es válido y propio, o el único hogar si no hay cabecera). 401 sin claim "sub".
    /// </summary>
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

    /// <summary>
    /// GET /api/hogares/{id}: resumen de un hogar del usuario. 404 si no existe o no es miembro
    /// (no se revela su existencia); 401 sin claim "sub".
    /// </summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        // 404 tanto si no existe como si no eres miembro: no se revela la existencia.
        var hogar = (await HogaresDelUsuario(db, userId, ct)).FirstOrDefault(h => h.Id == id);
        return hogar is null ? Results.NotFound() : Results.Ok(hogar);
    }

    /// <summary>GET /api/hogares: hogares donde el usuario es miembro activo, ordenados por nombre. 401 sin claim "sub".</summary>
    private static async Task<IResult> ListarAsync(
        HttpContext ctx, [FromServices] MiParteDbContext db, CancellationToken ct)
    {
        if (!TryUsuario(ctx, out var userId)) return Results.Unauthorized();

        return Results.Ok(await HogaresDelUsuario(db, userId, ct));
    }

    /// <summary>
    /// POST /api/hogares: crea un hogar y a su creador como miembro adulto y admin, con la semilla por
    /// defecto, todo en una transacción. 400 si faltan nombres o superan la longitud máxima; 409 si el
    /// usuario ya está en <see cref="MaxHogaresPorUsuario"/> hogares; 201 con el resumen del hogar.
    /// </summary>
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

        // El modelo EF solo conoce dos relaciones (las FK compuestas las aplica el SQL), así que no ordena
        // los INSERT por padres e hijos: se guarda por etapas (hogar → miembro → perfiles → detalle y
        // categorías) dentro de una transacción, y si algo falla se deshace todo. InMemory no admite transacciones.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        // La semilla (perfiles, categorías, detalles) son decenas de filas que no aportan: se resume en un solo evento.
        db.AuditoriaActiva = false;
        var hogar = new Hogar { Id = Guid.NewGuid(), Nombre = nombreHogar };
        db.Hogares.Add(hogar);
        await db.SaveChangesAsync(ct);
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
        await db.SaveChangesAsync(ct);
        await SemillaHogar.SembrarAsync(db, hogar.Id, creador.Id, ct); // perfiles y categorías por defecto, como crear_hogar en SQL
        db.AuditoriaActiva = true;
        db.Auditoria.Add(RegistroAuditoria.Manual(
            hogar.Id, userId, RegistroAuditoria.Crear, "hogar", hogar.Id, despues: new { nombre = nombreHogar }));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        return Results.Created($"/api/hogares/{hogar.Id}", new HogarResumen(hogar.Id, hogar.Nombre));
    }

    /// <summary>Obtiene el id del usuario autenticado del claim "sub"; false si falta o no es un GUID.</summary>
    private static bool TryUsuario(HttpContext ctx, out Guid userId)
        => Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out userId);
}
