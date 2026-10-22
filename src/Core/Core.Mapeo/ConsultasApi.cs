using MiParte.Contracts;
using MiParte.Core.Application.Gastos;

namespace MiParte.Core.Mapeo;

/// <summary>
/// Interpretación de los parámetros de consulta del contrato HTTP. Compartida por <c>Core.Api</c> y el servidor del modo demo, de
/// modo que ambos validan y responden igual.
/// </summary>
public static class ConsultasApi
{
    /// <summary>Longitud máxima del texto de búsqueda de gastos.</summary>
    public const int MaxBuscar = 200;

    /// <summary>
    /// Convierte los parámetros de <c>GET /api/gastos</c> en un <see cref="FiltroGastos"/>. El mes y el rango se combinan con «y»
    /// (intersección). Devuelve el mensaje de error 400 o null si todo es válido.
    /// </summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="desde">Primer día incluido, YYYY-MM-DD.</param>
    /// <param name="hasta">Último día incluido, YYYY-MM-DD.</param>
    /// <param name="categoriaId">Categoría concreta.</param>
    /// <param name="miembroId">Miembro que paga o asume una parte mayor que 0.</param>
    /// <param name="buscar">Texto del concepto.</param>
    /// <param name="filtro">Filtro resultante si no hay error.</param>
    public static string? FiltroGastos(
        string? mes, string? desde, string? hasta, Guid? categoriaId, Guid? miembroId, string? buscar, out FiltroGastos filtro)
    {
        filtro = new FiltroGastos(null, null, null, null, null);
        // Sin ningún límite de fechas se devolvería todo el histórico del hogar, con sus repartos: se exige al menos uno.
        if (mes is null && string.IsNullOrEmpty(desde) && string.IsNullOrEmpty(hasta))
            return "Indica el mes («mes») o al menos una fecha («desde» o «hasta»).";

        DateOnly? primero = null, ultimo = null;
        if (mes is not null)
        {
            if (!FormatosApi.TryMes(mes, out var inicio)) return "El mes debe tener el formato YYYY-MM.";
            primero = inicio;
            ultimo = inicio.AddMonths(1).AddDays(-1);
        }

        DateOnly? rangoDesde = null, rangoHasta = null;
        if (!string.IsNullOrEmpty(desde))
        {
            if (!FormatosApi.TryFecha(desde, out var f)) return "La fecha «desde» debe tener el formato YYYY-MM-DD.";
            rangoDesde = f;
        }

        if (!string.IsNullOrEmpty(hasta))
        {
            if (!FormatosApi.TryFecha(hasta, out var f)) return "La fecha «hasta» debe tener el formato YYYY-MM-DD.";
            rangoHasta = f;
        }

        if (rangoDesde is { } p && rangoHasta is { } u && p > u) return "La fecha «desde» no puede ser posterior a «hasta».";
        var texto = buscar?.Trim();
        if (texto is { Length: > MaxBuscar }) return $"El texto de búsqueda no puede pasar de {MaxBuscar} caracteres.";

        filtro = new FiltroGastos(Mayor(primero, rangoDesde), Menor(ultimo, rangoHasta), categoriaId, miembroId, texto);
        return null;
    }

    /// <summary>
    /// Valida el periodo de <c>GET /api/cuenta-comun/movimientos</c>: ambas fechas obligatorias (YYYY-MM-DD), <c>desde</c> no posterior a
    /// <c>hasta</c>. Devuelve el mensaje de error 400 o null si es válido.
    /// </summary>
    /// <param name="desde">Primer día incluido.</param>
    /// <param name="hasta">Último día incluido.</param>
    /// <param name="inicio">Primer día si no hay error.</param>
    /// <param name="fin">Último día si no hay error.</param>
    public static string? PeriodoMovimientos(string? desde, string? hasta, out DateOnly inicio, out DateOnly fin)
    {
        fin = default;
        if (!FormatosApi.TryFecha(desde, out inicio)) return "La fecha «desde» es obligatoria y debe tener el formato YYYY-MM-DD.";
        if (!FormatosApi.TryFecha(hasta, out fin)) return "La fecha «hasta» es obligatoria y debe tener el formato YYYY-MM-DD.";
        return inicio > fin ? "La fecha «desde» no puede ser posterior a «hasta»." : null;
    }

    /// <summary>La fecha más tardía de las dadas (la que más restringe un límite inferior); null si no hay ninguna.</summary>
    private static DateOnly? Mayor(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a > b ? a : b;

    /// <summary>La fecha más temprana de las dadas (la que más restringe un límite superior); null si no hay ninguna.</summary>
    private static DateOnly? Menor(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a < b ? a : b;
}
