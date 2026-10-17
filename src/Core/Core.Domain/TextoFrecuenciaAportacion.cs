namespace MiParte.Core.Domain;

/// <summary>Texto con el que una frecuencia de aportación viaja por la API y se guarda en base de datos.</summary>
public static class TextoFrecuenciaAportacion
{
    /// <summary>Convierte la frecuencia a su texto: unica, mensual, bimestral, trimestral, semestral o anual.</summary>
    public static string ATexto(FrecuenciaAportacion f) => f switch
    {
        FrecuenciaAportacion.Unica => "unica",
        FrecuenciaAportacion.Mensual => "mensual",
        FrecuenciaAportacion.Bimestral => "bimestral",
        FrecuenciaAportacion.Trimestral => "trimestral",
        FrecuenciaAportacion.Semestral => "semestral",
        FrecuenciaAportacion.Anual => "anual",
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    /// <summary>
    /// Interpreta el texto (sin distinguir mayúsculas ni espacios laterales). Un texto nulo o vacío es
    /// <see cref="FrecuenciaAportacion.Mensual"/>, como en los hogares existentes; false si no corresponde a ninguna.
    /// </summary>
    public static bool TryParse(string? texto, out FrecuenciaAportacion frecuencia)
    {
        switch (texto?.Trim().ToLowerInvariant())
        {
            case null or "" or "mensual": frecuencia = FrecuenciaAportacion.Mensual; return true;
            case "unica": frecuencia = FrecuenciaAportacion.Unica; return true;
            case "bimestral": frecuencia = FrecuenciaAportacion.Bimestral; return true;
            case "trimestral": frecuencia = FrecuenciaAportacion.Trimestral; return true;
            case "semestral": frecuencia = FrecuenciaAportacion.Semestral; return true;
            case "anual": frecuencia = FrecuenciaAportacion.Anual; return true;
            default: frecuencia = default; return false;
        }
    }
}
