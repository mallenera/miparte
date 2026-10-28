namespace MiParte.Core.Domain;

/// <summary>
/// Aportación fija mensual de un miembro a la cuenta común, vigente desde el primer día de <paramref name="Desde"/>.
/// De <paramref name="Importe"/>, <paramref name="Ahorro"/> va a ahorro y el resto queda para gastos.
/// <paramref name="Frecuencia"/> es la del hogar y decide en qué meses cobra (ver <see cref="ReglasFrecuenciaAportacion"/>).
/// </summary>
public sealed record AportacionVigente(
    Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro = 0m, FrecuenciaAportacion Frecuencia = FrecuenciaAportacion.Mensual);

/// <summary>
/// Gasto cargado a la cuenta común, adelantado por <paramref name="PagadoPor"/> o pagado directamente por la cuenta si es null (sin reembolso pendiente).
/// Con <paramref name="DesdeAhorro"/> se descuenta del ahorro en lugar del saldo de gastos.
/// </summary>
public sealed record GastoDeCuenta(Guid? PagadoPor, DateOnly Fecha, decimal Importe, bool DesdeAhorro = false);

/// <summary>
/// Reembolso de la cuenta común a quien adelantó un gasto. <paramref name="DesdeAhorro"/> indica de qué fondo sale: el del ahorro (para
/// los adelantos que se pagaron desde el ahorro) o el de Hogar. Un reembolso nunca mezcla los dos.
/// </summary>
public sealed record ReembolsoDeCuenta(Guid MiembroId, DateOnly Fecha, decimal Importe, bool DesdeAhorro = false);

/// <summary>Dinero que el hogar saca del ahorro de la cuenta común.</summary>
public sealed record RetiradaDeAhorro(DateOnly Fecha, decimal Importe);

/// <summary>
/// Dinero que entra a la cuenta para gastos fuera de las aportaciones (p. ej. lo que pagan las amigas de una cena): suma al saldo y al
/// efectivo desde su fecha, como el saldo inicial.
/// </summary>
public sealed record IngresoDeCuenta(DateOnly Fecha, decimal Importe);

/// <summary>
/// Dinero que entra al ahorro de la cuenta común fuera de la aportación mensual (ahorro inicial, lotería...).
/// <paramref name="MiembroId"/> es quien lo registra y a quien se atribuye en «su parte» (null: no se atribuye a nadie).
/// </summary>
public sealed record DepositoDeAhorro(DateOnly Fecha, decimal Importe, Guid? MiembroId = null);

/// <summary>
/// Dinero que ya había en la cuenta para gastos a <paramref name="Fecha"/>, fijado a mano (p. ej. el saldo del banco al empezar a usar la app).
/// Cuenta como un ingreso de esa fecha: los meses anteriores no lo ven y el siguiente sí.
/// </summary>
public sealed record SaldoInicialCuenta(DateOnly Fecha, decimal Importe);

/// <summary>
/// Extracto de un mes de la cuenta común, como el de un banco: el efectivo al inicio, lo que entra y lo que sale, y el efectivo al final.
/// <paramref name="Aportaciones"/> es lo que entra para gastos; <paramref name="SaldoInicialFijado"/> el saldo inicial si cae en el mes;
/// <paramref name="GastosCuenta"/> lo que paga la cuenta directamente y <paramref name="Reembolsos"/> lo que devuelve a quien adelantó.
/// <paramref name="GastosAdelantadosDelAhorro"/> sube el efectivo: lo paga el ahorro, pero la cuenta queda a deber ese importe a quien lo adelantó.
/// <paramref name="Ingresos"/> son los ingresos sueltos del mes, fuera de las aportaciones.
/// </summary>
public sealed record ExtractoCuentaComun(
    decimal EfectivoInicial, decimal Aportaciones, decimal SaldoInicialFijado, decimal GastosCuenta,
    decimal GastosAdelantadosDelAhorro, decimal Reembolsos, decimal EfectivoFinal, decimal Ingresos = 0m);

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

/// <summary>
/// Lo que la cuenta común debe a un miembro por gastos que adelantó y aún no le ha reembolsado, del fondo de Hogar o del ahorro
/// (<paramref name="DesdeAhorro"/>). Un miembro puede tener dos pendientes, uno por fondo.
/// </summary>
public sealed record PendienteMiembro(Guid MiembroId, decimal Importe, bool DesdeAhorro = false);

