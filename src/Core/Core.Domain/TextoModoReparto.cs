using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Texto con el que un modo de reparto viaja por la API y se guarda en base de datos.</summary>
public static class TextoModoReparto
{
    /// <summary>Convierte el modo al texto: porcentaje, partes, cuenta_comun o individual.</summary>
    public static string ATexto(ModoReparto m) => m switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.CuentaComun => "cuenta_comun",
        _ => "individual",
    };

    /// <summary>Interpreta el texto del modo (sin distinguir mayúsculas ni espacios laterales); false si no es válido.</summary>
    public static bool TryParse(string? texto, out ModoReparto modo)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case "porcentaje": modo = ModoReparto.Porcentaje; return true;
            case "partes": modo = ModoReparto.Partes; return true;
            case "cuenta_comun": modo = ModoReparto.CuentaComun; return true;
            case "individual": modo = ModoReparto.Individual; return true;
            default: modo = default; return false;
        }
    }
}
