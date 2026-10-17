using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Texto con el que un modo de liquidación viaja por la API y se guarda en base de datos.</summary>
public static class TextoModoLiquidacion
{
    /// <summary>Convierte el modo al texto: mensual o ciclos.</summary>
    public static string ATexto(ModoLiquidacion m) => m == ModoLiquidacion.Ciclos ? "ciclos" : "mensual";

    /// <summary>
    /// Interpreta el texto del modo (sin distinguir mayúsculas ni espacios laterales). Un texto nulo o vacío es
    /// <see cref="ModoLiquidacion.Mensual"/>; false si no corresponde a ningún modo.
    /// </summary>
    public static bool TryParse(string? texto, out ModoLiquidacion modo)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case null or "" or "mensual": modo = ModoLiquidacion.Mensual; return true;
            case "ciclos": modo = ModoLiquidacion.Ciclos; return true;
            default: modo = default; return false;
        }
    }
}
