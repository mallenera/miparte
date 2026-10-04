using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>Resumen mensual, liquidación y pagos de liquidación.</summary>
public static class LiquidacionEndpoints
{
    public static IEndpointRouteBuilder MapLiquidacion(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/resumen", ResumenAsync).RequireAuthorization();
        app.MapGet("/api/liquidacion", LiquidacionAsync).RequireAuthorization();
        app.MapPost("/api/pagos-liquidacion", CrearPagoAsync).RequireAuthorization();
        app.MapDelete("/api/pagos-liquidacion/{id:guid}", BorrarPagoAsync).RequireAuthorization();
        return app;
    }

    private static async Task<List<Gasto>> GastosDelMes(MiParteDbContext db, DateOnly inicio, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        return await db.Gastos.Include(g => g.Repartos).Where(g => g.Fecha >= inicio && g.Fecha < fin).ToListAsync(ct);
    }

    private static async Task<IResult> ResumenAsync(
        [FromQuery] string? mes, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
        var fin = inicio.AddMonths(1);

        var ingresos = await db.Ingresos.Where(i => i.Fecha >= inicio && i.Fecha < fin).Select(i => i.Importe).ToListAsync(ct);
        var gastos = await GastosDelMes(db, inicio, ct);
        var miembros = await db.Miembros.ToListAsync(ct);
        var categorias = await db.Categorias.ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);

        var ingresosTotales = ingresos.Sum();
        var gastosTotales = gastos.Sum(g => g.Importe);

