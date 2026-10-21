using System.Globalization;

namespace MiParte.Assistant.Api.Chat;

/// <summary>
/// Reglas del alta de un gasto desde el chat, compartidas por la herramienta que propone el borrador y por la
/// confirmación que lo guarda. Categoría y perfil nunca los pone el modelo ni el cliente: salen de los marcados como
/// predeterminados para gastos del hogar.
/// </summary>
public static class AltaGasto
{
    /// <summary>Máximo de importe admitido por Core.Api.</summary>
    public const decimal ImporteMaximo = 9_999_999_999.99m;

    /// <summary>Máximo de caracteres del concepto (igual que Core.Api).</summary>
    public const int MaxConcepto = 200;

    /// <summary>Mensaje cuando no hay categoría predeterminada para gastos.</summary>
    public const string SinCategoria =
        "No hay ninguna categoría marcada como predeterminada para gastos. Pide a un administrador que marque la que usáis en Categorías.";

    /// <summary>Mensaje cuando no hay perfil de reparto predeterminado para gastos.</summary>
    public const string SinPerfil =
        "No hay ningún perfil de reparto marcado como predeterminado para gastos. Pide a un administrador que lo marque en la configuración del hogar.";

    /// <summary>Mensaje cuando la categoría o el perfil predeterminados cambiaron después de mostrar el borrador.</summary>
    public const string PredeterminadosCambiados =
        "Los predeterminados para gastos han cambiado desde que se propuso el gasto, así que no lo he guardado. Pide de nuevo el gasto para ver la propuesta actualizada.";

    /// <summary>Comprueba el importe: mayor que 0, como mucho 2 decimales y dentro del máximo de Core.Api.</summary>
    /// <param name="importe">Importe en euros.</param>
    /// <exception cref="SolicitudInvalidaException">El importe no vale.</exception>
    public static void ValidarImporte(decimal importe)
    {
        if (importe <= 0) throw new SolicitudInvalidaException("El importe debe ser mayor que 0.");
        if (importe > ImporteMaximo) throw new SolicitudInvalidaException("El importe es demasiado grande.");
        if (decimal.Round(importe, 2) != importe) throw new SolicitudInvalidaException("El importe admite como máximo 2 decimales.");
    }

    /// <summary>Concepto sin espacios sobrantes; obligatorio y de hasta <see cref="MaxConcepto"/> caracteres.</summary>
    /// <param name="concepto">Texto del concepto.</param>
    /// <exception cref="SolicitudInvalidaException">El concepto está vacío o es demasiado largo.</exception>
    public static string ValidarConcepto(string? concepto)
    {
        var limpio = concepto?.Trim() ?? "";
        if (limpio.Length == 0) throw new SolicitudInvalidaException("Indica el concepto del gasto.");
        if (limpio.Length > MaxConcepto) throw new SolicitudInvalidaException($"El concepto no puede pasar de {MaxConcepto} caracteres.");
        return limpio;
    }

    /// <summary>Fecha en formato <c>AAAA-MM-DD</c>.</summary>
    /// <param name="texto">Fecha escrita.</param>
    /// <exception cref="SolicitudInvalidaException">El formato no es válido.</exception>
    public static DateOnly ParsearFecha(string? texto)
    {
        if (!DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            throw new SolicitudInvalidaException("La fecha debe tener el formato AAAA-MM-DD.");
        return fecha;
    }

    /// <summary>
    /// El único candidato marcado como predeterminado, o null si no hay exactamente uno (el índice único de la base de
    /// datos ya impide más de uno; esto cubre el caso de ninguno).
    /// </summary>
    /// <typeparam name="T">Tipo de la categoría o del perfil.</typeparam>
    /// <param name="marcados">Candidatos marcados como predeterminados.</param>
    public static T? Unico<T>(IReadOnlyList<T> marcados) where T : class => marcados.Count == 1 ? marcados[0] : null;
}
