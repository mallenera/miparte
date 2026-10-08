using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Cierres;

/// <summary>
/// Cierre de mes: cualquier miembro congela los gastos de un mes (y con ellos su reparto y su liquidación) y solo un admin puede reabrirlo.
/// Los demás endpoints consultan <see cref="CierreMes"/> antes de tocar gastos.
/// </summary>
public static class CierresMesEndpoints
{
    /// <summary>Registra los endpoints de /api/cierres-mes (listar, cerrar y reabrir); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCierresMes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cierres-mes").RequireAuthorization();
        g.MapGet("", ListarAsync);
        g.MapPost("", CerrarAsync);
        g.MapDelete("{mes}", ReabrirAsync);
        return app;
    }

    /// <summary>Resuelve el miembro activo vinculado al usuario de la petición; devuelve el 401/403 si no hay o no es del hogar.</summary>
    private static async Task<(Miembro? Yo, IResult? Error)> ExigirMiembroAsync(HttpContext ctx, MiParteDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(ctx.User.FindFirst("sub")?.Value, out var userId)) return (null, Results.Unauthorized());
        var yo = await db.Miembros.FirstOrDefaultAsync(m => m.UserId == userId && m.Activo, ct);
        return yo is null
            ? (null, Results.Json(new { error = "Solo un miembro activo del hogar puede cerrar un mes." }, statusCode: StatusCodes.Status403Forbidden))
            : (yo, null);
    }

    /// <summary>Como <see cref="ExigirMiembroAsync"/>, pero además exige rol admin: reabrir un mes es más delicado que cerrarlo.</summary>
    private static async Task<(Miembro? Yo, IResult? Error)> ExigirAdminAsync(HttpContext ctx, MiParteDbContext db, CancellationToken ct)
    {
        var (yo, error) = await ExigirMiembroAsync(ctx, db, ct);
        if (error is not null || yo!.Rol == RolMiembro.Admin) return (yo, error);
        return (null, Results.Json(new { error = "Solo un administrador puede reabrir un mes." }, statusCode: StatusCodes.Status403Forbidden));
    }

    /// <summary>Convierte un cierre en su DTO, con el nombre de quien lo cerró.</summary>
    private static MesCerradoDto A(MesCerrado c, IReadOnlyDictionary<Guid, string> autores)
        => new(ApiComun.FormatoMes(c.Mes), c.CerradoEn, c.CerradoPor is { } u ? autores.GetValueOrDefault(u) : null);

    /// <summary>GET /api/cierres-mes: meses cerrados del hogar, del más reciente al más antiguo. Cualquier miembro. 409 sin hogar.</summary>
    private static async Task<IResult> ListarAsync(
        [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var cierres = await db.MesesCerrados.OrderByDescending(c => c.Mes).ToListAsync(ct);
        var autores = await db.Miembros.Where(m => m.UserId != null).ToDictionaryAsync(m => m.UserId!.Value, m => m.Nombre, ct);
        return Results.Ok(cierres.Select(c => A(c, autores)).ToList());
    }

    /// <summary>POST /api/cierres-mes: cierra un mes. Cualquier miembro activo vinculado (403 si no). 400 si el mes es inválido o aún no ha terminado; 409 sin hogar o si ya está cerrado. 201 si se cierra.</summary>
    private static async Task<IResult> CerrarAsync(
        CerrarMesRequest req, HttpContext ctx, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var (yo, error) = await ExigirMiembroAsync(ctx, db, ct);
        if (error is not null) return error;
        if (!ApiComun.TryMes(req?.Mes, out var inicio)) return ApiComun.MesInvalido();
        // No se puede cerrar un mes que no ha terminado: se seguirían añadiendo gastos.
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        if (inicio.AddMonths(1) > hoy) return ApiComun.Invalido("Solo se puede cerrar un mes que ya ha terminado.");
        if (await db.MesesCerrados.AnyAsync(c => c.Mes == inicio, ct))
            return Results.Conflict(new { error = "Ese mes ya está cerrado." });

        var cierre = new MesCerrado { Id = Guid.NewGuid(), HogarId = hogarId, Mes = inicio, CerradoEn = DateTimeOffset.UtcNow, CerradoPor = yo!.UserId };
        db.MesesCerrados.Add(cierre);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (RecurrentesEndpoints.EsUnicidadViolada(ex))
        {
            // Dos cierres simultáneos del mismo mes: gana el primero y el índice único rechaza al segundo.
            return Results.Conflict(new { error = "Ese mes ya está cerrado." });
        }
        return Results.Created($"/api/cierres-mes/{ApiComun.FormatoMes(inicio)}", A(cierre, new Dictionary<Guid, string> { [yo.UserId!.Value] = yo.Nombre }));
    }

    /// <summary>DELETE /api/cierres-mes/{mes}: reabre un mes cerrado. Solo admin (403). 400 si el mes es inválido; 404 si no estaba cerrado; 409 sin hogar. 204 si se reabre.</summary>
    private static async Task<IResult> ReabrirAsync(
        string mes, HttpContext ctx, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var (_, error) = await ExigirAdminAsync(ctx, db, ct);
        if (error is not null) return error;
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
        var cierre = await db.MesesCerrados.FirstOrDefaultAsync(c => c.Mes == inicio, ct);
        if (cierre is null) return Results.NotFound();
        db.MesesCerrados.Remove(cierre);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>Consulta compartida del cierre de mes para los endpoints que modifican gastos.</summary>
public static class CierreMes
{
    /// <summary>Mensaje de error cuando se intenta tocar un mes cerrado.</summary>
    public const string Mensaje = "Ese mes está cerrado: un administrador debe reabrirlo para cambiar sus gastos.";

    /// <summary>SQLSTATE con el que los triggers de la base de datos rechazan una escritura de gasto en un mes cerrado.</summary>
    public const string SqlState = "MP409";

    /// <summary>Indica si la excepción es el rechazo de la base de datos por mes cerrado (carrera con un cierre concurrente).</summary>
    public static bool EsRechazo(DbUpdateException ex)
        => ex.InnerException is Npgsql.PostgresException { SqlState: SqlState };

    /// <summary>Respuesta 409 de mes cerrado.</summary>
    public static IResult Rechazo() => Results.Conflict(new { error = Mensaje });

    /// <summary>
    /// Guarda los cambios; si la base de datos rechaza la escritura porque otra petición cerró el mes después de la
    /// comprobación previa, devuelve el mismo 409 (y null si se guardó).
    /// </summary>
    public static async Task<IResult?> GuardarAsync(MiParteDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (EsRechazo(ex))
        {
            return Rechazo();
        }
    }

    /// <summary>Indica si el mes de la fecha está cerrado en el hogar actual.</summary>
    public static Task<bool> EstaCerradoAsync(MiParteDbContext db, DateOnly fecha, CancellationToken ct)
    {
        var inicio = new DateOnly(fecha.Year, fecha.Month, 1);
        return db.MesesCerrados.AnyAsync(c => c.Mes == inicio, ct);
    }

    /// <summary>Respuesta 409 de mes cerrado, o null si ninguna de las fechas cae en un mes cerrado.</summary>
    public static async Task<IResult?> ComprobarAsync(MiParteDbContext db, CancellationToken ct, params DateOnly[] fechas)
    {
        foreach (var f in fechas.Distinct())
            if (await EstaCerradoAsync(db, f, ct)) return Rechazo();
        return null;
    }
}
