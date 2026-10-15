using Microsoft.EntityFrameworkCore;
using MiParte.Contracts;
using MiParte.Core.Application;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>Utilidades compartidas por los endpoints de gastos, recurrentes y liquidación.</summary>
internal static class ApiComun
{
    /// <summary>Longitud máxima del concepto de un movimiento.</summary>
    public const int MaxConcepto = ReglasMovimiento.MaxConcepto;

    /// <summary>Parsea "YYYY-MM" estricto; devuelve el primer día del mes.</summary>
    public static bool TryMes(string? mes, out DateOnly inicio) => FormatosApi.TryMes(mes, out inicio);

    /// <summary>Formatea el primer día de un mes como "YYYY-MM".</summary>
    public static string FormatoMes(DateOnly inicio) => FormatosApi.FormatoMes(inicio);

    /// <summary>Respuesta 400 con el mensaje de error indicado.</summary>
    public static IResult Invalido(string mensaje) => Results.BadRequest(new { error = mensaje });

    /// <summary>Respuesta 400 por mes con formato distinto de YYYY-MM.</summary>
    public static IResult MesInvalido() => Invalido("El mes debe tener el formato YYYY-MM.");

    /// <summary>Parsea una fecha con el formato exacto "YYYY-MM-DD".</summary>
    public static bool TryFecha(string? texto, out DateOnly fecha) => FormatosApi.TryFecha(texto, out fecha);

    /// <summary>Respuesta 400 por una fecha con formato distinto de YYYY-MM-DD; <paramref name="parametro"/> es el nombre del parámetro.</summary>
    public static IResult FechaInvalida(string parametro) => Invalido($"La fecha «{parametro}» debe tener el formato YYYY-MM-DD.");

    /// <summary>Traduce el fallo de un caso de uso a su respuesta HTTP: 400, 404 o 409 (con los datos extra, como lo pendiente).</summary>
    public static IResult Respuesta(ErrorAplicacion error) => error.Tipo switch
    {
        TipoError.NoEncontrado => Results.NotFound(),
        TipoError.Conflicto => Results.Conflict(
            new Dictionary<string, object>(error.Extra ?? new Dictionary<string, object>()) { ["error"] = error.Mensaje }),
        _ => Invalido(error.Mensaje),
    };

    /// <summary>Respuesta 409 cuando la petición no tiene hogar seleccionado.</summary>
    public static IResult SinHogar() => Results.Conflict(new { error = "No hay hogar seleccionado." });

    /// <summary>Valida que el importe sea mayor que cero, no exceda el máximo y tenga como mucho 2 decimales; devuelve el mensaje de error o null.</summary>
    public static string? ValidarImporte(decimal importe) => ReglasMovimiento.ValidarImporte(importe);

    /// <summary>Valida la longitud máxima del concepto; devuelve el mensaje de error o null.</summary>
    public static string? ValidarConcepto(string? concepto)
        => ReglasMovimiento.ValidarConcepto(concepto);

    /// <summary>Recorta el concepto y devuelve null si está vacío.</summary>
    public static string? NormalizarConcepto(string? concepto)
        => ReglasMovimiento.NormalizarConcepto(concepto);

    /// <summary>Adultos activos del hogar, ordenados por nombre y id.</summary>
    public static Task<List<Miembro>> AdultosActivos(MiParteDbContext db, CancellationToken ct)
        => db.Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto)
            .OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToListAsync(ct);

    /// <summary>Indica si el miembro existe en el hogar, está activo y es adulto.</summary>
    public static Task<bool> EsAdultoActivo(MiParteDbContext db, Guid id, CancellationToken ct)
        => db.Miembros.AnyAsync(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto, ct);

    /// <summary>
    /// Calcula el reparto de un gasto entre los adultos activos según el modo del perfil.
    /// Devuelve null y un mensaje si no se puede repartir.
    /// </summary>
    public static IReadOnlyList<ParteAsumida>? Repartir(
        PerfilReparto perfil, IReadOnlyList<Miembro> adultos,
        decimal importe, Guid pagadoPor, out string? error)
    {
        error = null;
        if (perfil.Modo == ModoReparto.CuentaComun) return []; // lo asume la cuenta común: sin reparto entre personas
        if (adultos.Count == 0) { error = "El hogar no tiene adultos activos entre los que repartir."; return null; }

        var miembros = adultos.Select(a => new MiembroReparto(a.Id, perfil.Modo switch
        {
            ModoReparto.Porcentaje or ModoReparto.Partes
                => perfil.Detalles.Where(d => d.MiembroId == a.Id).Sum(d => d.Valor),
                        _ => 0m,
        })).ToList();

        if (perfil.Modo is ModoReparto.Porcentaje or ModoReparto.Partes && adultos.Count > 1 && miembros.All(m => m.Valor <= 0))
        {
            error = "El perfil de reparto no asigna valor a ningún adulto activo.";
            return null;
        }

        try
        {
            var partes = RepartoMiembros.Repartir(importe, perfil.Modo, miembros, pagadoPor);
            if (partes.Sum(p => p.Importe) != importe) { error = "El reparto no suma el importe del gasto."; return null; }
            return partes;
        }
        catch (ArgumentException e)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>Aplica el reparto al gasto: actualiza filas existentes, borra las que sobran y añade las nuevas.</summary>
    public static void AplicarReparto(Gasto gasto, IReadOnlyList<ParteAsumida> partes)
    {
        foreach (var r in gasto.Repartos.Where(r => partes.All(p => p.MiembroId != r.MiembroId)).ToList())
            gasto.Repartos.Remove(r);
        foreach (var p in partes)
        {
            var existente = gasto.Repartos.FirstOrDefault(r => r.MiembroId == p.MiembroId);
            if (existente is not null) existente.ImporteAsumido = p.Importe;
            else gasto.Repartos.Add(new GastoReparto
            {
                GastoId = gasto.Id, MiembroId = p.MiembroId, HogarId = gasto.HogarId, ImporteAsumido = p.Importe,
            });
        }
    }
}