        var implicados = miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).Select(m => m.Id)
            .Concat(gastos.Select(g => g.PagadoPor)).Concat(gastos.SelectMany(g => g.Repartos).Select(r => r.MiembroId))
            .ToHashSet();
        var porMiembro = miembros.Where(m => implicados.Contains(m.Id)).OrderBy(m => m.Nombre).ThenBy(m => m.Id)
            .Select(m => new ResumenMiembroDto(
                m.Id, m.Nombre,
                gastos.Where(g => g.PagadoPor == m.Id).Sum(g => g.Importe),
                gastos.SelectMany(g => g.Repartos).Where(r => r.MiembroId == m.Id).Sum(r => r.ImporteAsumido)))
            .ToList();

        var porCategoria = gastos.GroupBy(g => g.CategoriaId)
            .Select(c => new ResumenCategoriaDto(
                c.Key, categorias.GetValueOrDefault(c.Key, ""), c.Sum(g => g.Importe),
                c.SelectMany(g => g.Repartos).GroupBy(r => r.MiembroId)
                    .Select(r => new ImporteMiembroDto(r.Key, r.Sum(x => x.ImporteAsumido)))
                    .OrderBy(x => x.MiembroId).ToList()))
            .OrderBy(c => c.Nombre).ThenBy(c => c.CategoriaId).ToList();

        return Results.Ok(new ResumenMensualResponse(
            ApiComun.FormatoMes(inicio), ingresosTotales, gastosTotales, ingresosTotales - gastosTotales, porMiembro, porCategoria));
    }

    private sealed record Calculo(
        IReadOnlyList<SaldoMiembro> Saldos, IReadOnlyList<Transferencia> Transferencias,
        List<PagoLiquidacionRegistro> Pagos, Dictionary<Guid, string> Nombres, bool HayGastos);

    private static async Task<Calculo> Calcular(MiParteDbContext db, DateOnly inicio, CancellationToken ct)
    {
        var gastos = await GastosDelMes(db, inicio, ct);
        var pagos = await db.PagosLiquidacion.Where(p => p.Mes == inicio).OrderBy(p => p.Fecha).ThenBy(p => p.Id).ToListAsync(ct);
        var miembros = await db.Miembros.ToListAsync(ct);

        var ids = miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).Select(m => m.Id)
            .Concat(gastos.Select(g => g.PagadoPor)).Concat(gastos.SelectMany(g => g.Repartos).Select(r => r.MiembroId))
            .Concat(pagos.SelectMany(p => new[] { p.DeMiembroId, p.AMiembroId }))
            .Distinct().OrderBy(id => id).ToList();

        var calculados = gastos.Select(g => new GastoCalculado(
            g.PagadoPor, g.Importe, g.Repartos.Select(r => new ParteAsumida(r.MiembroId, r.ImporteAsumido)).ToList()));
        var saldos = Liquidacion.CalcularSaldos(ids, calculados, pagos.Select(p => new PagoLiquidacion(p.DeMiembroId, p.AMiembroId, p.Importe)));
        var transferencias = Liquidacion.Liquidar(saldos); // un solo miembro: sin transferencias

        return new Calculo(saldos, transferencias, pagos, miembros.ToDictionary(m => m.Id, m => m.Nombre), gastos.Count > 0);
    }

    private static async Task<IResult> LiquidacionAsync(
        [FromQuery] string? mes, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        var c = await Calcular(db, inicio, ct);
        return Results.Ok(new LiquidacionResponse(
            ApiComun.FormatoMes(inicio),
            c.Saldos.Select(s => new SaldoMiembroDto(s.MiembroId, c.Nombres.GetValueOrDefault(s.MiembroId, ""), s.Importe)).ToList(),
            c.Transferencias.Select(t => new TransferenciaDto(t.De, t.A, t.Importe)).ToList(),
            c.Pagos.Select(A).ToList()));
    }

    private static PagoLiquidacionDto A(PagoLiquidacionRegistro p)
        => new(p.Id, p.Mes, p.DeMiembroId, p.AMiembroId, p.Importe, p.Fecha, p.Concepto);

    private static async Task<IResult> CrearPagoAsync(
        CrearPagoLiquidacionRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (req.Mes.Day != 1) return ApiComun.Invalido("El mes del pago debe ser el día 1 del mes.");
        if (req.DeMiembroId == req.AMiembroId) return ApiComun.Invalido("Quien paga y quien recibe deben ser distintos.");
        var errorCampos = ApiComun.ValidarImporte(req.Importe) ?? ApiComun.ValidarConcepto(req.Concepto);
        if (errorCampos is not null) return ApiComun.Invalido(errorCampos);

        var existentes = await db.Miembros.CountAsync(m => m.Id == req.DeMiembroId || m.Id == req.AMiembroId, ct);
        if (existentes != 2) return ApiComun.Invalido("Los miembros del pago deben pertenecer al hogar.");

        // Deuda pendiente del par: lo máximo que puede pagar De sin pasarse de su deuda ni de lo que se le debe a A.
        // Si el mes no tiene gastos no es calculable y solo se valida importe > 0.
        var c = await Calcular(db, req.Mes, ct);
        if (c.HayGastos)
        {
            var deuda = -c.Saldos.FirstOrDefault(s => s.MiembroId == req.DeMiembroId)?.Importe ?? 0m;
            var credito = c.Saldos.FirstOrDefault(s => s.MiembroId == req.AMiembroId)?.Importe ?? 0m;
            var pendiente = Math.Max(0m, Math.Min(deuda, credito));
            if (req.Importe > pendiente)
                return Results.Conflict(new { error = $"El importe supera la deuda pendiente entre ambos miembros ({pendiente:0.00}).", pendiente });
        }

        var p = new PagoLiquidacionRegistro
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Mes = req.Mes, DeMiembroId = req.DeMiembroId, AMiembroId = req.AMiembroId,
            Importe = req.Importe, Fecha = req.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Concepto = ApiComun.NormalizarConcepto(req.Concepto),
        };
        db.PagosLiquidacion.Add(p);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/pagos-liquidacion/{p.Id}", A(p));
    }

    private static async Task<IResult> BorrarPagoAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var p = await db.PagosLiquidacion.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Results.NotFound();
        db.PagosLiquidacion.Remove(p);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
