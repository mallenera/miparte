using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.Auditoria;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Auditoria;

/// <summary>
/// Historial de cambios del hogar. Solo lectura y solo con el permiso <c>historial.ver</c> (los admins lo tienen siempre): los
/// eventos los escribe la propia API al guardar. Delega en <see cref="ServicioAuditoria"/>.
/// </summary>
public static class AuditoriaEndpoints
{
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
        [FromQuery] string? entidad, [FromQuery] Guid? entidadId, [FromQuery] int? limite,
        [FromQuery] DateTimeOffset? hasta, [FromQuery] Guid? despuesDeId, [FromServices] ServicioAuditoria servicio,
        [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();

        var r = await servicio.ListarAsync(new ConsultaAuditoria(entidad, entidadId, limite, hasta, despuesDeId), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);

        var autores = r.Valor!.Autores;
        return Results.Ok(r.Valor.Eventos.Select(e => new EventoAuditoriaDto(
            e.Id, e.Cuando, e.UsuarioId, e.UsuarioId is { } u ? autores.GetValueOrDefault(u) : null,
            e.Accion, e.Entidad, e.EntidadId, Parsear(e.Antes), Parsear(e.Despues), e.Origen)).ToList());
    }

    /// <summary>Convierte el JSON guardado en un elemento serializable; null si no hay.</summary>
    private static JsonElement? Parsear(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
