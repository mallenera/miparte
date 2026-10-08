using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>Una categoría con su profundidad en el árbol (0 = primer nivel).</summary>
/// <param name="Categoria">La categoría.</param>
/// <param name="Nivel">Profundidad: 0 para primer nivel, 1 para subcategoría, etc.</param>
public sealed record NodoCategoria(CategoriaDto Categoria, int Nivel);

/// <summary>Categoría del resumen mensual con su gasto propio y el acumulado de sus subcategorías.</summary>
/// <param name="CategoriaId">Identificador de la categoría (vacío si no está en el árbol del hogar).</param>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="Propio">Gasto imputado directamente a esta categoría.</param>
/// <param name="Total">Gasto propio más el de todas sus descendientes; cada gasto cuenta una sola vez.</param>
/// <param name="PorMiembro">Desglose del total acumulado por miembro.</param>
/// <param name="Hijos">Subcategorías con gasto, de mayor a menor total.</param>
public sealed record NodoResumen(
    Guid CategoriaId, string Nombre, decimal Propio, decimal Total,
    IReadOnlyList<ImporteMiembroDto> PorMiembro, IReadOnlyList<NodoResumen> Hijos);

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

    /// <summary>Nombre completo de una categoría, con sus ancestros («Comida › Supermercado»); «—» si no existe.</summary>
    /// <param name="categorias">Categorías del hogar.</param>
    /// <param name="id">Categoría a nombrar.</param>
    public static string Ruta(IReadOnlyCollection<CategoriaDto> categorias, Guid id)
    {
        var porId = categorias.ToDictionary(c => c.Id);
        if (!porId.TryGetValue(id, out var actual)) return "—";
        var partes = new List<string> { actual.Nombre };
        // El tope de pasos protege de un ciclo que la API ya impide pero el front no debe colgarse por él.
        while (actual.CategoriaPadreId is { } p && porId.TryGetValue(p, out actual) && partes.Count <= categorias.Count)
            partes.Insert(0, actual.Nombre);
        return string.Join(" › ", partes);
    }

    /// <summary>
    /// Convierte las categorías planas del resumen en un árbol con totales acumulados. Las madres sin gasto propio
    /// aparecen si alguna descendiente gastó; una categoría que no está en <paramref name="categorias"/> cuelga de la raíz.
    /// </summary>
    /// <param name="categorias">Categorías del hogar (para conocer los padres).</param>
    /// <param name="resumen">Totales por categoría que devuelve la API (gasto propio de cada una).</param>
    public static List<NodoResumen> ResumenEnArbol(
        IReadOnlyCollection<CategoriaDto> categorias, IReadOnlyCollection<ResumenCategoriaDto> resumen)
    {
        var porId = categorias.ToDictionary(c => c.Id);
        var propios = resumen.GroupBy(r => r.CategoriaId).ToDictionary(g => g.Key, g => g.First());

        Guid? PadreDe(Guid id) => porId.TryGetValue(id, out var c) && c.CategoriaPadreId is { } p && porId.ContainsKey(p) ? p : null;

        // Hijos por padre entre las categorías con gasto y todos sus ancestros.
        var hijosDe = new Dictionary<Guid, HashSet<Guid>>();
        var raices = new HashSet<Guid>();
        foreach (var r in resumen)
        {
            var actual = r.CategoriaId;
            var pasos = 0;
            while (pasos++ <= porId.Count)
            {
                if (PadreDe(actual) is not { } padre) { raices.Add(actual); break; }
                if (!hijosDe.TryGetValue(padre, out var set)) hijosDe[padre] = set = [];
                if (!set.Add(actual)) break;
                actual = padre;
            }
        }

        NodoResumen Construir(Guid id, int profundidad)
        {
            var hijos = profundidad <= porId.Count && hijosDe.TryGetValue(id, out var ids)
                ? ids.Select(h => Construir(h, profundidad + 1)).OrderByDescending(n => n.Total).ThenBy(n => n.Nombre).ToList()
                : [];
            propios.TryGetValue(id, out var propio);
            var porMiembro = (propio?.PorMiembro ?? []).Concat(hijos.SelectMany(h => h.PorMiembro))
                .GroupBy(m => m.MiembroId).Select(g => new ImporteMiembroDto(g.Key, g.Sum(m => m.Importe))).ToList();
            var nombre = porId.TryGetValue(id, out var cat) ? cat.Nombre : propio?.Nombre ?? "—";
            return new NodoResumen(id, nombre, propio?.Total ?? 0m, (propio?.Total ?? 0m) + hijos.Sum(h => h.Total), porMiembro, hijos);
        }

        return raices.Select(r => Construir(r, 0)).OrderByDescending(n => n.Total).ThenBy(n => n.Nombre).ToList();
    }
}
