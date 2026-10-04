using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

public static class IngresosEndpoints
{
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

    private static IngresoResponse A(Ingreso i) => new(i.Id, i.MiembroId, i.Fecha, i.Importe, i.Concepto);

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

    private static async Task<IResult> ObtenerAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var i = await db.Ingresos.FirstOrDefaultAsync(x => x.Id == id, ct);
        return i is null ? Results.NotFound() : Results.Ok(A(i));
    }

    private static async Task<string?> Validar(IngresoRequest r, MiParteDbContext db, CancellationToken ct)
    {
        var e = ApiComun.ValidarImporte(r.Importe) ?? ApiComun.ValidarConcepto(r.Concepto);
        if (e is not null) return e;
        if (r.Fecha == default) return "La fecha es obligatoria.";
        if (!await ApiComun.EsAdultoActivo(db, r.MiembroId, ct)) return "El miembro debe ser un adulto activo del hogar.";
        return null;
    }

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
