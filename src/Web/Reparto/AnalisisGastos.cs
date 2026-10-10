using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>Tramo de tiempo en el que se agrupa la serie temporal del dashboard.</summary>
public enum Granularidad
{
    /// <summary>Un punto por día (periodos de hasta 31 días).</summary>
    Dia,
    /// <summary>Un punto por semana natural, de lunes a domingo (hasta 120 días).</summary>
    Semana,
    /// <summary>Un punto por mes natural (periodos más largos).</summary>
    Mes,
}

/// <summary>Gasto de un tramo de la serie temporal.</summary>
/// <param name="Inicio">Primer día del tramo (el del periodo si el tramo empieza antes).</param>
/// <param name="Fin">Último día del tramo (el del periodo si el tramo termina después).</param>
/// <param name="Granularidad">Tramo al que pertenece el punto.</param>
/// <param name="Importe">Gasto del hogar en el tramo.</param>
public sealed record PuntoSerie(DateOnly Inicio, DateOnly Fin, Granularidad Granularidad, decimal Importe);

/// <summary>Gasto acumulado de una categoría de primer nivel (con sus subcategorías).</summary>
/// <param name="CategoriaId">Categoría raíz; <see cref="Guid.Empty"/> para el agrupado «Otras».</param>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="Importe">Gasto del periodo en la categoría y sus descendientes.</param>
/// <param name="Gastos">Número de gastos.</param>
public sealed record GastoPorCategoria(Guid CategoriaId, string Nombre, decimal Importe, int Gastos);

/// <summary>Dinero de un miembro en el periodo.</summary>
/// <param name="MiembroId">Miembro, o null si lo pagó la cuenta común o el ahorro.</param>
/// <param name="Pagado">Importe de los gastos del hogar que adelantó.</param>
/// <param name="Asumido">Parte de los gastos del hogar que le corresponde según el reparto guardado.</param>
public sealed record GastoPorMiembro(Guid? MiembroId, decimal Pagado, decimal Asumido);

/// <summary>Resultado del análisis de un periodo para los indicadores y gráficos del dashboard.</summary>
/// <param name="Desde">Primer día del periodo.</param>
/// <param name="Hasta">Último día del periodo.</param>
/// <param name="Total">Gasto del hogar (sin gastos personales, como el resumen mensual).</param>
/// <param name="Personales">Gastos personales del periodo, fuera de la liquidación.</param>
/// <param name="NumeroGastos">Número de gastos del hogar.</param>
/// <param name="MediaDiaria">Total entre los días del periodo.</param>
/// <param name="TotalAnterior">Gasto del hogar en el periodo anterior de la misma duración.</param>
/// <param name="Variacion">Cambio relativo frente al periodo anterior (0,12 = +12 %); null si el anterior no tuvo gasto.</param>
/// <param name="CategoriaPrincipal">Categoría de primer nivel con más gasto, o null sin gastos.</param>
/// <param name="MayorGasto">Gasto individual de mayor importe, o null sin gastos.</param>
/// <param name="Serie">Gasto por tramo, con todos los tramos del periodo (también los de 0).</param>
/// <param name="PorCategoria">Categorías de primer nivel de mayor a menor gasto.</param>
/// <param name="PorMiembro">Pagado y asumido por miembro, de mayor a menor pagado.</param>
/// <param name="Mayores">Los gastos de mayor importe del periodo.</param>
public sealed record ResultadoAnalisis(
    DateOnly Desde, DateOnly Hasta, decimal Total, decimal Personales, int NumeroGastos, decimal MediaDiaria,
    decimal TotalAnterior, decimal? Variacion, GastoPorCategoria? CategoriaPrincipal, GastoResponse? MayorGasto,
    IReadOnlyList<PuntoSerie> Serie, IReadOnlyList<GastoPorCategoria> PorCategoria, IReadOnlyList<GastoPorMiembro> PorMiembro,
    IReadOnlyList<GastoResponse> Mayores);

/// <summary>Cálculo de los indicadores del dashboard a partir de los gastos de un periodo y del anterior.</summary>
public static class AnalisisGastos
{
    /// <summary>Días máximos del periodo para agrupar por día.</summary>
    public const int MaxDiasPorDia = 31;

