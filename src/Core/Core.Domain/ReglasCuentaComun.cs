namespace MiParte.Core.Domain;

/// <summary>Reglas de campo de las aportaciones a la cuenta común.</summary>
public static class ReglasCuentaComun
{
    /// <summary>Valida el importe de una aportación: puede ser 0 (deja de aportar) pero no negativo; devuelve el error o null.</summary>
    public static string? ValidarImporteAportacion(decimal importe)
        => importe < 0 ? "El importe no puede ser negativo." : importe == 0 ? null : ReglasMovimiento.ValidarImporte(importe);

    /// <summary>Valida el saldo inicial: dinero que ya hay en la cuenta, así que no puede ser negativo ni tener más de 2 decimales; devuelve el error o null.</summary>
    public static string? ValidarSaldoInicial(decimal importe)
        => importe < 0 ? "El saldo inicial no puede ser negativo."
            : decimal.Round(importe, 2) != importe ? "El saldo inicial admite como máximo 2 decimales."
            : importe >= 10_000_000_000m ? "El saldo inicial es demasiado grande."
            : null;

    /// <summary>Valida la parte de ahorro de una aportación: no negativa, sin pasar del importe y con 2 decimales; devuelve el error o null.</summary>
    public static string? ValidarAhorroAportacion(decimal ahorro, decimal importe)
        => ahorro < 0 ? "El ahorro no puede ser negativo."
            : ahorro > importe ? "El ahorro no puede superar la aportación."
            : decimal.Round(ahorro, 2) != ahorro ? "El ahorro admite como máximo 2 decimales." : null;
}
