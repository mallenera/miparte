using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>Endpoints de gastos del hogar; cada gasto guarda su reparto calculado en el momento de crearlo o editarlo.</summary>
public static class GastosEndpoints
{
    /// <summary>Registra los endpoints de /api/gastos (listar, obtener, crear, editar y borrar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapGastos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos").RequireAuthorization();
        g.MapGet("", ListarAsync);
        g.MapGet("{id:guid}", ObtenerAsync);
        g.MapPost("", CrearAsync);
        g.MapPut("{id:guid}", EditarAsync);
        g.MapDelete("{id:guid}", BorrarAsync);
        return app;
    }

    /// <summary>Convierte un gasto con su reparto en el DTO de respuesta.</summary>
    private static GastoResponse A(Gasto g) => new(
        g.Id, g.Fecha, g.Importe, g.CategoriaId, g.PagadoPor, g.PerfilRepartoId, g.Concepto, g.GastoRecurrenteId,
        g.Repartos.OrderBy(r => r.MiembroId).Select(r => new RepartoGastoDto(r.MiembroId, r.ImporteAsumido)).ToList());

    /// <summary>GET /api/gastos: lista los gastos, filtrables por mes (YYYY-MM) y categoría, de más reciente a más antiguo. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> ListarAsync(
        [FromQuery] string? mes, [FromQuery] Guid? categoriaId,
        [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var q = db.Gastos.Include(g => g.Repartos).AsQueryable();
        if (mes is not null)
        {
            if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
            var fin = inicio.AddMonths(1);
            q = q.Where(g => g.Fecha >= inicio && g.Fecha < fin);
        }
        if (categoriaId is { } c) q = q.Where(g => g.CategoriaId == c);
        var lista = await q.OrderByDescending(g => g.Fecha).ThenBy(g => g.Id).ToListAsync(ct);
        return Results.Ok(lista.Select(A));
    }

    /// <summary>GET /api/gastos/{id}: devuelve un gasto con su reparto. 409 sin hogar; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var g = await db.Gastos.Include(x => x.Repartos).FirstOrDefaultAsync(x => x.Id == id, ct);
        return g is null ? Results.NotFound() : Results.Ok(A(g));
    }

    /// <summary>Valida el cuerpo y calcula el reparto con los ingresos del mes de la fecha del gasto.</summary>
    private static async Task<(IReadOnlyList<ParteAsumida>? Partes, string? Error)> Preparar(
        GastoRequest r, MiParteDbContext db, CancellationToken ct)
    {
        var e = ApiComun.ValidarImporte(r.Importe) ?? ApiComun.ValidarConcepto(r.Concepto);
        if (e is not null) return (null, e);
        if (r.Fecha == default) return (null, "La fecha es obligatoria.");
        if (!await db.Categorias.AnyAsync(c => c.Id == r.CategoriaId, ct)) return (null, "La categoría no existe en el hogar.");
        if (!await ApiComun.EsAdultoActivo(db, r.PagadoPor, ct)) return (null, "Quien paga debe ser un adulto activo del hogar.");
        var perfil = await db.PerfilesReparto.Include(p => p.Detalles).FirstOrDefaultAsync(p => p.Id == r.PerfilRepartoId, ct);
        if (perfil is null) return (null, "El perfil de reparto no existe en el hogar.");

        var adultos = await ApiComun.AdultosActivos(db, ct);
        var mes = new DateOnly(r.Fecha.Year, r.Fecha.Month, 1);
        var ingresos = perfil.Modo == ModoReparto.Ingresos
            ? await ApiComun.IngresosDelMes(db, mes, ct) : new Dictionary<Guid, decimal>();
        var partes = ApiComun.Repartir(perfil, adultos, ingresos, r.Importe, r.PagadoPor, out var error);
        return (partes, error);
    }

    /// <summary>POST /api/gastos: crea el gasto y guarda su reparto en la misma transacción. 201 si se crea; 409 sin hogar; 400 si falla la validación o el reparto.</summary>
    private static async Task<IResult> CrearAsync(
        GastoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var (partes, error) = await Preparar(req, db, ct);
        if (partes is null) return ApiComun.Invalido(error!);

        var g = new Gasto
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Fecha = req.Fecha, Importe = req.Importe,
            CategoriaId = req.CategoriaId, PagadoPor = req.PagadoPor, PerfilRepartoId = req.PerfilRepartoId,
            Concepto = ApiComun.NormalizarConcepto(req.Concepto),
        };
        ApiComun.AplicarReparto(g, partes);
        db.Gastos.Add(g);
        await db.SaveChangesAsync(ct); // gasto y reparto en la misma transacción
        return Results.Created($"/api/gastos/{g.Id}", A(g));
    }

    /// <summary>PUT /api/gastos/{id}: actualiza el gasto y recalcula solo su reparto, sin tocar el resto del histórico. 409 sin hogar; 404 si no existe; 400 si no es válido.</summary>
    private static async Task<IResult> EditarAsync(
        Guid id, GastoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var g = await db.Gastos.Include(x => x.Repartos).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Results.NotFound();
        var (partes, error) = await Preparar(req, db, ct);
        if (partes is null) return ApiComun.Invalido(error!);

        // Solo se recalcula este gasto; el resto del histórico no se toca.
        g.Fecha = req.Fecha;
        g.Importe = req.Importe;
        g.CategoriaId = req.CategoriaId;
        g.PagadoPor = req.PagadoPor;
        g.PerfilRepartoId = req.PerfilRepartoId;
        g.Concepto = ApiComun.NormalizarConcepto(req.Concepto);
        ApiComun.AplicarReparto(g, partes);
        await db.SaveChangesAsync(ct);
        return Results.Ok(A(g));
    }

    /// <summary>DELETE /api/gastos/{id}: borra el gasto y su reparto. 409 sin hogar; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var g = await db.Gastos.Include(x => x.Repartos).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Results.NotFound();
        db.GastosReparto.RemoveRange(g.Repartos);
        db.Gastos.Remove(g);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
