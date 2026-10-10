using System.Globalization;
using MiParte.Contracts;

namespace MiParte.Web;

/// <summary>Formatos de importes, meses y nombres compartidos por las vistas, para que cambiarlos sea un solo cambio.</summary>
public static class Formato
{
    private static readonly CultureInfo Es = new("es-ES");

    /// <summary>Importe en euros con la cultura española (por ejemplo «12,50 €»).</summary>
    /// <param name="importe">Importe a mostrar.</param>
    public static string Euros(decimal importe) => importe.ToString("C2", Es);

    /// <summary>Mes en el formato <c>YYYY-MM</c> que usa la API.</summary>
    /// <param name="fecha">Cualquier día del mes.</param>
    public static string Mes(DateOnly fecha) => fecha.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Nombre del miembro con ese id, o «—» si no está en la lista.</summary>
    /// <param name="id">Identificador del miembro.</param>
    /// <param name="miembros">Miembros del hogar.</param>
    public static string NombreMiembro(Guid id, IEnumerable<MiembroDto> miembros) =>
        miembros.FirstOrDefault(m => m.Id == id)?.Nombre ?? "—";
}
