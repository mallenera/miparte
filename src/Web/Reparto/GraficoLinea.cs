using System.Globalization;

namespace MiParte.Web.Reparto;

/// <summary>Un arco del rosco de «quién paga».</summary>
/// <param name="Clase">Clase CSS del color (la de la persona o la de la cuenta común).</param>
/// <param name="Trazo">Valor de <c>stroke-dasharray</c>: la circunferencia mide 100 unidades, así el trazo es el porcentaje.</param>
/// <param name="Desfase">Valor de <c>stroke-dashoffset</c> (empieza arriba y va en sentido horario).</param>
/// <param name="Miembro">Persona del arco; null si es la cuenta común o el ahorro.</param>
/// <param name="Elegida">Si es la selección del filtro cruzado.</param>
public sealed record Rodaja(string Clase, string Trazo, string Desfase, Guid? Miembro, bool Elegida);

/// <summary>
/// Geometría del gráfico de evolución del Dashboard: coordenadas del SVG para una serie de tramos. Se calcula aparte del componente
/// porque son cuentas puras (y la CSP obliga a pintar con atributos y clases, no con estilos en línea).
/// </summary>
/// <param name="serie">Tramos del periodo, en orden; al menos uno.</param>
public sealed class GraficoLinea(IReadOnlyList<PuntoSerie> serie)
{
    private static readonly CultureInfo Es = new("es-ES");

    /// <summary>Ancho del lienzo SVG.</summary>
    public const double Ancho = 520;

    /// <summary>Alto del lienzo SVG.</summary>
    public const double Alto = 220;

    /// <summary>Margen izquierdo (sitio para los importes del eje).</summary>
    public const double MargenIzq = 58;

    /// <summary>Margen derecho.</summary>
    public const double MargenDer = 14;

    /// <summary>Margen superior.</summary>
    public const double MargenSup = 14;

    /// <summary>Margen inferior (sitio para las fechas del eje).</summary>
    public const double MargenInf = 30;

    /// <summary>Ancho del área de dibujo.</summary>
    public const double AnchoUtil = Ancho - MargenIzq - MargenDer;

    /// <summary>Alto del área de dibujo.</summary>
    public const double AltoUtil = Alto - MargenSup - MargenInf;

    /// <summary>Número en formato invariante con hasta dos decimales, apto para atributos SVG.</summary>
    public static string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Tramos de la serie.</summary>
    public IReadOnlyList<PuntoSerie> Serie => serie;

    /// <summary>Importe máximo de la serie (el eje Y llega hasta él).</summary>
    public double MaxSerie { get; } = Math.Max(1.0, (double)serie.Max(p => p.Importe));

    /// <summary>Coordenada X del tramo <paramref name="i"/>.</summary>
    public double X(int i) => serie.Count == 1 ? MargenIzq + AnchoUtil / 2 : MargenIzq + AnchoUtil * i / (serie.Count - 1);

    /// <summary>Coordenada Y de un importe.</summary>
    public double Y(decimal v) => MargenSup + AltoUtil * (1 - (double)v / MaxSerie);

    /// <summary>Trazo de la línea de evolución.</summary>
    public string Linea => string.Join(' ', serie.Select((p, i) => $"{(i == 0 ? "M" : "L")}{N(X(i))} {N(Y(p.Importe))}"));

    /// <summary>Área bajo la línea, cerrada hasta el eje.</summary>
    public string Area => $"{Linea} L{N(X(serie.Count - 1))} {N(MargenSup + AltoUtil)} L{N(X(0))} {N(MargenSup + AltoUtil)} Z";

    /// <summary>Líneas guía horizontales: cero, mitad y máximo, con su importe.</summary>
    public IEnumerable<(string Y, string YTexto, string Texto)> Guias =>
        new[] { 0.0, 0.5, 1.0 }.Select(f =>
        {
            var y = MargenSup + AltoUtil * (1 - f);
            return (N(y), N(y + 4), ((decimal)(MaxSerie * f)).ToString("N0", Es) + " €");
        });

    /// <summary>Se rotula como mucho cada cuarto de la serie, para que las etiquetas no se pisen en pantallas estrechas.</summary>
    public int PasoEtiquetas => Math.Max(1, (int)Math.Ceiling((serie.Count - 1) / 4.0));

    /// <summary>Si el tramo <paramref name="i"/> lleva etiqueta en el eje.</summary>
    public bool RotulaEje(int i) => i % PasoEtiquetas == 0 || i == serie.Count - 1;

    /// <summary>Alineación de la etiqueta del eje: el primero a la izquierda, el último a la derecha.</summary>
    public string AnclaEje(int i) => i == 0 ? "start" : i == serie.Count - 1 ? "end" : "middle";

    /// <summary>Texto completo de un tramo (para tooltips y lectores de pantalla).</summary>
    public static string EtiquetaTramo(PuntoSerie p) => p.Granularidad switch
    {
        Granularidad.Dia => p.Inicio.ToString("d MMM yyyy", Es),
        Granularidad.Semana => "Semana del " + p.Inicio.ToString("d MMM yyyy", Es),
        _ => Es.TextInfo.ToTitleCase(p.Inicio.ToString("MMMM yyyy", Es)),
    };

    /// <summary>Texto corto de un tramo para el eje.</summary>
    public static string EtiquetaEje(PuntoSerie p) => p.Granularidad switch
    {
        Granularidad.Mes => p.Inicio.ToString("MMM yy", Es),
        _ => p.Inicio.ToString("d MMM", Es),
    };

    /// <summary>
    /// Arcos del rosco de quién paga: uno por persona (o por la cuenta común) con dinero adelantado, sobre una circunferencia
    /// de 100 unidades.
    /// </summary>
    /// <param name="porMiembro">Pagado por cada persona.</param>
    /// <param name="colorDe">Índice de color (0-7) de una persona.</param>
    /// <param name="filtro">Selección actual, para marcar el arco elegido.</param>
    public static IEnumerable<Rodaja> Rodajas(IReadOnlyList<GastoPorMiembro> porMiembro, Func<Guid, int> colorDe, FiltroAnalisis filtro)
    {
        var total = porMiembro.Sum(m => m.Pagado);
        var acumulado = 0.0;
        foreach (var m in porMiembro.Where(m => m.Pagado > 0))
        {
            var parte = (double)(m.Pagado / total) * 100;
            var clase = m.MiembroId is { } id ? $"dn-m{colorDe(id)}" : "dn-comun";
            var elegida = m.MiembroId is { } mid ? filtro.Miembro == mid : filtro.CuentaComun;
            yield return new Rodaja(clase, $"{N(parte)} {N(100 - parte)}", N(25 - acumulado), m.MiembroId, elegida);
            acumulado += parte;
        }
    }
}
