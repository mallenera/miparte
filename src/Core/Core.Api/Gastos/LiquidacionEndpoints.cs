using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;
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
        app.MapPut("/api/hogar/liquidacion", CambiarModoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HogarFunciones);
        app.MapPost("/api/liquidacion/ciclos", CerrarCicloAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.PagosRegistrar);
        app.MapGet("/api/liquidacion/ciclos", ListarCiclosAsync).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// PUT /api/hogar/liquidacion (permiso <c>hogar.funciones</c>): cambia el modo a "mensual" o "ciclos". No altera los gastos ni los
    /// ciclos ya cerrados. 400 si el modo no es válido; 409 sin hogar; 200 con el modo resultante.
    /// </summary>
    private static async Task<IResult> CambiarModoAsync(
        ModoLiquidacionRequest req, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (string.IsNullOrWhiteSpace(req?.Modo) || !TextoModoLiquidacion.TryParse(req.Modo, out var modo))
            return ApiComun.Invalido("El modo de liquidación debe ser «mensual» o «ciclos».");

        var resultado = await servicio.CambiarModoAsync(modo, ct);
        return Results.Ok(new ModoLiquidacionResponse(TextoModoLiquidacion.ATexto(resultado)));
    }

    /// <summary>
    /// POST /api/liquidacion/ciclos (permiso <c>pagos.registrar</c>): cierra el ciclo actual, que liquida los gastos pendientes desde el
    /// cierre anterior. 403 sin permiso; 409 sin hogar, si el hogar liquida por meses, si no hay gastos sin liquidar o si otro cierre
    /// acaba de hacerse; 201 con el ciclo creado.
    /// </summary>
    private static async Task<IResult> CerrarCicloAsync(
        HttpContext ctx, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!(ctx.Items.TryGetValue(HogarActualMiddleware.ClaveMiembro, out var resuelto) && resuelto is Miembro yo))
            return Results.Json(new { error = Mensajes.SoloMiembroCierra }, statusCode: StatusCodes.Status403Forbidden);

        var r = await servicio.CerrarCicloAsync(yo, ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/liquidacion/ciclos/{r.Valor!.Ciclo.Id}", r.Valor.ADto());
    }

    /// <summary>GET /api/liquidacion/ciclos: ciclos cerrados del hogar, del más reciente al más antiguo, con su total y fechas. 409 sin hogar.</summary>
    private static async Task<IResult> ListarCiclosAsync(
        [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        return Results.Ok((await servicio.ListarCiclosAsync(ct)).Select(c => c.ADto()).ToList());
    }

    /// <summary>GET /api/resumen?mes=YYYY-MM: gastos del mes, con totales pagados y asumidos por miembro y desglose por categoría. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> ResumenAsync(
        [FromQuery] string? mes, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        return Results.Ok((await servicio.ResumenAsync(inicio, ct)).ADto());
    }

    /// <summary>GET /api/liquidacion?mes=YYYY-MM: saldos por miembro, transferencias pendientes y pagos registrados del mes. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> LiquidacionAsync(
        [FromQuery] string? mes, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        return Results.Ok((await servicio.LiquidacionAsync(inicio, ct)).ADto(inicio));
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
        return Results.Ok(new ResumenYLiquidacionResponse(r.Resumen.ADto(), r.Liquidacion.ADto(inicio)));
    }

    /// <summary>POST /api/pagos-liquidacion: registra un pago entre dos miembros. 400 si el mes no es día 1, los miembros coinciden o el importe es inválido; 409 sin hogar o si el importe supera la deuda pendiente del par (solo se comprueba si el mes tiene gastos). 201 si se crea.</summary>
    private static async Task<IResult> CrearPagoAsync(
        CrearPagoLiquidacionRequest req, [FromServices] ServicioLiquidacion servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();

        var r = await servicio.RegistrarPagoAsync(
            new RegistrarPago(hogarId, req.Mes, req.DeMiembroId, req.AMiembroId, req.Importe, req.Fecha, req.Concepto), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/pagos-liquidacion/{r.Valor!.Id}", r.Valor.ADto());
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
