using Microsoft.AspNetCore.Mvc;
using MiParte.Contracts;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>
/// Cuenta común: aportaciones mensuales (con su parte de ahorro), saldo, reembolsos a quien adelantó gastos de la cuenta y
/// retiradas de ahorro. Los endpoints solo validan el formato HTTP, delegan en <see cref="ServicioCuentaComun"/> y traducen el resultado.
/// </summary>
public static class CuentaComunEndpoints
{
    /// <summary>Registra los endpoints de la cuenta común; todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCuentaComun(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cuenta-comun", EstadoAsync).RequireAuthorization();
        app.MapPut("/api/cuenta-comun/activacion", ActivarAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HogarFunciones);
        app.MapPut("/api/cuenta-comun/ahorro/activacion", ActivarAhorroAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.HogarFunciones);
        app.MapPut("/api/cuenta-comun/aportaciones", FijarAportacionAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CuentaMovimientos);
        app.MapPost("/api/cuenta-comun/reembolsos", CrearReembolsoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CuentaMovimientos);
        app.MapDelete("/api/cuenta-comun/reembolsos/{id:guid}", BorrarReembolsoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.CuentaMovimientos);
        app.MapPost("/api/cuenta-comun/depositos-ahorro", CrearDepositoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.AhorroMovimientos);
        app.MapDelete("/api/cuenta-comun/depositos-ahorro/{id:guid}", BorrarDepositoAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.AhorroMovimientos);
        app.MapPost("/api/cuenta-comun/retiradas-ahorro", CrearRetiradaAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.AhorroMovimientos);
        app.MapDelete("/api/cuenta-comun/retiradas-ahorro/{id:guid}", BorrarRetiradaAsync).RequireAuthorization().RequierePermiso(CatalogoPermisos.AhorroMovimientos);
        return app;
    }

    /// <summary>Convierte una aportación en su DTO de respuesta.</summary>
    private static AportacionCuentaDto ADto(AportacionCuenta a) => new(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro);

    /// <summary>Convierte un reembolso en su DTO de respuesta.</summary>
    private static ReembolsoCuentaDto ADto(ReembolsoCuenta r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Convierte un depósito de ahorro en su DTO de respuesta.</summary>
    private static DepositoAhorroDto ADto(DepositoAhorro d) => new(d.Id, d.MiembroId, d.Fecha, d.Importe, d.Concepto);

    /// <summary>Convierte una retirada de ahorro en su DTO de respuesta.</summary>
    private static RetiradaAhorroDto ADto(RetiradaAhorro r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Convierte el estado de la cuenta en la respuesta de la API.</summary>
    private static CuentaComunResponse ADto(EstadoCuentaDelMes s)
    {
        var e = s.Estado;
        var d = s.Detalle;
        var nombres = d.Nombres;
        return new CuentaComunResponse(
            ApiComun.FormatoMes(s.Mes), e.AportadoMes, e.Aportado, e.Gastado, e.Saldo,
            e.Pendientes.Select(p => new PendienteCuentaDto(p.MiembroId, nombres.GetValueOrDefault(p.MiembroId, ""), p.Importe)).ToList(),
            e.Efectivo, d.Aportaciones.Select(a => new AportacionCuentaDto(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro)).ToList(),
            d.ReembolsosDelMes.Select(r => new ReembolsoCuentaDto(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList(),
            e.AhorroMes, e.AhorroAcumulado, e.AhorroRetirado, e.AhorroDisponible,
            d.RetiradasDelMes.Select(r => new RetiradaAhorroDto(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList(),
            e.AhorroDepositado,
            d.DepositosDelMes.Select(x => new DepositoAhorroDto(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToList(),
            e.AhorroGastado, d.Historico.CuentaActiva,
            s.Partes.Select(p => new PartePersonaDto(
                p.MiembroId, nombres.GetValueOrDefault(p.MiembroId, ""), p.Aportado, p.Ahorrado, p.PorcentajeGastos, p.PorcentajeAhorro,
                p.ParteSaldo, p.ParteAhorro, p.Pendiente)).ToList(),
            d.AhorroActivo);
    }

    /// <summary>GET /api/cuenta-comun?mes=YYYY-MM: saldo acumulado, ahorro, reembolsos pendientes, aportaciones, y reembolsos y retiradas de ahorro del mes. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> EstadoAsync(
        [FromQuery] string? mes, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();

        return Results.Ok(ADto(await servicio.EstadoAsync(inicio, ct)));
    }

    /// <summary>
    /// PUT /api/cuenta-comun/activacion (permiso <c>hogar.funciones</c>): activa o desactiva la cuenta común del hogar. Desactivarla no borra datos:
    /// solo bloquea nuevas escrituras y gastos a su cargo. 403 sin el permiso; 409 sin hogar; 200 con el estado resultante.
    /// </summary>
    private static async Task<IResult> ActivarAsync(
        ActivarCuentaComunRequest req, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        return Results.Ok(new { activa = await servicio.ActivarCuentaAsync(req.Activa, ct) });
    }

    /// <summary>
    /// PUT /api/cuenta-comun/ahorro/activacion (permiso <c>hogar.funciones</c>): activa o desactiva el ahorro del hogar. Desactivarlo no borra datos:
    /// solo bloquea nuevas escrituras del ahorro. 403 sin el permiso; 409 sin hogar o con la cuenta común desactivada; 200 con el estado resultante.
    /// </summary>
    private static async Task<IResult> ActivarAhorroAsync(
        ActivarAhorroRequest req, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.ActivarAhorroAsync(req.Activo, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(new { activo = r.Valor });
    }

    /// <summary>PUT /api/cuenta-comun/aportaciones: fija la aportación de un adulto desde un mes, con la parte que va a ahorro (si ya había una de ese mes, la sustituye). 400 si el mes no es día 1, el importe o el ahorro son inválidos (el ahorro no puede superar el importe) o el miembro no es adulto activo; 409 sin hogar o si la rebaja del ahorro dejaría sin respaldo lo ya retirado o gastado desde el ahorro (<c>{ error, falta }</c>).</summary>
    private static async Task<IResult> FijarAportacionAsync(
        FijarAportacionRequest req, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.FijarAportacionAsync(new FijarAportacion(hogarId, req.MiembroId, req.Desde, req.Importe, req.Ahorro), ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.Ok(ADto(r.Valor!));
    }

    /// <summary>POST /api/cuenta-comun/reembolsos: registra un pago de la cuenta a quien adelantó gastos. 400 si el importe es inválido o el miembro no es del hogar; 409 sin hogar o si supera lo pendiente de reembolsar al miembro. 201 si se crea.</summary>
    private static async Task<IResult> CrearReembolsoAsync(
        CrearReembolsoRequest req, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearReembolsoAsync(new RegistrarMovimiento(hogarId, req.MiembroId, req.Importe, req.Fecha, req.Concepto), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/cuenta-comun/reembolsos/{r.Valor!.Id}", ADto(r.Valor));
    }

    /// <summary>DELETE /api/cuenta-comun/reembolsos/{id}: borra un reembolso. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarReembolsoAsync(
        Guid id, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EliminarReembolsoAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>POST /api/cuenta-comun/depositos-ahorro: registra dinero que entra al ahorro fuera de la aportación mensual. 400 si el importe es inválido o el miembro no es del hogar; 409 sin hogar. 201 si se crea.</summary>
    private static async Task<IResult> CrearDepositoAsync(
        CrearDepositoAhorroRequest req, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearDepositoAsync(new RegistrarMovimiento(hogarId, req.MiembroId, req.Importe, req.Fecha, req.Concepto), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/cuenta-comun/depositos-ahorro/{r.Valor!.Id}", ADto(r.Valor));
    }

    /// <summary>DELETE /api/cuenta-comun/depositos-ahorro/{id}: borra un depósito de ahorro. 409 sin hogar o si dejaría sin respaldo lo ya retirado o gastado desde el ahorro (<c>{ error, falta }</c>); 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarDepositoAsync(
        Guid id, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EliminarDepositoAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }

    /// <summary>POST /api/cuenta-comun/retiradas-ahorro: registra dinero que sale del ahorro. 400 si el importe es inválido o el miembro no es del hogar; 409 sin hogar o si supera el ahorro disponible (<c>{ error, disponible }</c>). 201 si se crea.</summary>
    private static async Task<IResult> CrearRetiradaAsync(
        CrearRetiradaAhorroRequest req, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var r = await servicio.CrearRetiradaAsync(new RegistrarMovimiento(hogarId, req.MiembroId, req.Importe, req.Fecha, req.Concepto), ct);
        if (r.Error is { } error) return ApiComun.Respuesta(error);
        return Results.Created($"/api/cuenta-comun/retiradas-ahorro/{r.Valor!.Id}", ADto(r.Valor));
    }

    /// <summary>DELETE /api/cuenta-comun/retiradas-ahorro/{id}: borra una retirada de ahorro. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarRetiradaAsync(
        Guid id, [FromServices] ServicioCuentaComun servicio, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await servicio.EliminarRetiradaAsync(id, ct);
        return r.Error is { } error ? ApiComun.Respuesta(error) : Results.NoContent();
    }
}
