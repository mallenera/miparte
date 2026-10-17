using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>
/// Datos por defecto de un grupo nuevo: los cuatro perfiles de reparto (iguales para todos los tipos) y las categorías de ejemplo,
/// que dependen del tipo de grupo. Para <c>hogar</c> replica la semilla de la función SQL crear_hogar. El creador recibe 1 parte en
/// «Por partes» y el 100 % en «Porcentaje fijo», para que un grupo de un solo adulto funcione desde el inicio.
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

    /// <summary>
    /// Categorías de ejemplo de cada tipo de grupo, con el nombre del perfil de reparto por defecto. Es la única fuente de estas
    /// semillas: añadir un tipo o cambiar sus categorías se hace aquí, no con condicionales en el repositorio.
    /// </summary>
    private static readonly IReadOnlyDictionary<TipoGrupo, IReadOnlyList<(string Categoria, string Perfil)>> CategoriasPorTipo =
        new Dictionary<TipoGrupo, IReadOnlyList<(string Categoria, string Perfil)>>
        {
            [TipoGrupo.Hogar] =
            [
                ("Hipoteca/Alquiler", PerfilPartes),
                ("Alimentación", PerfilPartes),
                ("Suministros", PerfilPartes),
                ("Gastos varios de casa", PerfilPartes),
                ("Hijo", PerfilPartes),
                ("Ocio personal", PerfilIndividual),
            ],
            [TipoGrupo.PisoCompartido] = PorPartes(
                "Alquiler", "Suministros", "Compra común", "Internet y móvil", "Limpieza y mantenimiento", "Otros"),
            [TipoGrupo.Viaje] = PorPartes("Transporte", "Alojamiento", "Comida", "Ocio", "Compras", "Otros"),
            [TipoGrupo.Evento] = PorPartes("Proveedores", "Regalos colectivos", "Comida y bebida", "Otros"),
            [TipoGrupo.Otro] = PorPartes("General"),
        };

    /// <summary>Categorías de ejemplo del tipo de grupo indicado, en orden, con el nombre de su perfil de reparto.</summary>
    public static IReadOnlyList<(string Categoria, string Perfil)> CategoriasDe(TipoGrupo tipo) => CategoriasPorTipo[tipo];

    /// <summary>Categorías con el perfil «Por partes» (1 parte a cada miembro, reparto igualitario por defecto).</summary>
    private static IReadOnlyList<(string Categoria, string Perfil)> PorPartes(params string[] nombres)
        => nombres.Select(n => (n, PerfilPartes)).ToList();
}
