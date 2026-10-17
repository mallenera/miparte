using System.Globalization;

namespace MiParte.Core.Domain;

/// <summary>Forma de un <see cref="Periodo"/>: un mes natural, un rango cerrado de días o un periodo abierto sin fin.</summary>
public enum TipoPeriodo
{
    /// <summary>Un mes natural («YYYY-MM»), el periodo fijo de siempre.</summary>
    Mes,

    /// <summary>Rango de días con inicio y fin incluidos («YYYY-MM-DD/YYYY-MM-DD»), p. ej. un viaje.</summary>
    Rango,

    /// <summary>Periodo desde una fecha, sin fin fijado, hasta que se cierre («YYYY-MM-DD/»), p. ej. un evento en curso.</summary>
    Abierto,
}

/// <summary>
/// Periodo de tiempo de un gasto o de un cierre. Un mes se representa como su rango completo (del día 1 al último),
/// así que <see cref="Contiene"/> funciona igual para las tres formas. El texto de un mes es el de siempre («YYYY-MM»)
/// y no cambia: los meses ya guardados y usados por la API siguen siendo válidos sin tocar nada.
/// </summary>
public sealed record Periodo
{
    /// <summary>Mensaje de error común para un texto de periodo con formato desconocido.</summary>
    private const string MensajeFormato = "El periodo debe tener el formato YYYY-MM, YYYY-MM-DD/YYYY-MM-DD o YYYY-MM-DD/.";

    /// <summary>Mensaje de error para un mes con formato distinto de YYYY-MM (mismo texto que la API).</summary>
    private const string MensajeMes = "El mes debe tener el formato YYYY-MM.";

    /// <summary>Mensaje de error cuando el fin de un rango es anterior a su inicio.</summary>
    private const string MensajeFinAnterior = "La fecha de fin del periodo no puede ser anterior a la de inicio.";

    private Periodo(TipoPeriodo tipo, DateOnly desde, DateOnly? hasta)
    {
        Tipo = tipo;
        Desde = desde;
        Hasta = hasta;
    }

    /// <summary>Forma del periodo: mes, rango o abierto.</summary>
    public TipoPeriodo Tipo { get; }

    /// <summary>Primer día del periodo, incluido.</summary>
    public DateOnly Desde { get; }

    /// <summary>Último día del periodo, incluido; nulo en un periodo abierto.</summary>
    public DateOnly? Hasta { get; }

    /// <summary>Crea el periodo de un mes a partir de «YYYY-MM» estricto; lanza <see cref="ArgumentException"/> si el formato no es válido.</summary>
    /// <param name="mes">Mes en formato «YYYY-MM» (año desde 1, mes 01-12).</param>
    public static Periodo DeMes(string mes)
    {
        if (!TryMes(mes, out var inicio)) throw new ArgumentException(MensajeMes, nameof(mes));
        return new Periodo(TipoPeriodo.Mes, inicio, FinDeMes(inicio));
    }

    /// <summary>Crea un rango con inicio y fin incluidos; lanza <see cref="ArgumentException"/> si el fin es anterior al inicio.</summary>
    /// <param name="desde">Primer día del rango.</param>
    /// <param name="hasta">Último día del rango, igual o posterior a <paramref name="desde"/>.</param>
    public static Periodo Rango(DateOnly desde, DateOnly hasta)
    {
        if (hasta < desde) throw new ArgumentException(MensajeFinAnterior, nameof(hasta));
        return new Periodo(TipoPeriodo.Rango, desde, hasta);
    }

    /// <summary>Crea un periodo abierto que empieza en <paramref name="desde"/> y no tiene fin hasta que se cierre.</summary>
    /// <param name="desde">Primer día del periodo.</param>
    public static Periodo Abierto(DateOnly desde) => new(TipoPeriodo.Abierto, desde, null);

    /// <summary>Indica si el día cae dentro del periodo; en un abierto, cualquier día desde su inicio en adelante.</summary>
    /// <param name="fecha">Día a comprobar.</param>
    public bool Contiene(DateOnly fecha) => fecha >= Desde && (Hasta is null || fecha <= Hasta.Value);

    /// <summary>Texto del periodo: «YYYY-MM» para un mes, «YYYY-MM-DD/YYYY-MM-DD» para un rango y «YYYY-MM-DD/» para un abierto.</summary>
    public string ATexto() => Tipo switch
    {
        TipoPeriodo.Mes => $"{Desde.Year:0000}-{Desde.Month:00}",
        TipoPeriodo.Rango => $"{FormatoFecha(Desde)}/{FormatoFecha(Hasta!.Value)}",
        _ => $"{FormatoFecha(Desde)}/",
    };

