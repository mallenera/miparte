using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>Una categoría con su profundidad en el árbol (0 = primer nivel).</summary>
/// <param name="Categoria">La categoría.</param>
/// <param name="Nivel">Profundidad: 0 para primer nivel, 1 para subcategoría, etc.</param>
public sealed record NodoCategoria(CategoriaDto Categoria, int Nivel);

/// <summary>Ordena categorías en árbol y calcula descendientes (para no elegir un padre que cree un ciclo).</summary>
public static class ArbolCategorias
{
    /// <summary>Lista plana en profundidad: cada categoría seguida de sus subcategorías, ordenadas por nombre.</summary>
    /// <param name="categorias">Categorías del hogar.</param>
    public static List<NodoCategoria> Aplanar(IReadOnlyCollection<CategoriaDto> categorias)
    {
        var ids = categorias.Select(c => c.Id).ToHashSet();
        var hijos = categorias.Where(c => c.CategoriaPadreId is { } p && ids.Contains(p))
            .GroupBy(c => c.CategoriaPadreId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList());
        var resultado = new List<NodoCategoria>();

        void Recorrer(CategoriaDto c, int nivel)
        {
            resultado.Add(new NodoCategoria(c, nivel));
            if (hijos.TryGetValue(c.Id, out var lista)) foreach (var h in lista) Recorrer(h, nivel + 1);
        }

        // Raíces: sin padre, o con un padre que ya no está en la lista (no se pierde ninguna).
        foreach (var raiz in categorias.Where(c => c.CategoriaPadreId is not { } p || !ids.Contains(p))
                     .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase))
            Recorrer(raiz, 0);
        return resultado;
    }

    /// <summary>Identificadores de la categoría y de todas sus descendientes.</summary>
    /// <param name="categorias">Categorías del hogar.</param>
    /// <param name="id">Categoría de partida.</param>
    public static HashSet<Guid> ConDescendientes(IReadOnlyCollection<CategoriaDto> categorias, Guid id)
    {
        var resultado = new HashSet<Guid> { id };
        bool crecio;
        do
        {
            crecio = false;
            foreach (var c in categorias)
                if (c.CategoriaPadreId is { } p && resultado.Contains(p) && resultado.Add(c.Id)) crecio = true;
        } while (crecio);
        return resultado;
    }
}
