namespace MiParte.Core.Domain;

/// <summary>Aportación fija mensual de un miembro a la cuenta común, vigente desde el primer día de <paramref name="Desde"/>.</summary>
public sealed record AportacionVigente(Guid MiembroId, DateOnly Desde, decimal Importe);

/// <summary>Gasto cargado a la cuenta común, adelantado por <paramref name="PagadoPor"/> o pagado directamente por la cuenta si es null (sin reembolso pendiente).</summary>
public sealed record GastoDeCuenta(Guid? PagadoPor, DateOnly Fecha, decimal Importe);

/// <summary>Reembolso de la cuenta común a quien adelantó un gasto.</summary>
public sealed record ReembolsoDeCuenta(Guid MiembroId, DateOnly Fecha, decimal Importe);

/// <summary>Lo que la cuenta común debe a un miembro por gastos que adelantó y aún no le ha reembolsado.</summary>
public sealed record PendienteMiembro(Guid MiembroId, decimal Importe);

/// <summary>
/// Estado de la cuenta común al final de un mes. <see cref="Saldo"/> es aportado menos gastado;
/// <see cref="Efectivo"/> es lo aportado menos lo ya reembolsado, es decir, el saldo más lo pendiente de reembolsar
/// (los gastos que adelanta una persona no bajan el dinero de la cuenta hasta reembolsarlos; los que paga la cuenta directamente sí).
/// </summary>
public sealed record EstadoCuentaComun(
    decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteMiembro> Pendientes, decimal Efectivo);

/// <summary>Cálculo del saldo acumulado de la cuenta común; no depende de persistencia.</summary>
public static class CuentaComun
{
    /// <summary>Primer día del mes de <paramref name="fecha"/>.</summary>
    public static DateOnly InicioMes(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);

    /// <summary>Importe que aporta un miembro en <paramref name="mes"/>: el de su aportación vigente más reciente (0 si no tiene).</summary>
    public static decimal AportacionDelMes(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => aportaciones.Where(a => a.MiembroId == miembroId && a.Desde <= mes)
            .OrderByDescending(a => a.Desde).Select(a => a.Importe).FirstOrDefault();

    /// <summary>
    /// Estado de la cuenta al terminar <paramref name="mes"/> (primer día). Las aportaciones se suman mes a mes
    /// desde la primera de cada miembro; los gastos y reembolsos, hasta el último día del mes.
    /// </summary>
    public static EstadoCuentaComun Calcular(
        DateOnly mes, IReadOnlyList<AportacionVigente> aportaciones,
        IEnumerable<GastoDeCuenta> gastos, IEnumerable<ReembolsoDeCuenta> reembolsos)
    {
        var fin = mes.AddMonths(1);
        var aportado = 0m;
        var aportadoMes = 0m;
        foreach (var m in aportaciones.Select(a => a.MiembroId).Distinct())
        {
            var primera = aportaciones.Where(a => a.MiembroId == m).Min(a => a.Desde);
            for (var d = primera; d <= mes; d = d.AddMonths(1))
            {
                var importe = AportacionDelMes(m, d, aportaciones);
                aportado += importe;
                if (d == mes) aportadoMes += importe;
            }
        }

        var gastosHasta = gastos.Where(g => g.Fecha < fin).ToList();
        var gastado = gastosHasta.Sum(g => g.Importe);
        var reembolsosHasta = reembolsos.Where(r => r.Fecha < fin).ToList();

        var pendientes = gastosHasta.Where(g => g.PagadoPor is not null).Select(g => (Id: g.PagadoPor!.Value, Importe: g.Importe))
            .Concat(reembolsosHasta.Select(r => (Id: r.MiembroId, Importe: -r.Importe)))
            .GroupBy(x => x.Id)
            .Select(x => new PendienteMiembro(x.Key, x.Sum(y => y.Importe)))
            .Where(p => p.Importe != 0).OrderBy(p => p.MiembroId).ToList();

        var saldo = aportado - gastado;
        return new EstadoCuentaComun(aportadoMes, aportado, gastado, saldo, pendientes, saldo + pendientes.Sum(p => p.Importe));
    }
}
