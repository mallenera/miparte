namespace MiParte.Core.Domain;

/// <summary>
/// Aportación fija mensual de un miembro a la cuenta común, vigente desde el primer día de <paramref name="Desde"/>.
/// De <paramref name="Importe"/>, <paramref name="Ahorro"/> va a ahorro y el resto queda para gastos.
/// </summary>
public sealed record AportacionVigente(Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro = 0m);

/// <summary>Gasto cargado a la cuenta común, adelantado por <paramref name="PagadoPor"/> o pagado directamente por la cuenta si es null (sin reembolso pendiente).</summary>
public sealed record GastoDeCuenta(Guid? PagadoPor, DateOnly Fecha, decimal Importe);

/// <summary>Reembolso de la cuenta común a quien adelantó un gasto.</summary>
public sealed record ReembolsoDeCuenta(Guid MiembroId, DateOnly Fecha, decimal Importe);

/// <summary>Dinero que el hogar saca del ahorro de la cuenta común.</summary>
public sealed record RetiradaDeAhorro(DateOnly Fecha, decimal Importe);

/// <summary>Lo que la cuenta común debe a un miembro por gastos que adelantó y aún no le ha reembolsado.</summary>
public sealed record PendienteMiembro(Guid MiembroId, decimal Importe);

/// <summary>
/// Estado de la cuenta común al final de un mes. <see cref="Aportado"/> incluye la parte de ahorro;
/// <see cref="Saldo"/> es lo aportado para gastos (sin ahorro) menos lo gastado;
/// <see cref="Efectivo"/> es el saldo más lo pendiente de reembolsar, es decir, el dinero de gastos que hay en la cuenta
/// (los gastos que adelanta una persona no bajan el dinero de la cuenta hasta reembolsarlos; los que paga la cuenta directamente sí).
/// El ahorro va aparte: <see cref="AhorroAcumulado"/> menos <see cref="AhorroRetirado"/> es <see cref="AhorroDisponible"/>.
/// </summary>
public sealed record EstadoCuentaComun(
    decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteMiembro> Pendientes, decimal Efectivo,
    decimal AhorroMes, decimal AhorroAcumulado, decimal AhorroRetirado, decimal AhorroDisponible);

/// <summary>Cálculo del saldo acumulado de la cuenta común; no depende de persistencia.</summary>
public static class CuentaComun
{
    /// <summary>Primer día del mes de <paramref name="fecha"/>.</summary>
    public static DateOnly InicioMes(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);

    /// <summary>Aportación vigente de un miembro en <paramref name="mes"/>: la más reciente que no pase de ese mes (null si no tiene).</summary>
    private static AportacionVigente? Vigente(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => aportaciones.Where(a => a.MiembroId == miembroId && a.Desde <= mes).OrderByDescending(a => a.Desde).FirstOrDefault();

    /// <summary>Importe que aporta un miembro en <paramref name="mes"/>: el de su aportación vigente más reciente (0 si no tiene).</summary>
    public static decimal AportacionDelMes(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => Vigente(miembroId, mes, aportaciones)?.Importe ?? 0m;

    /// <summary>Parte de la aportación de un miembro en <paramref name="mes"/> que va a ahorro (0 si no tiene aportación).</summary>
    public static decimal AhorroDelMes(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => Vigente(miembroId, mes, aportaciones)?.Ahorro ?? 0m;

    /// <summary>
    /// Estado de la cuenta al terminar <paramref name="mes"/> (primer día). Las aportaciones se suman mes a mes
    /// desde la primera de cada miembro; los gastos, reembolsos y retiradas de ahorro, hasta el último día del mes.
    /// </summary>
    public static EstadoCuentaComun Calcular(
        DateOnly mes, IReadOnlyList<AportacionVigente> aportaciones,
        IEnumerable<GastoDeCuenta> gastos, IEnumerable<ReembolsoDeCuenta> reembolsos,
        IEnumerable<RetiradaDeAhorro>? retiradas = null)
    {
        var fin = mes.AddMonths(1);
        var aportado = 0m;
        var aportadoMes = 0m;
        var ahorro = 0m;
        var ahorroMes = 0m;
        foreach (var m in aportaciones.Select(a => a.MiembroId).Distinct())
        {
            var primera = aportaciones.Where(a => a.MiembroId == m).Min(a => a.Desde);
            for (var d = primera; d <= mes; d = d.AddMonths(1))
            {
                var vigente = Vigente(m, d, aportaciones)!;
                aportado += vigente.Importe;
                ahorro += vigente.Ahorro;
                if (d == mes)
                {
                    aportadoMes += vigente.Importe;
                    ahorroMes += vigente.Ahorro;
                }
            }
        }

        var gastosHasta = gastos.Where(g => g.Fecha < fin).ToList();
        var gastado = gastosHasta.Sum(g => g.Importe);
        var reembolsosHasta = reembolsos.Where(r => r.Fecha < fin).ToList();
        var retirado = (retiradas ?? []).Where(r => r.Fecha < fin).Sum(r => r.Importe);

        var pendientes = gastosHasta.Where(g => g.PagadoPor is not null).Select(g => (Id: g.PagadoPor!.Value, Importe: g.Importe))
            .Concat(reembolsosHasta.Select(r => (Id: r.MiembroId, Importe: -r.Importe)))
            .GroupBy(x => x.Id)
            .Select(x => new PendienteMiembro(x.Key, x.Sum(y => y.Importe)))
            .Where(p => p.Importe != 0).OrderBy(p => p.MiembroId).ToList();

        var saldo = aportado - ahorro - gastado;
        return new EstadoCuentaComun(
            aportadoMes, aportado, gastado, saldo, pendientes, saldo + pendientes.Sum(p => p.Importe),
            ahorroMes, ahorro, retirado, ahorro - retirado);
    }
}