    /// <summary>Devuelve el mismo texto que <see cref="ATexto"/>.</summary>
    public override string ToString() => ATexto();

    /// <summary>
    /// Interpreta el texto de un periodo en cualquiera de sus tres formas. Devuelve false y un mensaje en español
    /// en <paramref name="error"/> si el formato no es válido, si una fecha no existe o si el fin es anterior al inicio.
    /// </summary>
    /// <param name="texto">Texto recibido, sin recortar espacios.</param>
    /// <param name="periodo">Periodo resultante, si es válido.</param>
    /// <param name="error">Mensaje de error, si no lo es.</param>
    public static bool TryParse(string? texto, out Periodo? periodo, out string? error)
    {
        periodo = null;
        error = null;
        if (string.IsNullOrEmpty(texto))
        {
            error = MensajeFormato;
            return false;
        }

        if (!texto.Contains('/'))
        {
            if (!TryMes(texto, out var inicio))
            {
                error = MensajeFormato;
                return false;
            }
            periodo = new Periodo(TipoPeriodo.Mes, inicio, FinDeMes(inicio));
            return true;
        }

        var partes = texto.Split('/');
        if (partes.Length != 2)
        {
            error = MensajeFormato;
            return false;
        }

        if (!TryFecha(partes[0], out var desde))
        {
            error = "La fecha de inicio del periodo debe tener el formato YYYY-MM-DD y existir.";
            return false;
        }

        // Un abierto termina en barra sin fecha: «YYYY-MM-DD/».
        if (partes[1].Length == 0)
        {
            periodo = new Periodo(TipoPeriodo.Abierto, desde, null);
            return true;
        }

        if (!TryFecha(partes[1], out var hasta))
        {
            error = "La fecha de fin del periodo debe tener el formato YYYY-MM-DD y existir.";
            return false;
        }

        if (hasta < desde)
        {
            error = MensajeFinAnterior;
            return false;
        }

        periodo = new Periodo(TipoPeriodo.Rango, desde, hasta);
        return true;
    }

    /// <summary>Igual que <see cref="TryParse(string?, out Periodo?, out string?)"/> pero lanza <see cref="ArgumentException"/> con el mensaje de error.</summary>
    /// <param name="texto">Texto del periodo.</param>
    public static Periodo Parse(string? texto)
        => TryParse(texto, out var periodo, out var error) ? periodo! : throw new ArgumentException(error, nameof(texto));

    /// <summary>Parsea «YYYY-MM» estricto (año desde 1, mes 01-12) y devuelve su primer día. Misma regla que la API.</summary>
    private static bool TryMes(string? mes, out DateOnly inicio)
    {
        inicio = default;
        if (mes is not { Length: 7 } || mes[4] != '-') return false;
        if (!int.TryParse(mes.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var anio) || anio < 1) return false;
        if (!int.TryParse(mes.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > 12) return false;
        inicio = new DateOnly(anio, numero, 1);
        return true;
    }

    /// <summary>Parsea una fecha «YYYY-MM-DD» estricta que exista en el calendario.</summary>
    private static bool TryFecha(string? texto, out DateOnly fecha)
    {
        fecha = default;
        if (texto is not { Length: 10 } || texto[4] != '-' || texto[7] != '-') return false;
        if (!int.TryParse(texto.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var anio) || anio < 1) return false;
        if (!int.TryParse(texto.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mes) || mes is < 1 or > 12) return false;
        if (!int.TryParse(texto.AsSpan(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var dia)
            || dia < 1 || dia > DateTime.DaysInMonth(anio, mes)) return false;
        fecha = new DateOnly(anio, mes, dia);
        return true;
    }

    /// <summary>Último día del mes que contiene a <paramref name="inicio"/>.</summary>
    private static DateOnly FinDeMes(DateOnly inicio) => new(inicio.Year, inicio.Month, DateTime.DaysInMonth(inicio.Year, inicio.Month));

    /// <summary>Formatea una fecha como «YYYY-MM-DD».</summary>
    private static string FormatoFecha(DateOnly fecha) => $"{fecha.Year:0000}-{fecha.Month:00}-{fecha.Day:00}";
}
