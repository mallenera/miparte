using System.Globalization;

namespace MiParte.Core.Application.Notificaciones;

/// <summary>Formato de los textos de los avisos del navegador.</summary>
public static class FormatoAvisos
{
    /// <summary>Importe con separador decimal español (12,00), sin el símbolo de moneda.</summary>
    public static string Importe(decimal importe) => importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"));
}
