using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Texto con el que un tipo de grupo viaja por la API y se guardará en base de datos.</summary>
public static class TextoTipoGrupo
{
    /// <summary>Convierte el tipo al texto: hogar, piso_compartido, viaje, evento u otro.</summary>
    public static string ATexto(TipoGrupo t) => t switch
    {
        TipoGrupo.PisoCompartido => "piso_compartido",
        TipoGrupo.Viaje => "viaje",
        TipoGrupo.Evento => "evento",
        TipoGrupo.Otro => "otro",
        _ => "hogar",
    };

    /// <summary>
    /// Interpreta el texto del tipo (sin distinguir mayúsculas ni espacios laterales). Un texto nulo o vacío
    /// es <see cref="TipoGrupo.Hogar"/>, el valor por defecto; false si el texto no corresponde a ningún tipo.
    /// </summary>
    public static bool TryParse(string? texto, out TipoGrupo tipo)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case null or "": tipo = TipoGrupo.Hogar; return true;
            case "hogar": tipo = TipoGrupo.Hogar; return true;
            case "piso_compartido": tipo = TipoGrupo.PisoCompartido; return true;
            case "viaje": tipo = TipoGrupo.Viaje; return true;
            case "evento": tipo = TipoGrupo.Evento; return true;
            case "otro": tipo = TipoGrupo.Otro; return true;
            default: tipo = default; return false;
        }
    }
}
