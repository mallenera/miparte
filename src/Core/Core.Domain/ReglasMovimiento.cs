namespace MiParte.Core.Domain;

/// <summary>Reglas de campo comunes a los movimientos de dinero (gastos, pagos, reembolsos, depósitos y retiradas).</summary>
public static class ReglasMovimiento
{
    /// <summary>Longitud máxima del concepto de un movimiento.</summary>
    public const int MaxConcepto = 200;

    /// <summary>Importe máximo admitido en un movimiento.</summary>
    public const decimal MaxImporte = 9_999_999_999.99m;

    /// <summary>Valida que el importe sea mayor que cero, no exceda el máximo y tenga como mucho 2 decimales; devuelve el mensaje de error o null.</summary>
    public static string? ValidarImporte(decimal importe)
    {
        if (importe <= 0) return "El importe debe ser mayor que cero.";
        if (importe > MaxImporte) return "El importe es demasiado grande.";
        if (decimal.Round(importe, 2) != importe) return "El importe admite como máximo 2 decimales.";
        return null;
    }

    /// <summary>Valida la longitud máxima del concepto; devuelve el mensaje de error o null.</summary>
    public static string? ValidarConcepto(string? concepto)
        => concepto is { Length: > MaxConcepto } ? $"El concepto admite como máximo {MaxConcepto} caracteres." : null;

    /// <summary>Recorta el concepto y devuelve null si está vacío.</summary>
    public static string? NormalizarConcepto(string? concepto)
        => string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim();
}
