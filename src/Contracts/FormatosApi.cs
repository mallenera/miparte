using System.Globalization;

namespace MiParte.Contracts;

/// <summary>Formatos de mes y fecha del contrato HTTP, compartidos por Core.Api y la demo del front para que validen igual.</summary>
public static class FormatosApi
{
    /// <summary>Parsea un mes estricto «YYYY-MM» (año desde 1, mes 01-12) y devuelve su primer día.</summary>
    /// <param name="mes">Texto recibido.</param>
    /// <param name="inicio">Primer día del mes, si es válido.</param>
    public static bool TryMes(string? mes, out DateOnly inicio)
    {
        inicio = default;
        if (mes is not { Length: 7 } || mes[4] != '-') return false;
        if (!int.TryParse(mes.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var anio) || anio < 1) return false;
        if (!int.TryParse(mes.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > 12) return false;
        inicio = new DateOnly(anio, numero, 1);
        return true;
    }

    /// <summary>Formatea cualquier día de un mes como «YYYY-MM».</summary>
    /// <param name="fecha">Día del mes.</param>
    public static string FormatoMes(DateOnly fecha) => $"{fecha.Year:0000}-{fecha.Month:00}";

    /// <summary>Parsea una fecha con el formato exacto «YYYY-MM-DD».</summary>
    /// <param name="texto">Texto recibido.</param>
    /// <param name="fecha">Fecha, si es válida.</param>
    public static bool TryFecha(string? texto, out DateOnly fecha) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
}
