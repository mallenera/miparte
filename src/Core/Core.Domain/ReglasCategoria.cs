namespace MiParte.Core.Domain;

/// <summary>Reglas de las categorías de gasto (jerárquicas) que no dependen de persistencia.</summary>
public static class ReglasCategoria
{
    /// <summary>Longitud máxima del nombre de una categoría.</summary>
    public const int MaxLongitudNombre = 100;

    /// <summary>Normaliza y valida el nombre; devuelve el error o null y deja el nombre recortado en <paramref name="nombre"/>.</summary>
    public static string? ValidarNombre(string? texto, out string nombre)
    {
        nombre = texto?.Trim() ?? "";
        if (nombre.Length == 0) return "El nombre es obligatorio.";
        if (nombre.Length > MaxLongitudNombre) return $"El nombre admite como máximo {MaxLongitudNombre} caracteres.";
        return null;
    }

    /// <summary>
    /// Si poner <paramref name="padreId"/> como padre de <paramref name="categoriaId"/> crearía un ciclo: subiendo desde el padre
    /// por <paramref name="padres"/> (categoría → su padre) aparecería la propia categoría.
    /// </summary>
    public static bool CreariaCiclo(Guid categoriaId, Guid padreId, IReadOnlyDictionary<Guid, Guid?> padres)
    {
        var actual = (Guid?)padreId;
        var visitados = new HashSet<Guid>();
        while (actual is { } a && visitados.Add(a))
        {
            if (a == categoriaId) return true;
            actual = padres.GetValueOrDefault(a);
        }

        return false;
    }
}
