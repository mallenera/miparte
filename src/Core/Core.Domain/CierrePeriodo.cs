namespace MiParte.Core.Domain;

/// <summary>
/// Reglas del cierre de un periodo propio (un viaje, un evento). Al cerrar, el periodo pasa a rango con fecha de fin y se congela:
/// los gastos que caen dentro no se pueden editar ni borrar hasta reabrirlo. Un periodo abierto termina el día del cierre;
/// uno de rango conserva la fecha de fin que ya tenía.
/// </summary>
public static class CierrePeriodo
{
    /// <summary>
    /// Periodo resultante de cerrar <paramref name="actual"/> el día <paramref name="hoy"/>. Null si no puede cerrarse: el hogar
    /// no tiene periodo propio, o es abierto y <paramref name="hoy"/> es anterior a su inicio (un periodo no puede acabar antes de empezar).
    /// </summary>
    /// <param name="actual">Periodo propio del hogar, si lo tiene.</param>
    /// <param name="hoy">Día del cierre.</param>
    /// <param name="error">Motivo si no se puede cerrar.</param>
    public static Periodo? Cerrar(Periodo? actual, DateOnly hoy, out string? error)
    {
        error = null;
        if (actual is null || actual.Tipo == TipoPeriodo.Mes)
        {
            error = "El hogar no tiene un periodo propio que cerrar.";
            return null;
        }

        if (actual.Tipo == TipoPeriodo.Rango) return actual;

        if (hoy < actual.Desde)
        {
            error = "El periodo todavía no ha empezado: no se puede cerrar antes de su primer día.";
            return null;
        }

        return Periodo.Rango(actual.Desde, hoy);
    }
}
