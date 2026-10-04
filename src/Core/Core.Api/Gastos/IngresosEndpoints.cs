using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>Endpoints de ingresos de los adultos del hogar; los ingresos del mes sirven de base al reparto por ingresos.</summary>
public static class IngresosEndpoints
{
    /// <summary>Registra los endpoints de /api/ingresos (listar, obtener, crear, editar y borrar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapIngresos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ingresos").RequireAuthorization();
        g.MapGet("", ListarAsync);
        g.MapGet("{id:guid}", ObtenerAsync);
        g.MapPost("", CrearAsync);
        g.MapPut("{id:guid}", EditarAsync);
        g.MapDelete("{id:guid}", BorrarAsync);
        return app;
    }

    /// <summary>Convierte un ingreso en su DTO de respuesta.</summary>
    private static IngresoResponse A(Ingreso i) => new(i.Id, i.MiembroId, i.Fecha, i.Importe, i.Concepto);

    /// <summary>GET /api/ingresos: lista los ingresos, filtrables por mes (YYYY-MM), de más reciente a más antiguo. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> ListarAsync(
        [FromQuery] string? mes, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var q = db.Ingresos.AsQueryable();
        if (mes is not null)
        {
            if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
            var fin = inicio.AddMonths(1);
            q = q.Where(i => i.Fecha >= inicio && i.Fecha < fin);
        }
        var lista = await q.OrderByDescending(i => i.Fecha).ThenBy(i => i.Id).ToListAsync(ct);
        return Results.Ok(lista.Select(A));
    }

    /// <summary>GET /api/ingresos/{id}: devuelve un ingreso. 409 sin hogar; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var i = await db.Ingresos.FirstOrDefaultAsync(x => x.Id == id, ct);
        return i is null ? Results.NotFound() : Results.Ok(A(i));
    }

    /// <summary>Valida importe, concepto, fecha obligatoria y que el miembro sea un adulto activo; devuelve el mensaje de error o null.</summary>
    private static async Task<string?> Validar(IngresoRequest r, MiParteDbContext db, CancellationToken ct)
    {
        var e = ApiComun.ValidarImporte(r.Importe) ?? ApiComun.ValidarConcepto(r.Concepto);
        if (e is not null) return e;
        if (r.Fecha == default) return "La fecha es obligatoria.";
        if (!await ApiComun.EsAdultoActivo(db, r.MiembroId, ct)) return "El miembro debe ser un adulto activo del hogar.";
        return null;
    }

    /// <summary>POST /api/ingresos: registra un ingreso. 201 si se crea; 409 sin hogar; 400 si no es válido.</summary>
    private static async Task<IResult> CrearAsync(
        IngresoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (await Validar(req, db, ct) is { } error) return ApiComun.Invalido(error);

        var i = new Ingreso
        {
            Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = req.MiembroId, Fecha = req.Fecha,
            Importe = req.Importe, Concepto = ApiComun.NormalizarConcepto(req.Concepto),
        };
        db.Ingresos.Add(i);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/ingresos/{i.Id}", A(i));
    }

    /// <summary>PUT /api/ingresos/{id}: actualiza un ingreso. 409 sin hogar; 404 si no existe; 400 si no es válido.</summary>
    private static async Task<IResult> EditarAsync(
        Guid id, IngresoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var i = await db.Ingresos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Results.NotFound();
        if (await Validar(req, db, ct) is { } error) return ApiComun.Invalido(error);

        i.MiembroId = req.MiembroId;
        i.Fecha = req.Fecha;
        i.Importe = req.Importe;
        i.Concepto = ApiComun.NormalizarConcepto(req.Concepto);
        await db.SaveChangesAsync(ct);
        return Results.Ok(A(i));
    }

    /// <summary>DELETE /api/ingresos/{id}: borra un ingreso. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var i = await db.Ingresos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Results.NotFound();
        db.Ingresos.Remove(i);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
