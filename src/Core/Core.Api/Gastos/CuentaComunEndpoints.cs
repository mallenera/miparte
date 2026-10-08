using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Api.Gastos;

/// <summary>Cuenta común: aportaciones mensuales (con su parte de ahorro), saldo, reembolsos a quien adelantó gastos de la cuenta y retiradas de ahorro.</summary>
public static class CuentaComunEndpoints
{
    /// <summary>Registra los endpoints de la cuenta común; todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapCuentaComun(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cuenta-comun", EstadoAsync).RequireAuthorization();
        app.MapPut("/api/cuenta-comun/aportaciones", FijarAportacionAsync).RequireAuthorization();
        app.MapPost("/api/cuenta-comun/reembolsos", CrearReembolsoAsync).RequireAuthorization();
        app.MapDelete("/api/cuenta-comun/reembolsos/{id:guid}", BorrarReembolsoAsync).RequireAuthorization();
        app.MapPost("/api/cuenta-comun/depositos-ahorro", CrearDepositoAsync).RequireAuthorization();
        app.MapDelete("/api/cuenta-comun/depositos-ahorro/{id:guid}", BorrarDepositoAsync).RequireAuthorization();
        app.MapPost("/api/cuenta-comun/retiradas-ahorro", CrearRetiradaAsync).RequireAuthorization();
        app.MapDelete("/api/cuenta-comun/retiradas-ahorro/{id:guid}", BorrarRetiradaAsync).RequireAuthorization();
        return app;
    }

    /// <summary>Convierte una aportación en su DTO de respuesta.</summary>
    private static AportacionCuentaDto A(AportacionCuenta a) => new(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro);

    /// <summary>Convierte un reembolso en su DTO de respuesta.</summary>
    private static ReembolsoCuentaDto A(ReembolsoCuenta r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Convierte un depósito de ahorro en su DTO de respuesta.</summary>
    private static DepositoAhorroDto A(DepositoAhorro d) => new(d.Id, d.MiembroId, d.Fecha, d.Importe, d.Concepto);

    /// <summary>Convierte una retirada de ahorro en su DTO de respuesta.</summary>
    private static RetiradaAhorroDto A(RetiradaAhorro r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Aportaciones en la forma que usa el cálculo del dominio.</summary>
    private static List<AportacionVigente> Vigentes(IEnumerable<AportacionCuenta> aportaciones)
        => aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe, a.Ahorro)).ToList();

    /// <summary>
    /// Ahorro del que se puede disponer con fecha <paramref name="fecha"/>: lo ahorrado (aportaciones y depósitos) hasta ese mes menos todo
    /// lo ya retirado o gastado desde el ahorro. <paramref name="excluirGasto"/> deja fuera un gasto que se está editando.
    /// </summary>
    internal static async Task<decimal> AhorroDisponibleAsync(MiParteDbContext db, DateOnly fecha, Guid? excluirGasto, CancellationToken ct)
    {
        var aportaciones = await db.AportacionesCuenta.ToListAsync(ct);
        var depositos = await db.DepositosAhorro.Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToListAsync(ct);
        var ahorrado = CuentaComun.Calcular(CuentaComun.InicioMes(fecha), Vigentes(aportaciones), [], [], null, depositos).AhorroAcumulado;
        var retirado = await db.RetiradasAhorro.SumAsync(r => r.Importe, ct);
        var gastado = await db.Gastos.Where(g => g.PagadoDesdeAhorro && g.Id != excluirGasto).SumAsync(g => g.Importe, ct);
        return ahorrado - retirado - gastado;
    }

    /// <summary>
    /// Lo que sobraría de ahorro en el último mes en que se retiró o se gastó desde el ahorro si las aportaciones y los depósitos fueran los dados
    /// (null si no hay retiradas ni gastos desde el ahorro). Un valor negativo es la cantidad que faltaría: el cambio dejaría sin respaldo dinero ya usado.
    /// </summary>
    private static async Task<decimal?> HolguraDeAhorroAsync(
        MiParteDbContext db, IReadOnlyList<AportacionVigente> aportaciones, IEnumerable<DepositoDeAhorro> depositos, CancellationToken ct)
    {
        var retiradas = await db.RetiradasAhorro.Select(r => new { r.Fecha, r.Importe }).ToListAsync(ct);
        var gastos = await db.Gastos.Where(g => g.PagadoDesdeAhorro).Select(g => new { g.Fecha, g.Importe }).ToListAsync(ct);
        if (retiradas.Count == 0 && gastos.Count == 0) return null;

        var ultimo = retiradas.Select(r => r.Fecha).Concat(gastos.Select(g => g.Fecha)).Max();
        var ahorrado = CuentaComun.Calcular(CuentaComun.InicioMes(ultimo), aportaciones, [], [], null, depositos).AhorroAcumulado;
        return ahorrado - retiradas.Sum(r => r.Importe) - gastos.Sum(g => g.Importe);
    }

    /// <summary>Respuesta 409 cuando un cambio dejaría el ahorro en negativo, con lo que faltaría.</summary>
    private static IResult AhorroSinRespaldo(string accion, decimal falta)
        => Results.Conflict(new { error = $"No se puede {accion}: ya se ha retirado o gastado ese ahorro (faltarían {-falta:0.00}).", falta = -falta });

    /// <summary>Gastos cargados a la cuenta común, para el cálculo de saldo.</summary>
    private static async Task<List<GastoDeCuenta>> GastosDeCuenta(MiParteDbContext db, CancellationToken ct)
        => await db.Gastos.Where(g => g.ACargoCuentaComun)
            .Select(g => new GastoDeCuenta(g.PagadoPor, g.Fecha, g.Importe, g.PagadoDesdeAhorro)).ToListAsync(ct);

    /// <summary>GET /api/cuenta-comun?mes=YYYY-MM: saldo acumulado, ahorro, reembolsos pendientes, aportaciones, y reembolsos y retiradas de ahorro del mes. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> EstadoAsync(
        [FromQuery] string? mes, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
        var fin = inicio.AddMonths(1);

        var aportaciones = await db.AportacionesCuenta.OrderBy(a => a.MiembroId).ThenBy(a => a.Desde).ToListAsync(ct);
        var reembolsos = await db.ReembolsosCuenta.OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToListAsync(ct);
        var retiradas = await db.RetiradasAhorro.OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToListAsync(ct);
        var depositos = await db.DepositosAhorro.OrderBy(d => d.Fecha).ThenBy(d => d.Id).ToListAsync(ct);
        var nombres = await db.Miembros.ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);

        var e = CuentaComun.Calcular(
            inicio, Vigentes(aportaciones),
            await GastosDeCuenta(db, ct), reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe)),
            retiradas.Select(r => new RetiradaDeAhorro(r.Fecha, r.Importe)),
            depositos.Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)));

        return Results.Ok(new CuentaComunResponse(
            ApiComun.FormatoMes(inicio), e.AportadoMes, e.Aportado, e.Gastado, e.Saldo,
            e.Pendientes.Select(p => new PendienteCuentaDto(p.MiembroId, nombres.GetValueOrDefault(p.MiembroId, ""), p.Importe)).ToList(),
            e.Efectivo, aportaciones.Select(A).ToList(),
            reembolsos.Where(r => r.Fecha >= inicio && r.Fecha < fin).Select(A).ToList(),
            e.AhorroMes, e.AhorroAcumulado, e.AhorroRetirado, e.AhorroDisponible,
            retiradas.Where(r => r.Fecha >= inicio && r.Fecha < fin).Select(A).ToList(),
            e.AhorroDepositado, depositos.Where(d => d.Fecha >= inicio && d.Fecha < fin).Select(A).ToList(),
            e.AhorroGastado));
    }

    /// <summary>PUT /api/cuenta-comun/aportaciones: fija la aportación de un adulto desde un mes, con la parte que va a ahorro (si ya había una de ese mes, la sustituye). 400 si el mes no es día 1, el importe o el ahorro son inválidos (el ahorro no puede superar el importe) o el miembro no es adulto activo; 409 sin hogar o si la rebaja del ahorro dejaría sin respaldo lo ya retirado o gastado desde el ahorro (<c>{ error, falta }</c>).</summary>
    private static async Task<IResult> FijarAportacionAsync(
        FijarAportacionRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (req.Desde.Day != 1) return ApiComun.Invalido("El mes de la aportación debe ser el día 1 del mes.");
        // Una aportación de 0 es válida: deja de aportar desde ese mes.
        var errorImporte = req.Importe < 0 ? "El importe no puede ser negativo."
            : req.Importe == 0 ? null : ApiComun.ValidarImporte(req.Importe);
        if (errorImporte is not null) return ApiComun.Invalido(errorImporte);
        var errorAhorro = req.Ahorro < 0 ? "El ahorro no puede ser negativo."
            : req.Ahorro > req.Importe ? "El ahorro no puede superar la aportación."
            : decimal.Round(req.Ahorro, 2) != req.Ahorro ? "El ahorro admite como máximo 2 decimales." : null;
        if (errorAhorro is not null) return ApiComun.Invalido(errorAhorro);
        if (!await ApiComun.EsAdultoActivo(db, req.MiembroId, ct))
            return ApiComun.Invalido("Solo los adultos activos del hogar aportan a la cuenta común.");

        // Rebajar el ahorro no puede dejar sin respaldo lo que ya se retiró o se gastó desde el ahorro.
        var existentes = await db.AportacionesCuenta.AsNoTracking().ToListAsync(ct);
        var tras = Vigentes(existentes.Where(x => !(x.MiembroId == req.MiembroId && x.Desde == req.Desde)))
            .Append(new AportacionVigente(req.MiembroId, req.Desde, req.Importe, req.Ahorro)).ToList();
        var depositosActuales = await db.DepositosAhorro.Select(d => new DepositoDeAhorro(d.Fecha, d.Importe)).ToListAsync(ct);
        if (await HolguraDeAhorroAsync(db, tras, depositosActuales, ct) is < 0m and var falta)
            return AhorroSinRespaldo("rebajar el ahorro de esta aportación", falta);

        var a = await db.AportacionesCuenta.FirstOrDefaultAsync(x => x.MiembroId == req.MiembroId && x.Desde == req.Desde, ct);
        if (a is null)
        {
            a = new AportacionCuenta { Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = req.MiembroId, Desde = req.Desde };
            db.AportacionesCuenta.Add(a);
        }
        a.Importe = req.Importe;
        a.Ahorro = req.Ahorro;
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

    /// <summary>POST /api/cuenta-comun/depositos-ahorro: registra dinero que entra al ahorro fuera de la aportación mensual. 400 si el importe es inválido o el miembro no es del hogar; 409 sin hogar. 201 si se crea.</summary>
    private static async Task<IResult> CrearDepositoAsync(
        CrearDepositoAhorroRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var error = ApiComun.ValidarImporte(req.Importe) ?? ApiComun.ValidarConcepto(req.Concepto);
        if (error is not null) return ApiComun.Invalido(error);
        if (!await db.Miembros.AnyAsync(m => m.Id == req.MiembroId, ct))
            return ApiComun.Invalido("El miembro del depósito debe pertenecer al hogar.");

        var d = new DepositoAhorro
        {
            Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = req.MiembroId, Importe = req.Importe,
            Fecha = req.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow), Concepto = ApiComun.NormalizarConcepto(req.Concepto),
        };
        db.DepositosAhorro.Add(d);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/cuenta-comun/depositos-ahorro/{d.Id}", A(d));
    }

    /// <summary>DELETE /api/cuenta-comun/depositos-ahorro/{id}: borra un depósito de ahorro. 409 sin hogar o si dejaría sin respaldo lo ya retirado o gastado desde el ahorro (<c>{ error, falta }</c>); 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarDepositoAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var d = await db.DepositosAhorro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Results.NotFound();

        // Sin este ingreso el ahorro no puede quedar por debajo de lo ya retirado o gastado desde él.
        var aportaciones = await db.AportacionesCuenta.ToListAsync(ct);
        var resto = await db.DepositosAhorro.Where(x => x.Id != id).Select(x => new DepositoDeAhorro(x.Fecha, x.Importe)).ToListAsync(ct);
        if (await HolguraDeAhorroAsync(db, Vigentes(aportaciones), resto, ct) is < 0m and var falta)
            return AhorroSinRespaldo("eliminar este ingreso", falta);

        db.DepositosAhorro.Remove(d);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>POST /api/cuenta-comun/retiradas-ahorro: registra dinero que sale del ahorro. 400 si el importe es inválido o el miembro no es del hogar; 409 sin hogar o si supera el ahorro disponible (<c>{ error, disponible }</c>). 201 si se crea.</summary>
    private static async Task<IResult> CrearRetiradaAsync(
        CrearRetiradaAhorroRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var error = ApiComun.ValidarImporte(req.Importe) ?? ApiComun.ValidarConcepto(req.Concepto);
        if (error is not null) return ApiComun.Invalido(error);
        if (!await db.Miembros.AnyAsync(m => m.Id == req.MiembroId, ct))
            return ApiComun.Invalido("El miembro de la retirada debe pertenecer al hogar.");

        var fecha = req.Fecha ?? DateOnly.FromDateTime(DateTime.UtcNow);
        // Lo ahorrado hasta el mes de la retirada, menos lo ya retirado o gastado desde el ahorro: nunca se saca más de lo que hay.
        var disponible = await AhorroDisponibleAsync(db, fecha, null, ct);
        if (req.Importe > disponible)
            return Results.Conflict(new { error = $"El importe supera el ahorro disponible ({Math.Max(0m, disponible):0.00}).", disponible = Math.Max(0m, disponible) });

        var r = new RetiradaAhorro
        {
            Id = Guid.NewGuid(), HogarId = hogarId, MiembroId = req.MiembroId, Importe = req.Importe,
            Fecha = fecha, Concepto = ApiComun.NormalizarConcepto(req.Concepto),
        };
        db.RetiradasAhorro.Add(r);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/cuenta-comun/retiradas-ahorro/{r.Id}", A(r));
    }

    /// <summary>DELETE /api/cuenta-comun/retiradas-ahorro/{id}: borra una retirada de ahorro. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarRetiradaAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await db.RetiradasAhorro.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        db.RetiradasAhorro.Remove(r);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
