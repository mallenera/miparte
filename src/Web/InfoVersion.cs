using System.Globalization;
using System.Reflection;

namespace MiParte.Web;

/// <summary>Datos de la versión desplegada del front, para el pie de página.</summary>
public static class InfoVersion
{
    /// <summary>Fecha en que se compiló el front (la inyecta el <c>.csproj</c> como metadato del ensamblado); nula si falta.</summary>
    public static DateOnly? FechaCompilacion { get; } = Leer(typeof(InfoVersion).Assembly);

    /// <summary>Lee la fecha del metadato <c>FechaCompilacion</c> del ensamblado.</summary>
    /// <param name="ensamblado">Ensamblado a consultar.</param>
    public static DateOnly? Leer(Assembly ensamblado)
    {
        var valor = ensamblado.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "FechaCompilacion")?.Value;
        return DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;
    }
}