/// <summary>
/// Estado de la cuenta común al final de un mes. <see cref="Aportado"/> incluye la parte de ahorro;
/// <see cref="Saldo"/> es lo aportado para gastos (sin ahorro) menos lo gastado, más el saldo inicial fijado si lo hay y lo ingresado
/// aparte (<see cref="Ingresado"/>);
/// <see cref="Efectivo"/> es el saldo más lo pendiente de reembolsar, es decir, el dinero de gastos que hay en la cuenta
/// (los gastos que adelanta una persona no bajan el dinero de la cuenta hasta reembolsarlos; los que paga la cuenta directamente sí).
/// El ahorro va aparte: <see cref="AhorroAcumulado"/> (lo apartado en las aportaciones más lo depositado aparte, <see cref="AhorroDepositado"/>)
/// menos <see cref="AhorroRetirado"/> y lo gastado desde el ahorro (<see cref="AhorroGastado"/>) es <see cref="AhorroDisponible"/>.
/// </summary>
public sealed record EstadoCuentaComun(
    decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteMiembro> Pendientes, decimal Efectivo,
    decimal AhorroMes, decimal AhorroAcumulado, decimal AhorroRetirado, decimal AhorroDisponible,
    decimal AhorroDepositado = 0m, decimal AhorroGastado = 0m, decimal Ingresado = 0m);

/// <summary>
/// Una fila del resumen mensual de la cuenta común (Hogar o Ahorro): lo que había al empezar el mes (<paramref name="Inicial"/>, el disponible
/// del mes anterior), lo que entra y sale en el mes, y lo que queda (<paramref name="Disponible"/>). <paramref name="Ingresos"/> son las
/// aportaciones de la fila; <paramref name="IngresosSueltos"/>, los ingresos aparte; <paramref name="Gastos"/>, lo que sale de la fila, y
/// <paramref name="Reembolsos"/>, lo devuelto a quien adelantó gastos (solo Hogar: sale del efectivo); <paramref name="Pendiente"/>, lo que
/// aún se debe al cierre del mes por los adelantos de ese fondo (Hogar o Ahorro).
/// </summary>
public sealed record FilaResumenMensualCuenta(
    decimal Inicial, decimal Ingresos, decimal IngresosSueltos, decimal Gastos, decimal Reembolsos, decimal Disponible,
    decimal Pendiente = 0m);

/// <summary>Resumen mensual de la cuenta común: la fila de Hogar (dinero para gastos), la de Ahorro y la suma de ambas.</summary>
public sealed record ResumenMensualCuenta(FilaResumenMensualCuenta Hogar, FilaResumenMensualCuenta Ahorro, FilaResumenMensualCuenta Total);

/// <summary>Cálculo del saldo acumulado de la cuenta común; no depende de persistencia.</summary>
public static class CuentaComun
{
    /// <summary>Primer día del mes de <paramref name="fecha"/>.</summary>
    public static DateOnly InicioMes(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);

