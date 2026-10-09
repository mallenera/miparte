using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Api.Cierres;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>Endpoints de gastos recurrentes (plantillas mensuales) y su generación por mes.</summary>
public static class RecurrentesEndpoints
{
    /// <summary>Registra los endpoints de /api/gastos-recurrentes (CRUD y generar); todos requieren autorización.</summary>
    public static IEndpointRouteBuilder MapGastosRecurrentes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos-recurrentes").RequireAuthorization();
        g.MapGet("", ListarAsync);
        g.MapGet("{id:guid}", ObtenerAsync);
        g.MapPost("", CrearAsync);
        g.MapPut("{id:guid}", EditarAsync);
        g.MapDelete("{id:guid}", BorrarAsync);
        g.MapPost("generar", GenerarAsync).RequireRateLimiting(LimitacionPeticiones.Costosa);
        return app;
    }

    /// <summary>Convierte una plantilla de gasto recurrente en su DTO de respuesta.</summary>
    private static GastoRecurrenteResponse A(GastoRecurrente r)
        => new(r.Id, r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.DiaMes, r.Concepto, r.Activo);

    /// <summary>GET /api/gastos-recurrentes: lista las plantillas ordenadas por día del mes. 409 sin hogar.</summary>
    private static async Task<IResult> ListarAsync(
        [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var lista = await db.GastosRecurrentes.OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToListAsync(ct);
        return Results.Ok(lista.Select(A));
    }

    /// <summary>GET /api/gastos-recurrentes/{id}: devuelve una plantilla. 409 sin hogar; 404 si no existe.</summary>
    private static async Task<IResult> ObtenerAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? Results.NotFound() : Results.Ok(A(r));
    }

    /// <summary>Valida importe, concepto, día del mes (1 a 28), categoría, pagador adulto activo y perfil de reparto; devuelve el mensaje de error o null.</summary>
    private static async Task<string?> Validar(GastoRecurrenteRequest r, MiParteDbContext db, CancellationToken ct)
    {
        var e = ApiComun.ValidarImporte(r.Importe) ?? ApiComun.ValidarConcepto(r.Concepto);
        if (e is not null) return e;
        if (r.DiaMes is < 1 or > 28) return "El día del mes debe estar entre 1 y 28.";
        if (!await db.Categorias.AnyAsync(c => c.Id == r.CategoriaId, ct)) return "La categoría no existe en el hogar.";
        if (!await ApiComun.EsAdultoActivo(db, r.PagadoPor, ct)) return "Quien paga debe ser un adulto activo del hogar.";
        if (!await db.PerfilesReparto.AnyAsync(p => p.Id == r.PerfilRepartoId, ct)) return "El perfil de reparto no existe en el hogar.";
        return null;
    }

    /// <summary>POST /api/gastos-recurrentes: crea una plantilla. 201 si se crea; 409 sin hogar; 400 si no es válida.</summary>
    private static async Task<IResult> CrearAsync(
        GastoRecurrenteRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (await Validar(req, db, ct) is { } error) return ApiComun.Invalido(error);

        var r = new GastoRecurrente
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Importe = req.Importe, CategoriaId = req.CategoriaId,
            PagadoPor = req.PagadoPor, PerfilRepartoId = req.PerfilRepartoId, DiaMes = req.DiaMes,
            Concepto = ApiComun.NormalizarConcepto(req.Concepto), Activo = req.Activo,
        };
        db.GastosRecurrentes.Add(r);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/gastos-recurrentes/{r.Id}", A(r));
    }

    /// <summary>PUT /api/gastos-recurrentes/{id}: actualiza la plantilla sin modificar los gastos ya generados. 409 sin hogar; 404 si no existe; 400 si no es válida.</summary>
    private static async Task<IResult> EditarAsync(
        Guid id, GastoRecurrenteRequest req, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        if (await Validar(req, db, ct) is { } error) return ApiComun.Invalido(error);

        // Los gastos ya generados no se tocan (historial inmutable).
        r.Importe = req.Importe;
        r.CategoriaId = req.CategoriaId;
        r.PagadoPor = req.PagadoPor;
        r.PerfilRepartoId = req.PerfilRepartoId;
        r.DiaMes = req.DiaMes;
        r.Concepto = ApiComun.NormalizarConcepto(req.Concepto);
        r.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        return Results.Ok(A(r));
    }

    /// <summary>DELETE /api/gastos-recurrentes/{id}: borra la plantilla. 409 sin hogar o si ya tiene gastos generados (hay que desactivarla); 404 si no existe; 204 si se borra.</summary>
    private static async Task<IResult> BorrarAsync(
        Guid id, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is null) return ApiComun.SinHogar();
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        // gasto.gasto_recurrente_id es FK sin ON DELETE: con gastos generados hay que desactivarla, no borrarla.
        if (await db.Gastos.AnyAsync(g => g.GastoRecurrenteId == id, ct))
            return Results.Conflict(new { error = "Ya tiene gastos generados: desactívala en lugar de borrarla." });
        db.GastosRecurrentes.Remove(r);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Crea los gastos del mes para las plantillas activas que aún no tengan uno. Idempotente. 409 si el mes está cerrado.</summary>
    private static async Task<IResult> GenerarAsync(
        [FromQuery] string? mes, [FromServices] MiParteDbContext db, [FromServices] IHogarActual hogar, CancellationToken ct)
    {
        if (hogar.HogarId is not { } hogarId) return ApiComun.SinHogar();
        if (!ApiComun.TryMes(mes, out var inicio)) return ApiComun.MesInvalido();
        var fin = inicio.AddMonths(1);
        if (await CierreMes.ComprobarAsync(db, ct, inicio) is { } cerrado) return cerrado;

        // Dos peticiones simultáneas pueden leer lo mismo y generar duplicados: el índice único
        // gasto_recurrente_mes_uq lo impide y aquí se trata el conflicto reintentando (hasta 3 veces).
        // En cada reintento se vuelven a leer los gastos, que ya incluyen los de la otra petición.
        for (var intento = 0; ; intento++)
        {
            var plantillas = await db.GastosRecurrentes.Where(r => r.Activo).OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToListAsync(ct);
            var yaGenerados = (await db.Gastos
                .Where(g => g.GastoRecurrenteId != null && g.Fecha >= inicio && g.Fecha < fin)
                .Select(g => g.GastoRecurrenteId!.Value).ToListAsync(ct)).ToHashSet();
            var pendientes = plantillas.Where(p => !yaGenerados.Contains(p.Id)).ToList();

            if (pendientes.Count > 0)
            {
                var adultos = await ApiComun.AdultosActivos(db, ct);
                var perfiles = await db.PerfilesReparto.Include(p => p.Detalles).ToDictionaryAsync(p => p.Id, ct);

                // Una plantilla a cargo de la cuenta común no genera gastos mientras el hogar no la tenga activada.
                if (pendientes.Any(p => perfiles[p.PerfilRepartoId].Modo == ModoReparto.CuentaComun)
                    && await CuentaComunEndpoints.ExigirActivaAsync(db, ct) is { } inactiva)
                    return inactiva;

                foreach (var p in pendientes)
                {
                    var partes = ApiComun.Repartir(perfiles[p.PerfilRepartoId], adultos, p.Importe, p.PagadoPor, out var error);
                    if (partes is null) // nada se guarda: o se generan todos o ninguno
                        return ApiComun.Invalido($"No se puede repartir el gasto recurrente '{p.Concepto ?? p.Id.ToString()}': {error}");

                    var g = new Gasto
                    {
                        Id = Guid.NewGuid(), HogarId = hogarId, Fecha = new DateOnly(inicio.Year, inicio.Month, p.DiaMes),
                        Importe = p.Importe, CategoriaId = p.CategoriaId, PagadoPor = p.PagadoPor,
                        PerfilRepartoId = p.PerfilRepartoId, Concepto = p.Concepto, GastoRecurrenteId = p.Id,
                        ACargoCuentaComun = perfiles[p.PerfilRepartoId].Modo == ModoReparto.CuentaComun,
                    };
                    ApiComun.AplicarReparto(g, partes);
                    db.Gastos.Add(g);
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (CierreMes.EsRechazo(ex))
                {
                    return CierreMes.Rechazo(); // otra petición cerró el mes tras la comprobación previa
                }
                catch (DbUpdateException ex) when (intento < 3 && EsUnicidadViolada(ex))
                {
                    // Otra petición generó alguno antes: se descartan los cambios y se recalcula.
                    db.ChangeTracker.Clear();
                    continue;
                }
            }

            return Results.Ok(new GenerarRecurrentesResponse(ApiComun.FormatoMes(inicio), pendientes.Count, plantillas.Count - pendientes.Count));
        }
    }

    /// <summary>Indica si la excepción es una violación de unicidad de PostgreSQL (SqlState 23505).</summary>
    internal static bool EsUnicidadViolada(DbUpdateException ex)
        => ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
