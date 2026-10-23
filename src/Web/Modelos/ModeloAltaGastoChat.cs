using System.Globalization;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Alta guiada de un gasto desde el chat: pregunta importe, concepto y fecha de uno en uno, valida cada respuesta y, al
/// completarse, compone el mensaje que el asistente convierte en borrador con <c>proponer_gasto</c>. No guarda nada: la
/// confirmación sigue siendo el botón del borrador. No depende de Blazor; la vista solo enlaza y pinta.
/// </summary>
public sealed class ModeloAltaGastoChat
{
    /// <summary>Longitud máxima del concepto; coincide con el límite de <c>proponer_gasto</c> en Assistant.Api.</summary>
    public const int MaxConcepto = 200;

    /// <summary>Importe máximo admitido; coincide con el límite de <c>proponer_gasto</c> en Assistant.Api.</summary>
    public const decimal MaxImporte = 9_999_999_999.99m;

    /// <summary>Formato de fecha que se pide y se acepta: día, mes y año de dos cifras.</summary>
    public const string FormatoFecha = "dd/MM/yy";

    /// <summary>Paso en el que está el alta.</summary>
    public enum Paso { Importe, Concepto, Fecha, Completa }

    /// <summary>Paso actual.</summary>
    public Paso PasoActual { get; private set; } = Paso.Importe;

    /// <summary>Importe validado; 0 hasta que se responde.</summary>
    public decimal Importe { get; private set; }

    /// <summary>Concepto validado; vacío hasta que se responde.</summary>
    public string Concepto { get; private set; } = "";

    /// <summary>Fecha validada; por defecto hasta que se responde.</summary>
    public DateOnly Fecha { get; private set; }

    /// <summary>Pregunta del paso actual, en español.</summary>
    public string Pregunta => PasoActual switch
    {
        Paso.Importe => "¿Cuánto ha sido? Escribe el importe en euros, por ejemplo 12,50.",
        Paso.Concepto => "¿Qué se ha pagado? Por ejemplo, café.",
        Paso.Fecha => "¿De qué fecha es? Escríbela como DD/MM/YY, por ejemplo 23/10/26.",
        _ => "",
    };

    /// <summary>
    /// Registra la respuesta al paso actual. Devuelve null si era válida y la conversación avanza, o el motivo en español
    /// para pedirla otra vez sin avanzar.
    /// </summary>
    /// <param name="texto">Lo que ha escrito la persona.</param>
    public string? Responder(string? texto)
    {
        switch (PasoActual)
        {
            case Paso.Importe:
                if (!EntradaImporte.TryParse(texto, out var importe)) return EntradaImporte.MensajeInvalido;
                if (importe > MaxImporte) return "El importe es demasiado grande.";
                Importe = importe;
                PasoActual = Paso.Concepto;
                return null;
            case Paso.Concepto:
                var concepto = texto?.Trim() ?? "";
                if (concepto.Length == 0) return "Escribe qué se ha pagado.";
                if (concepto.Length > MaxConcepto) return $"El concepto no puede pasar de {MaxConcepto} caracteres.";
                Concepto = concepto;
                PasoActual = Paso.Fecha;
                return null;
            case Paso.Fecha:
                if (!DateOnly.TryParseExact(texto?.Trim() ?? "", FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                    return "La fecha no es válida. Escríbela como DD/MM/YY, por ejemplo 23/10/26.";
                Fecha = fecha;
                PasoActual = Paso.Completa;
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Mensaje que se envía al asistente cuando el alta está completa. Va en formato técnico (importe con punto y fecha ISO)
    /// para que la herramienta <c>proponer_gasto</c> reciba datos sin ambigüedad.
    /// </summary>
    public string MensajeParaAsistente() =>
        $"Quiero apuntar un gasto: importe {Importe.ToString(CultureInfo.InvariantCulture)} €, concepto «{Concepto}», "
        + $"fecha {Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. Prepáralo con proponer_gasto.";
}