    /// <summary>Aportación vigente de un miembro en <paramref name="mes"/>: la más reciente que no pase de ese mes (null si no tiene).</summary>
    private static AportacionVigente? Vigente(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => aportaciones.Where(a => a.MiembroId == miembroId && a.Desde <= mes).OrderByDescending(a => a.Desde).FirstOrDefault();

    /// <summary>
    /// Aportación que cobra un miembro en <paramref name="mes"/>: la vigente, si ese mes es de cobro según su frecuencia;
    /// null si no cobra ese mes (o no tiene aportación vigente).
    /// </summary>
    private static AportacionVigente? Cobro(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
    {
        var vigente = Vigente(miembroId, mes, aportaciones);
        return vigente is not null && ReglasFrecuenciaAportacion.EsMesDeCobro(vigente.Frecuencia, vigente.Desde, mes) ? vigente : null;
    }

    /// <summary>Importe que aporta un miembro en <paramref name="mes"/>: lo que cobra ese mes (0 si no cobra).</summary>
    public static decimal AportacionDelMes(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => Cobro(miembroId, mes, aportaciones)?.Importe ?? 0m;

    /// <summary>Parte de lo que cobra un miembro en <paramref name="mes"/> que va a ahorro (0 si no cobra).</summary>
    public static decimal AhorroDelMes(Guid miembroId, DateOnly mes, IEnumerable<AportacionVigente> aportaciones)
        => Cobro(miembroId, mes, aportaciones)?.Ahorro ?? 0m;

    /// <summary>
    /// Estado de la cuenta al terminar <paramref name="mes"/> (primer día). Las aportaciones se suman mes a mes
    /// desde la primera de cada miembro; los gastos, reembolsos, ingresos y movimientos de ahorro, hasta el último día del mes.
    /// </summary>
    public static EstadoCuentaComun Calcular(
        DateOnly mes, IReadOnlyList<AportacionVigente> aportaciones,
        IEnumerable<GastoDeCuenta> gastos, IEnumerable<ReembolsoDeCuenta> reembolsos,
        IEnumerable<RetiradaDeAhorro>? retiradas = null, IEnumerable<DepositoDeAhorro>? depositos = null,
        SaldoInicialCuenta? saldoInicial = null, IEnumerable<IngresoDeCuenta>? ingresos = null)
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
                if (Cobro(m, d, aportaciones) is not { } cobro) continue;
                aportado += cobro.Importe;
                ahorro += cobro.Ahorro;
                if (d == mes)
                {
                    aportadoMes += cobro.Importe;
                    ahorroMes += cobro.Ahorro;
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

        var pendientes = gastosHasta.Where(g => g.PagadoPor is not null).Select(g => (Id: g.PagadoPor!.Value, g.DesdeAhorro, Importe: g.Importe))
            .Concat(reembolsosHasta.Select(r => (Id: r.MiembroId, r.DesdeAhorro, Importe: -r.Importe)))
            .GroupBy(x => (x.Id, x.DesdeAhorro))
            .Select(x => new PendienteMiembro(x.Key.Id, x.Sum(y => y.Importe), x.Key.DesdeAhorro))
            .Where(p => p.Importe != 0).OrderBy(p => p.MiembroId).ThenBy(p => p.DesdeAhorro).ToList();

        var inicial = saldoInicial is { } si && si.Fecha < fin ? si.Importe : 0m;
        var ingresado = (ingresos ?? []).Where(i => i.Fecha < fin).Sum(i => i.Importe);
        var saldo = aportado - ahorro - gastado + inicial + ingresado;
        return new EstadoCuentaComun(
            aportadoMes, aportado, gastado, saldo, pendientes, saldo + pendientes.Sum(p => p.Importe),
            ahorroMes + depositadoMes, ahorro + depositado, retirado, ahorro + depositado - retirado - ahorroGastado, depositado, ahorroGastado,
            ingresado);
    }

    /// <summary>
    /// Extracto del mes <paramref name="mes"/> (primer día), a partir de su estado ya calculado. El efectivo inicial es el final menos
    /// los movimientos del mes, así que coincide con el efectivo final del mes anterior.
    /// </summary>
    /// <param name="mes">Primer día del mes.</param>
    /// <param name="aportaciones">Aportaciones vigentes de todos los miembros.</param>
    /// <param name="gastosDelMes">Gastos a cargo de la cuenta; solo cuentan los que tienen fecha en el mes.</param>
    /// <param name="reembolsosDelMes">Reembolsos; solo cuentan los del mes.</param>
    /// <param name="saldoInicial">Saldo inicial fijado, si lo hay.</param>
    /// <param name="estado">Estado de la cuenta al final de <paramref name="mes"/>.</param>
    /// <param name="ingresosDelMes">Ingresos sueltos; solo cuentan los del mes.</param>
    public static ExtractoCuentaComun Extracto(
        DateOnly mes, IReadOnlyList<AportacionVigente> aportaciones, IEnumerable<GastoDeCuenta> gastosDelMes,
        IEnumerable<ReembolsoDeCuenta> reembolsosDelMes, SaldoInicialCuenta? saldoInicial, EstadoCuentaComun estado,
        IEnumerable<IngresoDeCuenta>? ingresosDelMes = null)
    {
        var fin = mes.AddMonths(1);
        var aportado = aportaciones.Select(a => a.MiembroId).Distinct()
            .Sum(m => AportacionDelMes(m, mes, aportaciones) - AhorroDelMes(m, mes, aportaciones));
        var gastos = gastosDelMes.Where(g => g.Fecha >= mes && g.Fecha < fin).ToList();
        var directos = gastos.Where(g => g.PagadoPor is null && !g.DesdeAhorro).Sum(g => g.Importe);
        var adelantadosDelAhorro = gastos.Where(g => g.PagadoPor is not null && g.DesdeAhorro).Sum(g => g.Importe);
        var reembolsado = reembolsosDelMes.Where(r => r.Fecha >= mes && r.Fecha < fin).Sum(r => r.Importe);
        var fijado = saldoInicial is { } si && si.Fecha >= mes && si.Fecha < fin ? si.Importe : 0m;
        var ingresado = (ingresosDelMes ?? []).Where(i => i.Fecha >= mes && i.Fecha < fin).Sum(i => i.Importe);
        var movimientos = aportado + fijado + ingresado - directos + adelantadosDelAhorro - reembolsado;
        return new ExtractoCuentaComun(
            estado.Efectivo - movimientos, aportado, fijado, directos, adelantadosDelAhorro, reembolsado, estado.Efectivo, ingresado);
    }

    /// <summary>
    /// Resumen del mes <paramref name="mes"/> (primer día) en filas de Hogar y Ahorro. El inicial de cada fila es el disponible del mes
    /// anterior, así que el saldo se acumula, y el resto de columnas son solo del mes. El disponible de Hogar es el efectivo de
    /// <see cref="Calcular"/> y el de Ahorro su ahorro disponible. Un gasto adelantado desde el ahorro sube el ingreso de Hogar (el
    /// dinero pasa al efectivo) y el gasto de Ahorro; una retirada solo sale del ahorro.
    /// </summary>
    /// <param name="mes">Primer día del mes.</param>
    /// <param name="h">Histórico de la cuenta hasta el fin del mes.</param>
    public static ResumenMensualCuenta ResumenMensual(DateOnly mes, HistoricoCuenta h)
    {
        var fin = mes.AddMonths(1);
        var anterior = Calcular(
            mes.AddMonths(-1), h.Vigentes, h.Gastos, h.Reembolsos, h.Retiradas, h.Depositos, h.SaldoInicial, h.Ingresos);
        bool EnMes(DateOnly f) => f >= mes && f < fin;

        var aportesGastos = h.Vigentes.Select(a => a.MiembroId).Distinct()
            .Sum(m => AportacionDelMes(m, mes, h.Vigentes) - AhorroDelMes(m, mes, h.Vigentes));
        var aportesAhorro = h.Vigentes.Select(a => a.MiembroId).Distinct().Sum(m => AhorroDelMes(m, mes, h.Vigentes));
        var gastos = (h.GastosDelMes ?? []).Where(g => EnMes(g.Fecha)).ToList();
        var directos = gastos.Where(g => g.PagadoPor is null && !g.DesdeAhorro).Sum(g => g.Importe);
        var delAhorro = gastos.Where(g => g.DesdeAhorro).Sum(g => g.Importe);
        var adelantadosDelAhorro = gastos.Where(g => g.PagadoPor is not null && g.DesdeAhorro).Sum(g => g.Importe);
        var reembolsado = h.Reembolsos.Where(r => EnMes(r.Fecha)).Sum(r => r.Importe);
        var retirado = h.Retiradas.Where(r => EnMes(r.Fecha)).Sum(r => r.Importe);
        var depositado = h.Depositos.Where(d => EnMes(d.Fecha)).Sum(d => d.Importe);
        var ingresado = (h.Ingresos ?? []).Where(i => EnMes(i.Fecha)).Sum(i => i.Importe);
        var fijado = h.SaldoInicial is { } si && EnMes(si.Fecha) ? si.Importe : 0m;

        var pendientes = Calcular(mes, h.Vigentes, h.Gastos, h.Reembolsos, h.Retiradas, h.Depositos, h.SaldoInicial, h.Ingresos).Pendientes;
        var pendienteHogar = pendientes.Where(p => !p.DesdeAhorro).Sum(p => p.Importe);
        var pendienteAhorro = pendientes.Where(p => p.DesdeAhorro).Sum(p => p.Importe);
        // Los reembolsos salen del efectivo de Hogar aunque el adelanto fuera del ahorro: el efectivo ya incluye lo pendiente de los dos
        // fondos. El fondo solo decide de qué pendiente se descuenta, así que la columna de Ahorro no cambia el dinero.
        var hogar = new FilaResumenMensualCuenta(
            anterior.Efectivo, aportesGastos + fijado + adelantadosDelAhorro, ingresado, directos, reembolsado,
            anterior.Efectivo + aportesGastos + fijado + adelantadosDelAhorro + ingresado - directos - reembolsado, pendienteHogar);
        var ahorro = new FilaResumenMensualCuenta(
            anterior.AhorroDisponible, aportesAhorro, depositado, delAhorro + retirado, 0m,
            anterior.AhorroDisponible + aportesAhorro + depositado - delAhorro - retirado, pendienteAhorro);
        var total = new FilaResumenMensualCuenta(
            hogar.Inicial + ahorro.Inicial, hogar.Ingresos + ahorro.Ingresos, hogar.IngresosSueltos + ahorro.IngresosSueltos,
            hogar.Gastos + ahorro.Gastos, hogar.Reembolsos + ahorro.Reembolsos, hogar.Disponible + ahorro.Disponible,
            hogar.Pendiente + ahorro.Pendiente);
        return new ResumenMensualCuenta(hogar, ahorro, total);
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
            aportado.TryAdd(m, 0m);
            ahorrado.TryAdd(m, 0m);
            for (var d = primera; d <= mes; d = d.AddMonths(1))
            {
                if (Cobro(m, d, aportaciones) is not { } cobro) continue;
                aportado[m] += cobro.Importe - cobro.Ahorro;
                ahorrado[m] += cobro.Ahorro;
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
            estado.Pendientes.Where(p => p.MiembroId == id).Sum(p => p.Importe))).ToList();
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
