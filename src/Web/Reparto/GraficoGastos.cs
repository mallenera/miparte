namespace MiParte.Web.Reparto;

/// <summary>Reglas de presentación de los gráficos del dashboard que no dependen de Blazor ni del SVG.</summary>
public static class GraficoGastos
{
    /// <summary>Etiqueta con la que se agrupa el resto de categorías.</summary>
    public const string EtiquetaOtras = "Otras categorías";

    /// <summary>
    /// Deja como máximo <paramref name="max"/> barras: las primeras categorías y el resto agrupado en «Otras categorías», para que el
    /// gráfico se lea en una pantalla pequeña. La categoría elegida siempre se ve: si quedaría en el resto, ocupa el lugar de la última visible.
    /// </summary>
    /// <param name="todas">Categorías ordenadas de más a menos gasto.</param>
    /// <param name="max">Número máximo de barras, incluida la de «Otras».</param>
    /// <param name="elegida">Categoría seleccionada en el filtro cruzado; null si no hay.</param>
    public static IReadOnlyList<GastoPorCategoria> AgruparResto(IReadOnlyList<GastoPorCategoria> todas, int max, Guid? elegida)
    {
        if (todas.Count <= max) return todas;

        var visibles = todas.Take(max - 1).ToList();
        if (elegida is { } id && todas.FirstOrDefault(c => c.CategoriaId == id) is { } c && !visibles.Contains(c))
            visibles[^1] = c;
        var resto = todas.Where(x => !visibles.Contains(x)).ToList();
        return visibles
            .Append(new GastoPorCategoria(Guid.Empty, EtiquetaOtras, resto.Sum(x => x.Importe), resto.Sum(x => x.Gastos)))
            .ToList();
    }

    /// <summary>Porcentaje (0-100) que supone <paramref name="parte"/> sobre <paramref name="total"/>; 0 si el total no es positivo.</summary>
    public static decimal PorCiento(decimal parte, decimal total) => total > 0 ? parte / total * 100m : 0m;
}
