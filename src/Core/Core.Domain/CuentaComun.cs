namespace MiParte.Core.Domain;

/// <summary>
/// Aportación fija mensual de un miembro a la cuenta común, vigente desde el primer día de <paramref name="Desde"/>.
/// De <paramref name="Importe"/>, <paramref name="Ahorro"/> va a ahorro y el resto queda para gastos.
/// </summary>
public sealed record AportacionVigente(Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro = 0m);

/// <summary>
/// Gasto cargado a la cuenta común, adelantado por <paramref name="PagadoPor"/> o pagado directamente por la cuenta si es null (sin reembolso pendiente).
/// Con <paramref name="DesdeAhorro"/> se descuenta del ahorro en lugar del saldo de gastos.
/// </summary>
public sealed record GastoDeCuenta(Guid? PagadoPor, DateOnly Fecha, decimal Importe, bool DesdeAhorro = false);

/// <summary>Reembolso de la cuenta común a quien adelantó un gasto.</summary>
public sealed record ReembolsoDeCuenta(Guid MiembroId, DateOnly Fecha, decimal Importe);

/// <summary>Dinero que el hogar saca del ahorro de la cuenta común.</summary>
public sealed record RetiradaDeAhorro(DateOnly Fecha, decimal Importe);

/// <summary>
/// Dinero que entra al ahorro de la cuenta común fuera de la aportación mensual (ahorro inicial, lotería...).
/// <paramref name="MiembroId"/> es quien lo registra y a quien se atribuye en «su parte» (null: no se atribuye a nadie).
/// </summary>
public sealed record DepositoDeAhorro(DateOnly Fecha, decimal Importe, Guid? MiembroId = null);

/// <summary>
/// Lo que le corresponde a un miembro de la cuenta común: lo que ha puesto y su parte proporcional del saldo y del ahorro.
/// <paramref name="Aportado"/> es lo aportado para gastos (sin ahorro) y <paramref name="Ahorrado"/> lo apartado a ahorro
/// (aportaciones más depósitos suyos). <paramref name="PorcentajeGastos"/> y <paramref name="PorcentajeAhorro"/> son su peso sobre el total (0-100, 2 decimales).
/// <paramref name="ParteSaldo"/> es su porción del saldo de gastos y <paramref name="ParteAhorro"/> la del ahorro disponible.
/// <paramref name="Pendiente"/> es lo que la cuenta le debe por gastos que adelantó.
/// </summary>
public sealed record PartePersona(
    Guid MiembroId, decimal Aportado, decimal Ahorrado, decimal PorcentajeGastos, decimal PorcentajeAhorro,
    decimal ParteSaldo, decimal ParteAhorro, decimal Pendiente);

/// <summary>Lo que la cuenta común debe a un miembro por gastos que adelantó y aún no le ha reembolsado.</summary>
public sealed record PendienteMiembro(Guid MiembroId, decimal Importe);

