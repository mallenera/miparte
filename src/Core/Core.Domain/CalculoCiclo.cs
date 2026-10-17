using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>
/// Reglas de la liquidación por ciclos: qué gastos siguen pendientes y qué resume un ciclo al cerrarse. Son puras (sin persistencia):
/// el servicio lee los gastos y decide cuál es el ciclo.
/// </summary>
public static class CalculoCiclo
{
    /// <summary>Gastos que todavía no los liquida ningún ciclo. Un gasto liquidado sigue consultable, pero ya no cuenta como pendiente.</summary>
    public static IReadOnlyList<Gasto> Pendientes(IEnumerable<Gasto> gastos)
        => gastos.Where(g => g.CicloLiquidacionId is null).ToList();

    /// <summary>
    /// Total y fechas extremas de los gastos que liquida un ciclo. Exige al menos un gasto: un ciclo sin gastos no se cierra.
    /// </summary>
    /// <exception cref="ArgumentException">La lista está vacía.</exception>
    public static (decimal Total, DateOnly Desde, DateOnly Hasta) Resumir(IReadOnlyList<Gasto> gastos)
    {
        if (gastos.Count == 0) throw new ArgumentException("Un ciclo necesita al menos un gasto.", nameof(gastos));
        return (gastos.Sum(g => g.Importe), gastos.Min(g => g.Fecha), gastos.Max(g => g.Fecha));
    }
}
