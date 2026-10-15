namespace MiParte.Core.Domain;

/// <summary>
/// Datos por defecto de un hogar nuevo: los cuatro perfiles de reparto y las categorías de ejemplo. Replica la semilla de la
/// función SQL crear_hogar. El creador recibe 1 parte en «Por partes» y el 100 % en «Porcentaje fijo», para que un hogar de un
/// solo adulto funcione desde el inicio.
/// </summary>
public static class SemillaHogar
{
    /// <summary>Nombre del perfil de reparto a cargo de la cuenta común (sin reparto entre personas).</summary>
    public const string PerfilCuentaComun = "Cuenta común";

    /// <summary>Nombre del perfil de reparto por partes.</summary>
    public const string PerfilPartes = "Por partes";

    /// <summary>Nombre del perfil de reparto por porcentaje fijo.</summary>
    public const string PerfilPorcentaje = "Porcentaje fijo";

    /// <summary>Nombre del perfil de reparto individual.</summary>
    public const string PerfilIndividual = "Individual";

    /// <summary>Categoría de ejemplo y nombre del perfil de reparto por defecto.</summary>
    public static readonly IReadOnlyList<(string Categoria, string Perfil)> Categorias =
    [
        ("Hipoteca/Alquiler", PerfilPartes),
        ("Alimentación", PerfilPartes),
        ("Suministros", PerfilPartes),
        ("Gastos varios de casa", PerfilPartes),
        ("Hijo", PerfilPartes),
        ("Ocio personal", PerfilIndividual),
    ];
}
