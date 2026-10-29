namespace MiParte.Core.Domain;

/// <summary>Reglas de la lista de la compra del hogar: longitudes, cantidades y vigencia de los enlaces para añadir por QR.</summary>
public static class ReglasListaCompra
{
    /// <summary>Longitud máxima del nombre de un producto.</summary>
    public const int MaxNombre = 100;

    /// <summary>Cantidad máxima de un producto.</summary>
    public const int MaxCantidad = 999;

    /// <summary>Vigencia por defecto de un enlace para añadir por QR (7 días).</summary>
    public static readonly TimeSpan VigenciaEnlace = TimeSpan.FromDays(7);

    /// <summary>Valida el nombre (sin espacios sobrantes, no vacío, como máximo <see cref="MaxNombre"/>); devuelve el mensaje de error o null.</summary>
    public static string? ValidarNombre(string? nombre)
    {
        var n = nombre?.Trim() ?? "";
        if (n.Length == 0) return "El producto necesita un nombre.";
        if (n.Length > MaxNombre) return $"El nombre no puede tener más de {MaxNombre} caracteres.";
        return null;
    }

    /// <summary>Nombre normalizado: sin espacios sobrantes.</summary>
    public static string NormalizarNombre(string nombre) => nombre.Trim();

    /// <summary>Valida la cantidad: entero entre 1 y <see cref="MaxCantidad"/>; devuelve el mensaje de error o null.</summary>
    public static string? ValidarCantidad(int cantidad)
        => cantidad is < 1 or > MaxCantidad ? $"La cantidad debe estar entre 1 y {MaxCantidad}." : null;

    /// <summary>Si un enlace sigue admitiendo añadir o marcar: no revocado y sin caducar en <paramref name="ahora"/>.</summary>
    public static bool Vigente(bool activo, DateTimeOffset? caducaEn, DateTimeOffset ahora)
        => Estado(activo, caducaEn, ahora) == EstadoEnlaceLista.Vigente;

    /// <summary>Estado de un enlace en <paramref name="ahora"/>: revocado si se revocó (aunque haya caducado), caducado si ya pasó su fecha, vigente si no.</summary>
    public static EstadoEnlaceLista Estado(bool activo, DateTimeOffset? caducaEn, DateTimeOffset ahora)
        => !activo ? EstadoEnlaceLista.Revocado : caducaEn is { } c && c <= ahora ? EstadoEnlaceLista.Caducado : EstadoEnlaceLista.Vigente;

    /// <summary>Texto con que viaja el estado en la API.</summary>
    public static string TextoEstado(EstadoEnlaceLista estado) => estado switch
    {
        EstadoEnlaceLista.Revocado => "revocado",
        EstadoEnlaceLista.Caducado => "caducado",
        _ => "vigente",
    };
}

/// <summary>Estado de un enlace QR de la lista de la compra.</summary>
public enum EstadoEnlaceLista
{
    /// <summary>Admite ver, añadir y marcar productos.</summary>
    Vigente,

    /// <summary>Un miembro lo revocó.</summary>
    Revocado,

    /// <summary>Pasó su fecha de caducidad sin revocarse.</summary>
    Caducado,
}