    /// <summary>Días máximos del periodo para agrupar por semana (más allá se agrupa por mes).</summary>
    public const int MaxDiasPorSemana = 120;

    /// <summary>Número de gastos de la lista de mayores importes.</summary>
    public const int NumeroMayores = 5;

    /// <summary>Días del periodo, ambos extremos incluidos.</summary>
    /// <param name="desde">Primer día.</param>
    /// <param name="hasta">Último día.</param>
    public static int Dias(DateOnly desde, DateOnly hasta) => hasta.DayNumber - desde.DayNumber + 1;

    /// <summary>Primer día del periodo anterior, de la misma duración y justo antes de <paramref name="desde"/>.</summary>
    /// <param name="desde">Primer día del periodo actual.</param>
    /// <param name="hasta">Último día del periodo actual.</param>
    public static DateOnly InicioAnterior(DateOnly desde, DateOnly hasta) => desde.AddDays(-Dias(desde, hasta));

    /// <summary>Tramo con el que se agrupa un periodo de la duración indicada.</summary>
    /// <param name="dias">Días del periodo.</param>
    public static Granularidad GranularidadPara(int dias) =>
        dias <= MaxDiasPorDia ? Granularidad.Dia : dias <= MaxDiasPorSemana ? Granularidad.Semana : Granularidad.Mes;

    /// <summary>Calcula los indicadores del periodo; <paramref name="gastos"/> puede incluir también el periodo anterior.</summary>
    /// <param name="gastos">Gastos entre <see cref="InicioAnterior"/> y <paramref name="hasta"/>.</param>
    /// <param name="categorias">Categorías del hogar, para agrupar por primer nivel.</param>
    /// <param name="desde">Primer día del periodo.</param>
    /// <param name="hasta">Último día del periodo.</param>
    public static ResultadoAnalisis Calcular(
        IReadOnlyCollection<GastoResponse> gastos, IReadOnlyCollection<CategoriaDto> categorias, DateOnly desde, DateOnly hasta)
    {
        var dias = Dias(desde, hasta);
        var anterior = InicioAnterior(desde, hasta);
        var actuales = gastos.Where(g => g.Fecha >= desde && g.Fecha <= hasta).ToList();
        var delHogar = actuales.Where(g => !g.Personal).ToList();
        var total = delHogar.Sum(g => g.Importe);
        var totalAnterior = gastos.Where(g => !g.Personal && g.Fecha >= anterior && g.Fecha < desde).Sum(g => g.Importe);

        var porCategoria = AgruparPorCategoria(delHogar, categorias);
        var porMiembro = AgruparPorMiembro(delHogar);

        return new ResultadoAnalisis(
            desde, hasta, total, actuales.Where(g => g.Personal).Sum(g => g.Importe), delHogar.Count,
            dias > 0 ? decimal.Round(total / dias, 2) : 0m,
            totalAnterior, totalAnterior > 0 ? (total - totalAnterior) / totalAnterior : null,
            porCategoria.FirstOrDefault(), delHogar.OrderByDescending(g => g.Importe).FirstOrDefault(),
            Serie(delHogar, desde, hasta), porCategoria, porMiembro,
            delHogar.OrderByDescending(g => g.Importe).ThenByDescending(g => g.Fecha).Take(NumeroMayores).ToList());
    }

    /// <summary>Gastos que cumplen el filtro cruzado del dashboard (categoría de primer nivel y/o quién paga).</summary>
    /// <param name="gastos">Gastos a filtrar.</param>
    /// <param name="categorias">Categorías del hogar, para resolver la de primer nivel.</param>
    /// <param name="filtro">Selección activa.</param>
    public static List<GastoResponse> Filtrar(
        IReadOnlyCollection<GastoResponse> gastos, IReadOnlyCollection<CategoriaDto> categorias, FiltroAnalisis filtro)
    {
        if (!filtro.Activo) return gastos.ToList();
        var porId = categorias.ToDictionary(c => c.Id);
        return gastos
            .Where(g => filtro.Categoria is not { } c || Raiz(porId, g.CategoriaId) == c)
            .Where(g => filtro.Miembro is not { } m || g.PagadoPor == m)
            .Where(g => !filtro.CuentaComun || g.PagadoPor is null)
            .ToList();
    }

