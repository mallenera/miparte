using MiParte.Contracts;

namespace MiParte.Web.Hogares;

/// <summary>
/// Textos de los tipos de grupo para la interfaz: nombre y explicación de cada valor de <see cref="TiposGrupo"/>.
/// Es el único sitio donde se escriben; las páginas leen de aquí para no repetirlos.
/// </summary>
public static class NombresTiposGrupo
{
    /// <summary>Opción de un selector de tipo de grupo.</summary>
    /// <param name="Valor">Valor que viaja a la API (<see cref="TiposGrupo"/>).</param>
    /// <param name="Nombre">Nombre mostrado.</param>
    /// <param name="Descripcion">Explicación breve bajo el selector.</param>
    public sealed record Opcion(string Valor, string Nombre, string Descripcion);

    /// <summary>Opciones en el orden del selector; la primera es la que queda marcada por defecto.</summary>
    public static IReadOnlyList<Opcion> Opciones { get; } =
    [
        new(TiposGrupo.Hogar, "Hogar", "Familia o personas que conviven y comparten los gastos del día a día."),
        new(TiposGrupo.PisoCompartido, "Piso compartido", "Personas que no forman una familia y comparten piso o casa."),
        new(TiposGrupo.Viaje, "Viaje", "Gastos comunes durante un viaje o una escapada."),
        new(TiposGrupo.Evento, "Evento", "Un acontecimiento puntual, como una boda o una cena de grupo."),
        new(TiposGrupo.Otro, "Otro", "Cualquier otro grupo que comparta gastos."),
    ];

    /// <summary>Nombre del tipo; un valor desconocido se muestra como «Hogar», el tipo por defecto.</summary>
    /// <param name="tipo">Valor de <see cref="TiposGrupo"/>.</param>
    public static string Nombre(string? tipo) => Buscar(tipo).Nombre;

    /// <summary>Explicación del tipo; un valor desconocido se explica como «Hogar».</summary>
    /// <param name="tipo">Valor de <see cref="TiposGrupo"/>.</param>
    public static string Descripcion(string? tipo) => Buscar(tipo).Descripcion;

    private static Opcion Buscar(string? tipo) => Opciones.FirstOrDefault(o => o.Valor == tipo) ?? Opciones[0];
}
