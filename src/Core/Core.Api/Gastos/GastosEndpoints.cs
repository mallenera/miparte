using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Cierres;
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
        g.Repartos.OrderBy(r => r.MiembroId).Select(r => new RepartoGastoDto(r.MiembroId, r.ImporteAsumido)).ToList(),
        g.ACargoCuentaComun, g.PagadoDesdeAhorro);

    /// <summary>GET /api/gastos: lista los gastos, filtrables por mes (YYYY-MM), categoría, miembro (quien paga o asume una parte mayor que 0) y texto del concepto, de más reciente a más antiguo. 409 sin hogar; 400 si el mes es inválido.</summary>
    private static async Task<IResult> ListarAsync(
        [FromQuery] string? mes, [FromQuery] Guid? categoriaId, [FromQuery] Guid? miembroId, [FromQuery] string? buscar,
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
        if (miembroId is { } m) q = q.Where(g => g.PagadoPor == m || g.Repartos.Any(r => r.MiembroId == m && r.ImporteAsumido > 0m));
        var texto = buscar?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            if (texto.Length > 200) return ApiComun.Invalido("El texto de búsqueda no puede pasar de 200 caracteres.");
            var patron = texto.ToLowerInvariant();
            q = q.Where(g => g.Concepto != null && g.Concepto.ToLower().Contains(patron));
        }
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

    /// <summary>Valida el cuerpo y calcula el reparto (vacío y marcado a cargo de la cuenta común si el perfil es de cuenta común).</summary>
    private static async Task<(IReadOnlyList<ParteAsumida>? Partes, string? Error, bool CuentaComun)> Preparar(
        GastoRequest r, MiParteDbContext db, CancellationToken ct)
    {
        var e = ApiComun.ValidarImporte(r.Importe) ?? ApiComun.ValidarConcepto(r.Concepto);
        if (e is not null) return (null, e, false);
        if (r.Fecha == default) return (null, "La fecha es obligatoria.", false);
        if (!await db.Categorias.AnyAsync(c => c.Id == r.CategoriaId, ct)) return (null, "La categoría no existe en el hogar.", false);
        if (r.PagadoPor is { } pagador && !await ApiComun.EsAdultoActivo(db, pagador, ct))
            return (null, "Quien paga debe ser un adulto activo del hogar.", false);
        if (r.PagadoDesdeAhorro && r.PagadoPor is not null)
            return (null, "Un gasto pagado desde el ahorro no lo adelanta nadie: déjalo sin pagador.", false);
        var perfil = await db.PerfilesReparto.Include(p => p.Detalles).FirstOrDefaultAsync(p => p.Id == r.PerfilRepartoId, ct);
        if (perfil is null) return (null, "El perfil de reparto no existe en el hogar.", false);
        if (r.PagadoPor is null && perfil.Modo != ModoReparto.CuentaComun)
            return (null, "La cuenta común solo paga gastos con el perfil de cuenta común.", false);

        var adultos = await ApiComun.AdultosActivos(db, ct);
        var partes = ApiComun.Repartir(perfil, adultos, r.Importe, r.PagadoPor ?? Guid.Empty, out var error);
        return (partes, error, perfil.Modo == ModoReparto.CuentaComun);
    }

    /// <summary>Si el gasto se paga desde el ahorro, comprueba que el ahorro disponible lo cubre; devuelve el 409 si no, o null si todo está bien.</summary>
    private static async Task<IResult?> ComprobarAhorroAsync(GastoRequest r, Guid? excluir, MiParteDbContext db, CancellationToken ct)
    {
        if (!r.PagadoDesdeAhorro) return null;
        var disponible = Math.Max(0m, await CuentaComunEndpoints.AhorroDisponibleAsync(db, r.Fecha, excluir, ct));
        return r.Importe <= disponible ? null
            : Results.Conflict(new { error = $"El ahorro disponible ({disponible:0.00}) no cubre el gasto.", disponible });
    }

    /// <summary>POST /api/gastos: crea el gasto y guarda su reparto en la misma transacción. 201 si se crea; 409 sin hogar; 400 si falla la validación o el reparto; 409 si el mes está cerrado o si se paga desde el ahorro y no alcanza.</summary>
    private static async Task<IResult> CrearAsync(
        GastoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        var (partes, error, cuentaComun) = await Preparar(req, db, ct);
        if (partes is null) return ApiComun.Invalido(error!);
        if (await CierreMes.ComprobarAsync(db, ct, req.Fecha) is { } cerrado) return cerrado;
        if (await ComprobarAhorroAsync(req, null, db, ct) is { } sinAhorro) return sinAhorro;

        var g = new Gasto
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Fecha = req.Fecha, Importe = req.Importe,
            CategoriaId = req.CategoriaId, PagadoPor = req.PagadoPor, PerfilRepartoId = req.PerfilRepartoId,
            Concepto = ApiComun.NormalizarConcepto(req.Concepto), ACargoCuentaComun = cuentaComun,
            PagadoDesdeAhorro = req.PagadoDesdeAhorro,
        };
        ApiComun.AplicarReparto(g, partes);
        db.Gastos.Add(g);
        if (await CierreMes.GuardarAsync(db, ct) is { } cerradoAhora) return cerradoAhora; // gasto y reparto en la misma transacción
        return Results.Created($"/api/gastos/{g.Id}", A(g));
    }

    /// <summary>PUT /api/gastos/{id}: actualiza el gasto y recalcula solo su reparto, sin tocar el resto del histórico. 409 sin hogar; 404 si no existe; 400 si no es válido.</summary>
    private static async Task<IResult> EditarAsync(
        Guid id, GastoRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var g = await db.Gastos.Include(x => x.Repartos).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Results.NotFound();
        var (partes, error, cuentaComun) = await Preparar(req, db, ct);
        if (partes is null) return ApiComun.Invalido(error!);
        if (await CierreMes.ComprobarAsync(db, ct, g.Fecha, req.Fecha) is { } cerrado) return cerrado; // ni sacarlo de un mes cerrado ni meterlo en uno
        if (await ComprobarAhorroAsync(req, id, db, ct) is { } sinAhorro) return sinAhorro;

        // Solo se recalcula este gasto; el resto del histórico no se toca.
        g.Fecha = req.Fecha;
        g.Importe = req.Importe;
        g.CategoriaId = req.CategoriaId;
        g.PagadoPor = req.PagadoPor;
        g.PerfilRepartoId = req.PerfilRepartoId;
        g.Concepto = ApiComun.NormalizarConcepto(req.Concepto);
        g.ACargoCuentaComun = cuentaComun;
        g.PagadoDesdeAhorro = req.PagadoDesdeAhorro;
        ApiComun.AplicarReparto(g, partes);
        if (await CierreMes.GuardarAsync(db, ct) is { } cerradoAhora) return cerradoAhora;
        return Results.Ok(A(g));
    }

    /// <summary>DELETE /api/gastos/{id}: borra el gasto y su reparto. 409 sin hogar o si el mes está cerrado; 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var g = await db.Gastos.Include(x => x.Repartos).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Results.NotFound();
        if (await CierreMes.ComprobarAsync(db, ct, g.Fecha) is { } cerrado) return cerrado;
        db.GastosReparto.RemoveRange(g.Repartos);
        db.Gastos.Remove(g);
        if (await CierreMes.GuardarAsync(db, ct) is { } cerradoAhora) return cerradoAhora;
        return Results.NoContent();
    }
}
