using System.Globalization;

namespace MiParte.Web.Reparto;

/// <summary>
/// Interpretación del importe que escribe la persona en un formulario. Es la única copia de la regla en el front (antes estaba repetida
/// en Gastos, Recurrentes y Cuenta común); el servidor la vuelve a comprobar, esta solo evita el viaje en vano.
/// </summary>
public static class EntradaImporte
{
    /// <summary>Mensaje cuando el importe no es mayor que 0 o tiene más de 2 decimales.</summary>
    public const string MensajeInvalido = "Introduce un importe mayor que 0 con máximo 2 decimales.";

    /// <summary>
    /// Interpreta el texto (admite coma o punto decimal): válido si es un número con como mucho 2 decimales y mayor que 0, o igual
    /// a 0 si <paramref name="permitirCero"/>.
    /// </summary>
    /// <param name="texto">Texto del campo.</param>
    /// <param name="permitirCero">Si 0 es un valor válido (p. ej. «deja de aportar»).</param>
    /// <param name="valor">Importe interpretado; 0 si no es válido.</param>
    public static bool TryParse(string? texto, bool permitirCero, out decimal valor)
    {
        valor = 0m;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var normalizado = texto.Trim().Replace(',', '.');
        // Los decimales se cuentan en el texto: «1,230» vale lo mismo que «1,23» pero tiene tres decimales, y en un teclado español
        // «1.230» suele significar mil doscientos treinta; aceptarlos guardaría 1,23 € sin avisar.
        var punto = normalizado.IndexOf('.');
        if (punto >= 0 && normalizado.Length - punto - 1 > 2) return false;
        if (!decimal.TryParse(normalizado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor))
            return false;
        return (valor > 0 || (permitirCero && valor == 0)) && decimal.Round(valor, 2) == valor;
    }

    /// <summary>Atajo para un importe obligatorio mayor que 0.</summary>
    /// <param name="texto">Texto del campo.</param>
    /// <param name="valor">Importe interpretado.</param>
    public static bool TryParse(string? texto, out decimal valor) => TryParse(texto, permitirCero: false, out valor);
}
