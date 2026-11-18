using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MiParte.Assistant.Api.Chat;

/// <summary>Datos leídos de un ticket ya validados; cada uno es null si el modelo no lo leyó o no era creíble.</summary>
/// <param name="Importe">Total pagado: mayor que 0, como mucho 2 decimales y dentro del máximo de Core.Api.</param>
/// <param name="Comercio">Nombre del comercio, limpio y acotado.</param>
/// <param name="Fecha">Fecha de la compra, no posterior a mañana.</param>
public sealed record LecturaTicket(decimal? Importe, string? Comercio, DateOnly? Fecha);

/// <summary>
/// Reglas de la foto que llega a <c>POST /api/chat/ticket</c> y de la respuesta del modelo de visión. La foto solo se acepta
/// en los formatos que produce el navegador (JPEG, PNG, WebP) y comprobada por su firma; la respuesta del modelo se trata
/// como entrada no fiable: se extrae el JSON, se valida cada campo y lo que no cuadra se descarta en vez de corregirse.
/// </summary>
public static class FotoTicket
{
    /// <summary>Longitud máxima del comercio que se propone como concepto (corta a propósito: viene de una imagen).</summary>
    public const int MaxComercio = 60;

    /// <summary>Tipos MIME aceptados (sin parámetros).</summary>
    public static readonly IReadOnlySet<string> TiposPermitidos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    /// <summary>Tipo MIME aceptado de la cabecera <c>Content-Type</c>, en minúsculas; null si no es uno permitido.</summary>
    /// <param name="contentType">Valor de la cabecera; puede ser nulo.</param>
    public static string? TipoPermitido(string? contentType)
        => MediaTypeHeaderValue.TryParse(contentType, out var tipo) && tipo.MediaType is { } m && TiposPermitidos.Contains(m)
            ? m.ToLowerInvariant()
            : null;

    /// <summary>Comprueba la firma del fichero: JPEG (<c>FF D8 FF</c>), PNG (<c>89 50 4E 47</c>) o WebP (<c>RIFF</c>…<c>WEBP</c>).</summary>
    /// <param name="imagen">Contenido recibido.</param>
    public static bool FirmaValida(ReadOnlySpan<byte> imagen)
    {
        if (imagen.Length < 12) return false;
        if (imagen[0] == 0xFF && imagen[1] == 0xD8 && imagen[2] == 0xFF) return true;
        if (imagen[0] == 0x89 && imagen[1] == 0x50 && imagen[2] == 0x4E && imagen[3] == 0x47) return true;
        return imagen[..4].SequenceEqual("RIFF"u8) && imagen.Slice(8, 4).SequenceEqual("WEBP"u8);
    }

    /// <summary>
    /// Interpreta la respuesta del modelo: toma el primer objeto JSON del texto (el modelo a veces lo rodea de explicaciones
    /// o de <c>```json</c>) y valida cada campo por separado. Nunca lanza: una respuesta ilegible da una lectura vacía.
    /// </summary>
    /// <param name="respuesta">Texto devuelto por el modelo.</param>
    /// <param name="hoy">Fecha actual; una fecha posterior a mañana se descarta.</param>
    public static LecturaTicket Interpretar(string? respuesta, DateOnly hoy)
    {
        var vacia = new LecturaTicket(null, null, null);
        if (string.IsNullOrWhiteSpace(respuesta)) return vacia;
        var inicio = respuesta.IndexOf('{');
        var fin = respuesta.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio) return vacia;

        try
        {
            using var doc = JsonDocument.Parse(respuesta[inicio..(fin + 1)]);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return vacia;
            return new LecturaTicket(Importe(raiz), Comercio(raiz), Fecha(raiz, hoy));
        }
        catch (JsonException)
        {
            return vacia;
        }
    }

    private static decimal? Importe(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("importe", out var v)) return null;
        decimal n;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (!v.TryGetDecimal(out n)) return null;
        }
        else if (v.ValueKind == JsonValueKind.String)
        {
            // Algunos modelos responden «23,45» o «23,45 €» pese a pedir punto decimal.
            var texto = v.GetString()!.Replace("€", "").Trim().Replace(',', '.');
            if (!decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out n)) return null;
        }
        else
        {
            return null;
        }
        return n > 0 && n <= AltaGasto.ImporteMaximo && decimal.Round(n, 2) == n ? n : null;
    }

    private static string? Comercio(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("comercio", out var v) || v.ValueKind != JsonValueKind.String) return null;
        var limpio = NotaDeVoz.Limpiar(v.GetString(), MaxComercio);
        return limpio.Length == 0 ? null : limpio;
    }

    private static DateOnly? Fecha(JsonElement raiz, DateOnly hoy)
    {
        if (!raiz.TryGetProperty("fecha", out var v) || v.ValueKind != JsonValueKind.String) return null;
        if (!DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return null;
        // Un día de margen por la zona horaria; antes de 2000 es casi seguro una mala lectura del año.
        return f > hoy.AddDays(1) || f.Year < 2000 ? null : f;
    }
}
