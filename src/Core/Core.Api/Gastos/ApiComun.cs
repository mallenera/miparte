using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Api.Gastos;

/// <summary>Utilidades compartidas por los endpoints de ingresos, gastos, recurrentes y liquidación.</summary>
internal static partial class ApiComun
{
    public const int MaxConcepto = 200;
    private const decimal MaxImporte = 9_999_999_999.99m;

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex PatronMes();

    /// <summary>Parsea "YYYY-MM" estricto; devuelve el primer día del mes.</summary>
    public static bool TryMes(string? mes, out DateOnly inicio)
    {
        inicio = default;
        if (mes is null || !PatronMes().IsMatch(mes)) return false;
        var anio = int.Parse(mes[..4]);
        if (anio < 1) return false;
        inicio = new DateOnly(anio, int.Parse(mes[5..]), 1);
        return true;
    }

    public static string FormatoMes(DateOnly inicio) => $"{inicio.Year:0000}-{inicio.Month:00}";

    public static IResult Invalido(string mensaje) => Results.BadRequest(new { error = mensaje });

    public static IResult MesInvalido() => Invalido("El mes debe tener el formato YYYY-MM.");

    public static IResult SinHogar() => Results.Conflict(new { error = "No hay hogar seleccionado." });

    public static string? ValidarImporte(decimal importe)
    {
        if (importe <= 0) return "El importe debe ser mayor que cero.";
        if (importe > MaxImporte) return "El importe es demasiado grande.";
        if (decimal.Round(importe, 2) != importe) return "El importe admite como máximo 2 decimales.";
        return null;
    }

    public static string? ValidarConcepto(string? concepto)
        => concepto is { Length: > MaxConcepto } ? $"El concepto admite como máximo {MaxConcepto} caracteres." : null;

    public static string? NormalizarConcepto(string? concepto)
        => string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim();

    public static Task<List<Miembro>> AdultosActivos(MiParteDbContext db, CancellationToken ct)
        => db.Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto)
            .OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToListAsync(ct);

    public static Task<bool> EsAdultoActivo(MiParteDbContext db, Guid id, CancellationToken ct)
        => db.Miembros.AnyAsync(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto, ct);

    /// <summary>Suma de ingresos de cada miembro en el mes que empieza en <paramref name="inicio"/>.</summary>
    public static async Task<Dictionary<Guid, decimal>> IngresosDelMes(MiParteDbContext db, DateOnly inicio, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        var filas = await db.Ingresos.Where(i => i.Fecha >= inicio && i.Fecha < fin)
            .Select(i => new { i.MiembroId, i.Importe }).ToListAsync(ct);
        return filas.GroupBy(f => f.MiembroId).ToDictionary(g => g.Key, g => g.Sum(x => x.Importe));
    }

    /// <summary>
    /// Calcula el reparto de un gasto entre los adultos activos según el modo del perfil.
    /// Devuelve null y un mensaje si no se puede repartir.
    /// </summary>
    public static IReadOnlyList<ParteAsumida>? Repartir(
        PerfilReparto perfil, IReadOnlyList<Miembro> adultos, IReadOnlyDictionary<Guid, decimal> ingresosMes,
        decimal importe, Guid pagadoPor, out string? error)
    {
        error = null;
        if (adultos.Count == 0) { error = "El hogar no tiene adultos activos entre los que repartir."; return null; }

        var miembros = adultos.Select(a => new MiembroReparto(a.Id, perfil.Modo switch
        {
            ModoReparto.Porcentaje or ModoReparto.Partes
                => perfil.Detalles.Where(d => d.MiembroId == a.Id).Sum(d => d.Valor),
            ModoReparto.Ingresos => ingresosMes.GetValueOrDefault(a.Id),
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
