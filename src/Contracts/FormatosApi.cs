using System.Globalization;

namespace MiParte.Contracts;

/// <summary>Formatos de mes y fecha del contrato HTTP, compartidos por Core.Api y la demo del front para que validen igual.</summary>
public static class FormatosApi
{
    /// <summary>
    /// Primer año admitido en meses y fechas. Un gasto del hogar no tiene sentido fuera de <see cref="AnioMinimo"/>-<see cref="AnioMaximo"/>,
    /// y acotarlo evita los desbordamientos de <see cref="DateOnly"/> al calcular «el mes siguiente» de 9999-12 (antes, un 500). Debe
    /// coincidir con <c>Periodo.AnioMinimo</c> del dominio.
    /// </summary>
    public const int AnioMinimo = 1900;

    /// <summary>Último año admitido en meses y fechas (ver <see cref="AnioMinimo"/>).</summary>
    public const int AnioMaximo = 2999;

    /// <summary>Parsea un mes estricto «YYYY-MM» (año entre <see cref="AnioMinimo"/> y <see cref="AnioMaximo"/>, mes 01-12) y devuelve su primer día.</summary>
    /// <param name="mes">Texto recibido.</param>
    /// <param name="inicio">Primer día del mes, si es válido.</param>
    public static bool TryMes(string? mes, out DateOnly inicio)
    {
        inicio = default;
        if (mes is not { Length: 7 } || mes[4] != '-') return false;
        if (!int.TryParse(mes.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var anio) || anio is < AnioMinimo or > AnioMaximo) return false;
        if (!int.TryParse(mes.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > 12) return false;
        inicio = new DateOnly(anio, numero, 1);
        return true;
    }

    /// <summary>Formatea cualquier día de un mes como «YYYY-MM».</summary>
    /// <param name="fecha">Día del mes.</param>
    public static string FormatoMes(DateOnly fecha) => $"{fecha.Year:0000}-{fecha.Month:00}";

    /// <summary>Parsea una fecha con el formato exacto «YYYY-MM-DD» y año entre <see cref="AnioMinimo"/> y <see cref="AnioMaximo"/>.</summary>
    /// <param name="texto">Texto recibido.</param>
    /// <param name="fecha">Fecha, si es válida.</param>
    public static bool TryFecha(string? texto, out DateOnly fecha) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha) && FechaEnRango(fecha);

    /// <summary>Indica si el año de la fecha está entre <see cref="AnioMinimo"/> y <see cref="AnioMaximo"/>.</summary>
    /// <param name="fecha">Fecha a comprobar.</param>
    public static bool FechaEnRango(DateOnly fecha) => fecha.Year is >= AnioMinimo and <= AnioMaximo;
}
