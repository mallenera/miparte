using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>
/// Resumen mensual, liquidación y pagos de liquidación. Los endpoints solo validan el formato HTTP, delegan en
/// <see cref="ServicioLiquidacion"/> y traducen su resultado a DTOs y códigos de estado.
/// </summary>
public static class LiquidacionEndpoints
{
    /// <summary>Registra los endpoints de resumen mensual, liquidación y pagos de liquidación; todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapLiquidacion(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/resumen", ResumenAsync).RequireAuthorization();
        app.MapGet("/api/liquidacion", LiquidacionAsync).RequireAuthorization();
        app.MapGet("/api/resumen-liquidacion", ResumenYLiquidacionAsync).RequireAuthorization();
        app.MapPost("/api/pagos-liquidacion", CrearPagoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.PagosRegistrar);
        app.MapDelete("/api/pagos-liquidacion/{id:guid}", BorrarPagoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.PagosRegistrar);
        return app;
    }

    /// <summary>Convierte el resumen del dominio en la respuesta de la API.</summary>
    private static ResumenMensualResponse A(ResumenDelMes r) => new(
        ApiComun.FormatoMes(r.Mes), r.GastosTotales,
        r.PorMiembro.Select(m => new ResumenMiembroDto(m.MiembroId, m.Nombre, m.Pagado, m.Asumido, m.PendienteCuenta, m.Personal)).ToList(),
        r.PorCategoria.Select(c => new ResumenCategoriaDto(
            c.CategoriaId, c.Nombre, c.Total, c.PorMiembro.Select(x => new ImporteMiembroDto(x.MiembroId, x.Importe)).ToList())).ToList(),
        r.Cuenta is { } c2
            ? new ResumenCuentaComunDto(c2.AportadoMes, c2.GastadoMes, c2.Saldo, c2.Efectivo, c2.PendienteReembolso, c2.AhorroMes, c2.AhorroDisponible)
            : null,
        r.Personales);

    /// <summary>Convierte la liquidación del dominio en la respuesta de la API.</summary>
    private static LiquidacionResponse A(DateOnly inicio, LiquidacionCalculada c) => new(
        ApiComun.FormatoMes(inicio),
        c.Saldos.Select(s => new SaldoMiembroDto(s.MiembroId, c.Nombres.GetValueOrDefault(s.MiembroId, ""), s.Importe)).ToList(),
        c.Transferencias.Select(t => new TransferenciaDto(t.De, t.A, t.Importe)).ToList(),
        c.Pagos.Select(A).ToList());

    /// <summary>Convierte un pago de liquidación en su DTO de respuesta.</summary>
    private static PagoLiquidacionDto A(PagoLiquidacionRegistro p)
        => new(p.Id, p.Mes, p.DeMiembroId, p.AMiembroId, p.Importe, p.Fecha, p.Concepto);

    /// <summary>GET /api/resumen?mes=YYYY-MM: gastos del mes, con totales pagados y asumidos por miembro y desglose por categoría. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> ResumenAsync(
        [FromQuery] string? mes, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        return Results.Ok(A(await servicio.ResumenAsync(inicio, ct)));
    }

    /// <summary>GET /api/liquidacion?mes=YYYY-MM: saldos por miembro, transferencias pendientes y pagos registrados del mes. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> LiquidacionAsync(
        [FromQuery] string? mes, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        return Results.Ok(A(inicio, await servicio.LiquidacionAsync(inicio, ct)));
    }

    /// <summary>
    /// GET /api/resumen-liquidacion?mes=YYYY-MM: el resumen y la liquidación del mes en una sola respuesta. Es lo que pinta la pestaña
    /// Resumen: ambas necesitan los gastos del mes y los miembros, que así se leen una vez en una sola petición. Los importes son
    /// los mismos que devuelven <c>/api/resumen</c> y <c>/api/liquidacion</c>. 409 sin hogar; 400 si el mes es inválido.
    /// </summary>
    private static async Task<IResult> ResumenYLiquidacionAsync(
        [FromQuery] string? mes, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        var r = await servicio.ResumenYLiquidacionAsync(inicio, ct);
        return Results.Ok(new ResumenYLiquidacionResponse(A(r.Resumen), A(inicio, r.Liquidacion)));
    }

    /// <summary>POST /api/pagos-liquidacion: registra un pago entre dos miembros. 400 si el mes no es día 1, los miembros coinciden o el importe es inválido; 409 sin hogar o si el importe supera la deuda pendiente del par (solo se comprueba si el mes tiene gastos). 201 si se crea.</summary>
    private static async Task<IResult> CrearPagoAsync(
        CrearPagoLiquidacionRequest req, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();

        var r = await servicio.RegistrarPagoAsync(
            new RegistrarPago(hogarId, req.Mes, req.DeMiembroId, req.AMiembroId, req.Importe, req.Fecha, req.Concepto), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/pagos-liquidacion/{r.Valor!.Id}", A(r.Valor));
    }

    /// <summary>DELETE /api/pagos-liquidacion/{id}: borra un pago de liquidación. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarPagoAsync(
        Guid id, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EliminarPagoAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
