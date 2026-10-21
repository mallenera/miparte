using System.Runtime.CompilerServices;
using System.Text;

namespace MiParte.Web.Api;

/// <summary>
/// Lee un flujo <c>text/event-stream</c> (SSE) y entrega cada evento como su nombre y sus datos. Sigue el formato
/// habitual: líneas <c>event:</c> y <c>data:</c>, y una línea vacía que cierra cada evento.
/// </summary>
public static class LectorSse
{
    /// <summary>Eventos del flujo, en orden. Un evento sin <c>event:</c> se llama <c>message</c>.</summary>
    /// <param name="flujo">Cuerpo de la respuesta.</param>
    /// <param name="ct">Token de cancelación.</param>
    public static async IAsyncEnumerable<(string Evento, string Datos)> LeerAsync(
        Stream flujo, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var lector = new StreamReader(flujo, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string? evento = null;
        var datos = new StringBuilder();
        while (await lector.ReadLineAsync(ct) is { } linea)
        {
            if (linea.Length == 0)
            {
                if (datos.Length > 0 || evento is not null) yield return (evento ?? "message", datos.ToString());
                evento = null;
                datos.Clear();
            }
            else if (linea.StartsWith("event:", StringComparison.Ordinal)) evento = linea[6..].Trim();
            else if (linea.StartsWith("data:", StringComparison.Ordinal))
            {
                if (datos.Length > 0) datos.Append('\n');
                datos.Append(linea[5..].TrimStart());
            }
        }

        // Evento final sin línea vacía (el servidor cortó después de los datos).
        if (datos.Length > 0) yield return (evento ?? "message", datos.ToString());
    }
}
