using MiParte.Core.Domain;
using MiParte.Core.Api.Seguridad;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Auditoria;

/// <summary>Historial de cambios del hogar. Solo lectura y solo con el permiso <c>historial.ver</c> (los admins lo tienen siempre): los eventos los escribe la propia API al guardar.</summary>
public static class AuditoriaEndpoints
{
    /// <summary>Eventos devueltos por defecto en una página.</summary>
    private const int LimitePorDefecto = 50;

    /// <summary>Máximo de eventos por página.</summary>
    private const int LimiteMaximo = 200;

    /// <summary>Registra GET /api/auditoria; requiere autorización y el permiso <c>historial.ver</c>.</summary>
    public static IEndpointRouteBuilder MapAuditoria(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auditoria", ListarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HistorialVer);
        return app;
    }

    /// <summary>
    /// GET /api/auditoria?entidad=&amp;entidadId=&amp;limite=&amp;hasta=&amp;despuesDeId=: eventos del hogar de más reciente a más
    /// antiguo (empates por id). Se pagina con un cursor compuesto: <c>hasta</c> y <c>despuesDeId</c> son el <c>cuando</c> y el
    /// <c>id</c> del último evento recibido. Solo con el instante se perderían los eventos de un mismo guardado, que
    /// comparten <c>cuando</c>. 403 sin el permiso <c>historial.ver</c>; 400 si el límite no es 1-200; 409 sin hogar.
    /// </summary>
    private static async Task<IResult> ListarAsync(
        HttpContext ctx, [FromQuery] string? entidad, [FromQuery] Guid? entidadId, [FromQuery] int? limite,
        [FromQuery] DateTimeOffset? hasta, [FromQuery] Guid? despuesDeId, [FromServices] MiParteDbContext db,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();


        var n = limite ?? LimitePorDefecto;
        if (n is < 1 or > LimiteMaximo) return ApiComun.Invalido($"El límite debe estar entre 1 y {LimiteMaximo}.");

        var q = db.Auditoria.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entidad)) q = q.Where(e => e.Entidad == entidad);
        if (entidadId is { } id) q = q.Where(e => e.EntidadId == id);
        if (hasta is { } corte)
        {
            // Mismo orden que el OrderBy (cuando desc, id asc): lo siguiente es más antiguo, o igual de reciente con id mayor.
            q = despuesDeId is { } cursorId
                ? q.Where(e => e.Cuando < corte || (e.Cuando == corte && e.Id.CompareTo(cursorId) > 0))
                : q.Where(e => e.Cuando < corte);
        }

        var eventos = await q.OrderByDescending(e => e.Cuando).ThenBy(e => e.Id).Take(n).ToListAsync(ct);
        var autores = await db.Miembros.Where(m => m.UserId != null)
            .ToDictionaryAsync(m => m.UserId!.Value, m => m.Nombre, ct);

        return Results.Ok(eventos.Select(e => new EventoAuditoriaDto(
            e.Id, e.Cuando, e.UsuarioId, e.UsuarioId is { } u ? autores.GetValueOrDefault(u) : null,
            e.Accion, e.Entidad, e.EntidadId, Parsear(e.Antes), Parsear(e.Despues))).ToList());
    }

    /// <summary>Convierte el JSON guardado en un elemento serializable; null si no hay.</summary>
    private static JsonElement? Parsear(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
