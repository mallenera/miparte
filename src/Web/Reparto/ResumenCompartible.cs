using System.Globalization;
using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>
/// Texto en claro de la liquidación de un mes, para copiarlo o enviarlo a quien no usa la aplicación.
/// Solo lleva nombres (ni ids ni datos de otros hogares) y los importes con coma decimal y «€».
/// </summary>
public static class ResumenCompartible
{
    private static readonly CultureInfo Es = new("es-ES");

    /// <summary>Línea que sustituye a las transferencias cuando el mes está saldado.</summary>
    public const string TodoCuadrado = "Todo cuadrado";

    /// <summary>Genera el texto del resumen: título con hogar y mes, y una línea por transferencia o «Todo cuadrado».</summary>
    /// <param name="nombreHogar">Nombre del hogar, tal como lo ve la persona.</param>
    /// <param name="mes">Cualquier día del mes resumido.</param>
    /// <param name="liquidacion">Liquidación del mes (saldos y transferencias ya descontados los pagos).</param>
    /// <param name="miembros">Miembros del hogar, por si algún id de transferencia no aparece en los saldos.</param>
    /// <param name="porCiclos">
    /// Si la liquidación es por ciclos: sus transferencias son los gastos pendientes desde el último cierre, no los de <paramref name="mes"/>,
    /// así que el título no lleva mes.
    /// </param>
    public static string Generar(
        string nombreHogar, DateOnly mes, LiquidacionResponse liquidacion, IEnumerable<MiembroDto> miembros, bool porCiclos = false)
    {
        // Los saldos traen el nombre; la lista de miembros cubre el caso raro de una transferencia sin saldo.
        var nombres = miembros.ToDictionary(m => m.Id, m => m.Nombre);
        foreach (var s in liquidacion.Saldos) nombres[s.MiembroId] = s.Nombre;

        string Nombre(Guid id) => nombres.TryGetValue(id, out var n) ? n : "Miembro";

        var periodo = porCiclos ? "gastos pendientes de liquidar" : mes.ToString("MMMM yyyy", Es);
        var lineas = new List<string>
        {
            $"Resumen de {nombreHogar} · {periodo}",
            "",
        };

        if (liquidacion.Transferencias.Count == 0)
            lineas.Add(TodoCuadrado);
        else
            lineas.AddRange(liquidacion.Transferencias.Select(t => $"{Nombre(t.De)} debe a {Nombre(t.A)} {Importe(t.Importe)}"));

        return string.Join('\n', lineas);
    }

    /// <summary>Importe con coma decimal y «€», con espacio normal (el de Formato lleva espacio no separable).</summary>
    private static string Importe(decimal importe) => Formato.Euros(importe).Replace(' ', ' ');
}