/// <summary>
/// Estado de la cuenta común al final de un mes. <see cref="Aportado"/> incluye la parte de ahorro;
/// <see cref="Saldo"/> es lo aportado para gastos (sin ahorro) menos lo gastado;
/// <see cref="Efectivo"/> es el saldo más lo pendiente de reembolsar, es decir, el dinero de gastos que hay en la cuenta
/// (los gastos que adelanta una persona no bajan el dinero de la cuenta hasta reembolsarlos; los que paga la cuenta directamente sí).
/// El ahorro va aparte: <see cref="AhorroAcumulado"/> (lo apartado en las aportaciones más lo depositado aparte, <see cref="AhorroDepositado"/>)
/// menos <see cref="AhorroRetirado"/> y lo gastado desde el ahorro (<see cref="AhorroGastado"/>) es <see cref="AhorroDisponible"/>.
/// </summary>
public sealed record EstadoCuentaComun(
    decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteMiembro> Pendientes, decimal Efectivo,
    decimal AhorroMes, decimal AhorroAcumulado, decimal AhorroRetirado, decimal AhorroDisponible,
    decimal AhorroDepositado = 0m, decimal AhorroGastado = 0m);

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
    /// desde la primera de cada miembro; los gastos, reembolsos y movimientos de ahorro, hasta el último día del mes.
    /// </summary>
    public static EstadoCuentaComun Calcular(
        DateOnly mes, IReadOnlyList<AportacionVigente> aportaciones,
        IEnumerable<GastoDeCuenta> gastos, IEnumerable<ReembolsoDeCuenta> reembolsos,
        IEnumerable<RetiradaDeAhorro>? retiradas = null, IEnumerable<DepositoDeAhorro>? depositos = null)
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
        var gastado = gastosHasta.Where(g => !g.DesdeAhorro).Sum(g => g.Importe);
        var ahorroGastado = gastosHasta.Where(g => g.DesdeAhorro).Sum(g => g.Importe);
        var reembolsosHasta = reembolsos.Where(r => r.Fecha < fin).ToList();
        var retirado = (retiradas ?? []).Where(r => r.Fecha < fin).Sum(r => r.Importe);
        var depositosHasta = (depositos ?? []).Where(d => d.Fecha < fin).ToList();
        var depositado = depositosHasta.Sum(d => d.Importe);
        var depositadoMes = depositosHasta.Where(d => d.Fecha >= mes).Sum(d => d.Importe);

        var pendientes = gastosHasta.Where(g => g.PagadoPor is not null).Select(g => (Id: g.PagadoPor!.Value, Importe: g.Importe))
            .Concat(reembolsosHasta.Select(r => (Id: r.MiembroId, Importe: -r.Importe)))
            .GroupBy(x => x.Id)
            .Select(x => new PendienteMiembro(x.Key, x.Sum(y => y.Importe)))
            .Where(p => p.Importe != 0).OrderBy(p => p.MiembroId).ToList();

        var saldo = aportado - ahorro - gastado;
        return new EstadoCuentaComun(
            aportadoMes, aportado, gastado, saldo, pendientes, saldo + pendientes.Sum(p => p.Importe),
            ahorroMes + depositadoMes, ahorro + depositado, retirado, ahorro + depositado - retirado - ahorroGastado, depositado, ahorroGastado);
    }

    /// <summary>
    /// «Su parte» de cada miembro con aportaciones o depósitos hasta <paramref name="mes"/>: el saldo de gastos se reparte
    /// en proporción a lo aportado para gastos y el ahorro disponible, en proporción a lo ahorrado. El céntimo sobrante lo
    /// absorbe el último miembro (por id), de modo que las partes suman siempre el saldo y el ahorro disponible.
    /// Un saldo negativo se reparte igual: es la parte de cada uno en el descubierto.
    /// </summary>
    /// <param name="mes">Primer día del mes hasta el que se calcula.</param>
    /// <param name="aportaciones">Aportaciones vigentes de todos los miembros.</param>
    /// <param name="estado">Estado de la cuenta ya calculado para ese mes (saldo, ahorro disponible y pendientes).</param>
    /// <param name="depositos">Depósitos de ahorro; solo se atribuyen los que llevan miembro.</param>
    public static IReadOnlyList<PartePersona> PartesPorPersona(
        DateOnly mes, IReadOnlyList<AportacionVigente> aportaciones, EstadoCuentaComun estado,
        IEnumerable<DepositoDeAhorro>? depositos = null)
    {
        var fin = mes.AddMonths(1);
        var aportado = new Dictionary<Guid, decimal>();
        var ahorrado = new Dictionary<Guid, decimal>();
        foreach (var m in aportaciones.Select(a => a.MiembroId).Distinct())
        {
            var primera = aportaciones.Where(a => a.MiembroId == m).Min(a => a.Desde);
            for (var d = primera; d <= mes; d = d.AddMonths(1))
            {
                var vigente = Vigente(m, d, aportaciones)!;
                aportado[m] = aportado.GetValueOrDefault(m) + vigente.Importe - vigente.Ahorro;
                ahorrado[m] = ahorrado.GetValueOrDefault(m) + vigente.Ahorro;
            }
        }

        foreach (var dep in (depositos ?? []).Where(d => d.MiembroId is not null && d.Fecha < fin))
        {
            ahorrado[dep.MiembroId!.Value] = ahorrado.GetValueOrDefault(dep.MiembroId!.Value) + dep.Importe;
            aportado.TryAdd(dep.MiembroId!.Value, 0m);
        }

        foreach (var id in aportado.Keys) ahorrado.TryAdd(id, 0m);

        var ids = aportado.Keys.OrderBy(id => id).ToList();
        var porSaldo = Prorratear(estado.Saldo, ids.Select(id => aportado[id]).ToList(), igualSiSinPesos: true);
        var porAhorro = Prorratear(estado.AhorroDisponible, ids.Select(id => ahorrado[id]).ToList());
        var totalAportado = aportado.Values.Sum();
        var totalAhorrado = ahorrado.Values.Sum();

        return ids.Select((id, i) => new PartePersona(
            id, aportado[id], ahorrado[id],
            totalAportado > 0 ? Math.Round(100m * aportado[id] / totalAportado, 2, MidpointRounding.AwayFromZero) : 0m,
            totalAhorrado > 0 ? Math.Round(100m * ahorrado[id] / totalAhorrado, 2, MidpointRounding.AwayFromZero) : 0m,
            porSaldo[i], porAhorro[i],
            estado.Pendientes.FirstOrDefault(p => p.MiembroId == id)?.Importe ?? 0m)).ToList();
    }

    /// <summary>Reparte un importe (de cualquier signo) según pesos no negativos; el último con peso absorbe el céntimo sobrante. Si todos los pesos son cero, con <paramref name="igualSiSinPesos"/> se reparte a partes iguales (la suma sigue siendo el importe) y sin él todo queda a cero.</summary>
    private static decimal[] Prorratear(decimal importe, IReadOnlyList<decimal> pesos, bool igualSiSinPesos = false)
    {
        var resultado = new decimal[pesos.Count];
        if (pesos.Count == 0 || importe == 0) return resultado;
        if (pesos.Sum() <= 0)
        {
            // Sin aportaciones que ponderar (p. ej. solo depósitos de ahorro) el saldo se reparte a partes iguales.
            if (!igualSiSinPesos) return resultado;
            pesos = pesos.Select(_ => 1m).ToList();
        }

        var total = pesos.Sum();

        var ultimo = pesos.Select((p, i) => (p, i)).Last(x => x.p > 0).i;
        var acumulado = 0m;
        for (var i = 0; i < ultimo; i++)
        {
            resultado[i] = Math.Round(importe * pesos[i] / total, 2, MidpointRounding.AwayFromZero);
            acumulado += resultado[i];
        }

        resultado[ultimo] = importe - acumulado;
        return resultado;
    }
}
