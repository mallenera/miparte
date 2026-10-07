using MiParte.Contracts;

namespace MiParte.Web.Hogares;

/// <summary>
/// Asigna a cada miembro un color de la paleta de 8 tonos (0-7). La API no guarda color, así que se deriva del orden
/// por identificador entre los miembros del hogar: es estable mientras no se añadan miembros con un id menor.
/// </summary>
public static class ColorMiembro
{
    /// <summary>Número de colores de la paleta (<c>--m0</c>..<c>--m7</c> en app.css).</summary>
    public const int Colores = 8;

    /// <summary>Índice de color 0-7 del miembro dentro de la lista de miembros del hogar.</summary>
    /// <param name="id">Identificador del miembro.</param>
    /// <param name="miembros">Miembros del hogar.</param>
    public static int De(Guid id, IEnumerable<MiembroDto> miembros)
    {
        var orden = miembros.Select(m => m.Id).OrderBy(g => g).ToList();
        var i = orden.IndexOf(id);
        return i < 0 ? Colores - 1 : i % Colores;
    }
}
