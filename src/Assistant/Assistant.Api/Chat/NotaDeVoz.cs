using System.Net.Http.Headers;
using System.Text;

namespace MiParte.Assistant.Api.Chat;

/// <summary>
/// Reglas de la nota de voz que llega a <c>POST /api/chat/audio</c>: solo los formatos que graba <c>MediaRecorder</c> en
/// los navegadores (WebM y Ogg con Opus, MP4/AAC en Safari) y comprobados por su firma, no solo por la cabecera; y limpieza
/// del texto transcrito antes de devolverlo, porque después vuelve al chat como mensaje de la persona.
/// </summary>
public static class NotaDeVoz
{
    /// <summary>Tipos MIME aceptados (sin parámetros como <c>codecs</c>).</summary>
    public static readonly IReadOnlySet<string> TiposPermitidos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/webm", "audio/ogg", "audio/mp4" };

    /// <summary>Indica si la cabecera <c>Content-Type</c> es de un formato aceptado (los parámetros se ignoran).</summary>
    /// <param name="contentType">Valor de la cabecera; puede ser nulo.</param>
    public static bool TipoPermitido(string? contentType)
        => MediaTypeHeaderValue.TryParse(contentType, out var tipo) && tipo.MediaType is { } m && TiposPermitidos.Contains(m);

    /// <summary>
    /// Comprueba que los primeros bytes corresponden a un contenedor de audio aceptado: EBML (WebM), <c>OggS</c> (Ogg) o
    /// una caja <c>ftyp</c> (MP4). Evita mandar al proveedor ficheros arbitrarios con una cabecera de audio.
    /// </summary>
    /// <param name="audio">Contenido recibido.</param>
    public static bool FirmaValida(ReadOnlySpan<byte> audio)
    {
        if (audio.Length < 12) return false;
        if (audio[0] == 0x1A && audio[1] == 0x45 && audio[2] == 0xDF && audio[3] == 0xA3) return true; // EBML (WebM)
        if (audio[..4].SequenceEqual("OggS"u8)) return true;
        return audio.Slice(4, 4).SequenceEqual("ftyp"u8); // MP4: tamaño de la caja y después «ftyp»
    }

    /// <summary>
    /// Deja el texto transcrito listo para el cuadro del chat: sin caracteres de control, espacios normalizados y como
    /// máximo <paramref name="maxCaracteres"/> (el mismo tope que un mensaje escrito).
    /// </summary>
    /// <param name="texto">Transcripción del modelo.</param>
    /// <param name="maxCaracteres">Longitud máxima.</param>
    public static string Limpiar(string? texto, int maxCaracteres)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        var espacio = false;
        foreach (var c in texto)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                espacio = sb.Length > 0;
                continue;
            }
            if (espacio) sb.Append(' ');
            espacio = false;
            sb.Append(c);
        }
        var limpio = sb.ToString();
        return limpio.Length <= maxCaracteres ? limpio : limpio[..maxCaracteres].TrimEnd();
    }
}
