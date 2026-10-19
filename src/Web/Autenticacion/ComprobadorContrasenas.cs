using System.Security.Cryptography;
using System.Text;

namespace MiParte.Web.Autenticacion;

/// <summary>
/// Comprueba si una contraseña aparece en filtraciones conocidas con la API de Pwned Passwords (HaveIBeenPwned) y
/// k-anonymity: solo sale del navegador el prefijo de cinco caracteres del SHA-1 de la contraseña, y la respuesta trae
/// todos los sufijos con ese prefijo, así que la coincidencia se hace en local. La contraseña nunca se envía.
/// Es la alternativa gratuita a «Leaked password protection» de Supabase, que exige plan Pro.
/// </summary>
public sealed class ComprobadorContrasenas(HttpClient http)
{
    /// <summary>Longitud del prefijo SHA-1 que se envía a la API.</summary>
    private const int LongitudPrefijo = 5;

    /// <summary>
    /// Devuelve <c>true</c> si la contraseña figura en una filtración, <c>false</c> si no, y <c>null</c> si no se pudo
    /// comprobar (red caída, respuesta inesperada). El llamador decide: aquí un fallo externo no bloquea el registro.
    /// </summary>
    /// <param name="contrasena">Contraseña escrita por la persona.</param>
    /// <param name="ct">Token de cancelación.</param>
    public async Task<bool?> EstaFiltradaAsync(string contrasena, CancellationToken ct = default)
    {
        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(contrasena)));
        var prefijo = sha1[..LongitudPrefijo];
        var sufijo = sha1[LongitudPrefijo..];

        try
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, $"range/{prefijo}");
            // Add-Padding: la API rellena la respuesta con sufijos falsos (cuenta 0) para no revelar cuántos hay de verdad.
            peticion.Headers.Add("Add-Padding", "true");
            using var respuesta = await http.SendAsync(peticion, ct);
            if (!respuesta.IsSuccessStatusCode) return null;

            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            foreach (var linea in texto.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var partes = linea.Trim().Split(':');
                if (partes.Length != 2 || !string.Equals(partes[0], sufijo, StringComparison.OrdinalIgnoreCase)) continue;
                return int.TryParse(partes[1], out var veces) && veces > 0;
            }
            return false;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Tiempo de espera de la propia petición (no cancelación del usuario): se trata como no comprobada.
            return null;
        }
    }
}
