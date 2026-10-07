using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Api.Gastos;

/// <summary>Cuenta común: aportaciones mensuales, saldo y reembolsos a quien adelantó gastos de la cuenta.</summary>
public static class CuentaComunEndpoints
{
    /// <summary>Registra los endpoints de la cuenta común; todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCuentaComun(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cuenta-comun", EstadoAsync).RequireAuthorization();
        app.MapPut("/api/cuenta-comun/aportaciones", FijarAportacionAsync).RequireAuthorization();
        app.MapPost("/api/cuenta-comun/reembolsos", CrearReembolsoAsync).RequireAuthorization();
        app.MapDelete("/api/cuenta-comun/reembolsos/{id:guid}", BorrarReembolsoAsync).RequireAuthorization();
        return app;
    }

    /// <summary>Convierte una aportación en su DTO de respuesta.</summary>
    private static AportacionCuentaDto A(AportacionCuenta a) => new(a.Id, a.MiembroId, a.Desde, a.Importe);

    /// <summary>Convierte un reembolso en su DTO de respuesta.</summary>
    private static ReembolsoCuentaDto A(ReembolsoCuenta r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Gastos cargados a la cuenta común, para el cálculo de saldo.</summary>
    private static async Task<List<GastoDeCuenta>> GastosDeCuenta(MiParteDbContext db, CancellationToken ct)
        => await db.Gastos.Where(g => g.ACargoCuentaComun)
            .Select(g => new GastoDeCuenta(g.PagadoPor, g.Fecha, g.Importe)).ToListAsync(ct);

    /// <summary>GET /api/cuenta-comun?mes=YYYY-MM: saldo acumulado, reembolsos pendientes, aportaciones y reembolsos del mes. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> EstadoAsync(
        [FromQuery] string? mes, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
        var fin = inicio.AddMonths(1);

        var aportaciones = await db.AportacionesCuenta.OrderBy(a => a.MiembroId).ThenBy(a => a.Desde).ToListAsync(ct);
        var reembolsos = await db.ReembolsosCuenta.OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToListAsync(ct);
        var nombres = await db.Miembros.ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);

        var e = CuentaComun.Calcular(
            inicio, aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe)).ToList(),
            await GastosDeCuenta(db, ct), reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe)));

        return Results.Ok(new CuentaComunResponse(
            ApiComun.FormatoMes(inicio), e.AportadoMes, e.Aportado, e.Gastado, e.Saldo,
            e.Pendientes.Select(p => new PendienteCuentaDto(p.MiembroId, nombres.GetValueOrDefault(p.MiembroId, ""), p.Importe)).ToList(),
            e.Efectivo, aportaciones.Select(A).ToList(),
            reembolsos.Where(r => r.Fecha >= inicio && r.Fecha < fin).Select(A).ToList()));
    }

    /// <summary>PUT /api/cuenta-comun/aportaciones: fija la aportación de un adulto desde un mes (si ya había una de ese mes, la sustituye). 400 si el mes no es día 1, el importe es inválido o el miembro no es adulto activo; 409 sin hogar.</summary>
    private static async Task<IResult> FijarAportacionAsync(
        FijarAportacionRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (req.Desde.Day != 1) return ApiComun.Invalido("El mes de la aportación debe ser el día 1 del mes.");
        // Una aportación de 0 es válida: deja de aportar desde ese mes.
        var errorImporte = req.Importe < 0 ? "El importe no puede ser negativo."
            : req.Importe == 0 ? null : ApiComun.ValidarImporte(req.Importe);
        if (errorImporte is not null) return ApiComun.Invalido(errorImporte);
        if (!await ApiComun.EsAdultoActivo(db, req.MiembroId, ct))
            return ApiComun.Invalido("Solo los adultos activos del hogar aportan a la cuenta común.");

        var a = await db.AportacionesCuenta.FirstOrDefaultAsync(x => x.MiembroId == req.MiembroId && x.Desde == req.Desde, ct);
        if (a is null)
        {
            a = new AportacionCuenta { Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = req.MiembroId, Desde = req.Desde };
            db.AportacionesCuenta.Add(a);
        }
        a.Importe = req.Importe;
        await db.SaveChangesAsync(ct);
        return Results.Ok(A(a));
    }

    /// <summary>POST /api/cuenta-comun/reembolsos: registra un pago de la cuenta a quien adelantó gastos. 400 si el importe es inválido o el miembro no es del hogar; 409 sin hogar o si supera lo pendiente de reembolsar al miembro. 201 si se crea.</summary>
    private static async Task<IResult> CrearReembolsoAsync(
        CrearReembolsoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var error = ApiComun.ValidarImporte(req.Importe) ?? ApiComun.ValidarConcepto(req.Concepto);
        if (error is not null) return ApiComun.Invalido(error);
        if (!await db.Miembros.AnyAsync(m => m.Id == req.MiembroId, ct))
            return ApiComun.Invalido("El miembro del reembolso debe pertenecer al hogar.");

        var pendiente = await db.Gastos.Where(g => g.ACargoCuentaComun && g.PagadoPor == req.MiembroId).SumAsync(g => g.Importe, ct)
            - await db.ReembolsosCuenta.Where(r => r.MiembroId == req.MiembroId).SumAsync(r => r.Importe, ct);
        if (req.Importe > pendiente)
            return Results.Conflict(new { error = $"El importe supera lo pendiente de reembolsar al miembro ({Math.Max(0m, pendiente):0.00}).", pendiente = Math.Max(0m, pendiente) });

        var r = new ReembolsoCuenta
        {
            Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = req.MiembroId, Importe = req.Importe,
            Fecha = req.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow), Concepto = ApiComun.NormalizarConcepto(req.Concepto),
        };
        db.ReembolsosCuenta.Add(r);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/cuenta-comun/reembolsos/{r.Id}", A(r));
    }

    /// <summary>DELETE /api/cuenta-comun/reembolsos/{id}: borra un reembolso. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarReembolsoAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await db.ReembolsosCuenta.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        db.ReembolsosCuenta.Remove(r);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