    /// <summary>Categoría de primer nivel de la categoría dada.</summary>
    private static Guid Raiz(Dictionary<Guid, CategoriaDto> porId, Guid id)
    {
        // El límite evita un bucle si los datos tuvieran un ciclo de padres.
        for (var i = 0; i <= porId.Count && porId.TryGetValue(id, out var c) && c.CategoriaPadreId is { } padre && porId.ContainsKey(padre); i++)
            id = padre;
        return id;
    }

    private static List<PuntoSerie> Serie(IReadOnlyCollection<GastoResponse> gastos, DateOnly desde, DateOnly hasta)
    {
        var granularidad = GranularidadPara(Dias(desde, hasta));
        var importes = gastos.GroupBy(g => InicioTramo(g.Fecha, granularidad)).ToDictionary(x => x.Key, x => x.Sum(g => g.Importe));
        var puntos = new List<PuntoSerie>();
        for (var tramo = InicioTramo(desde, granularidad); tramo <= hasta; tramo = Siguiente(tramo, granularidad))
        {
            var fin = Siguiente(tramo, granularidad).AddDays(-1);
            puntos.Add(new PuntoSerie(tramo < desde ? desde : tramo, fin > hasta ? hasta : fin, granularidad, importes.GetValueOrDefault(tramo)));
        }
        return puntos;
    }

    private static DateOnly InicioTramo(DateOnly fecha, Granularidad g) => g switch
    {
        Granularidad.Dia => fecha,
        Granularidad.Semana => fecha.AddDays(-(((int)fecha.DayOfWeek + 6) % 7)),
        _ => new DateOnly(fecha.Year, fecha.Month, 1),
    };

    private static DateOnly Siguiente(DateOnly inicio, Granularidad g) => g switch
    {
        Granularidad.Dia => inicio.AddDays(1),
        Granularidad.Semana => inicio.AddDays(7),
        _ => inicio.AddMonths(1),
    };

    private static List<GastoPorCategoria> AgruparPorCategoria(IReadOnlyCollection<GastoResponse> gastos, IReadOnlyCollection<CategoriaDto> categorias)
    {
        var porId = categorias.ToDictionary(c => c.Id);

        return gastos.GroupBy(g => Raiz(porId, g.CategoriaId))
            .Select(x => new GastoPorCategoria(x.Key, porId.TryGetValue(x.Key, out var c) ? c.Nombre : "Sin categoría", x.Sum(g => g.Importe), x.Count()))
            .OrderByDescending(c => c.Importe).ThenBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<GastoPorMiembro> AgruparPorMiembro(IReadOnlyCollection<GastoResponse> gastos)
    {
        // Guid.Empty hace de «sin miembro» (cuenta común o ahorro): un Dictionary no admite claves nulas.
        var pagado = gastos.GroupBy(g => g.PagadoPor ?? Guid.Empty).ToDictionary(x => x.Key, x => x.Sum(g => g.Importe));
        var asumido = gastos.SelectMany(g => g.Repartos).GroupBy(r => r.MiembroId).ToDictionary(x => x.Key, x => x.Sum(r => r.ImporteAsumido));
        return pagado.Keys.Union(asumido.Keys)
            .Select(m => new GastoPorMiembro(m == Guid.Empty ? null : m, pagado.GetValueOrDefault(m), asumido.GetValueOrDefault(m)))
            .OrderByDescending(m => m.Pagado).ThenByDescending(m => m.Asumido)
            .ToList();
    }
}

/// <summary>Selección activa del dashboard: al pinchar en un gráfico se filtran los demás, como en un cuadro de mando.</summary>
/// <param name="Categoria">Categoría de primer nivel elegida, o null.</param>
/// <param name="Miembro">Persona que paga elegida, o null.</param>
/// <param name="CuentaComun">Solo lo pagado por la cuenta común o el ahorro (sin persona).</param>
public sealed record FiltroAnalisis(Guid? Categoria = null, Guid? Miembro = null, bool CuentaComun = false)
{
    /// <summary>Hay alguna selección.</summary>
    public bool Activo => Categoria is not null || Miembro is not null || CuentaComun;
}
