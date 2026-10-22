namespace MiParte.Core.Domain;

/// <summary>Clase de movimiento que se ve en el banco de la cuenta común. El orden es el del extracto en un mismo día: primero lo que entra.</summary>
public enum TipoMovimientoBanco
{
    /// <summary>Saldo inicial fijado para la cuenta (entrada).</summary>
    SaldoInicial,

    /// <summary>Aportación de un adulto, con su parte de ahorro (entrada).</summary>
    Aportacion,

    /// <summary>Ingreso suelto a la cuenta, fuera de aportaciones y ahorro (entrada).</summary>
    Ingreso,

    /// <summary>Depósito de ahorro aparte de las aportaciones (entrada).</summary>
    DepositoAhorro,

    /// <summary>Gasto que paga la cuenta directamente (salida).</summary>
    Gasto,

    /// <summary>Gasto que paga la cuenta con el ahorro (salida).</summary>
    GastoAhorro,

    /// <summary>Reembolso a quien adelantó un gasto de la cuenta (salida).</summary>
    Reembolso,

    /// <summary>Retirada de ahorro (salida).</summary>
    RetiradaAhorro,
}

/// <summary>Gasto pagado directamente por la cuenta común (sin nadie que lo adelante), con lo que hace falta para reconocerlo en el banco.</summary>
public sealed record GastoPagadoPorCuenta(Guid Id, DateOnly Fecha, decimal Importe, Guid CategoriaId, string? Concepto, bool DesdeAhorro);

/// <summary>
/// Todo lo que mueve dinero en el banco de la cuenta común hasta una fecha. Los gastos que adelanta una persona no están: el banco
/// no los ve hasta que se le reembolsan.
/// </summary>
public sealed record DatosMovimientosBanco(
    IReadOnlyList<AportacionVigente> Aportaciones, SaldoInicialCuenta? SaldoInicial, IReadOnlyList<GastoPagadoPorCuenta> Gastos,
    IReadOnlyList<MovimientoCuenta> Reembolsos, IReadOnlyList<MovimientoCuenta> Ingresos,
    IReadOnlyList<MovimientoCuenta> Depositos, IReadOnlyList<MovimientoCuenta> Retiradas);

/// <summary>
/// Una línea del extracto bancario de la cuenta común. <paramref name="Importe"/> lleva signo (entradas positivas, salidas negativas) y
/// <paramref name="Saldo"/> es el saldo de la cuenta (gastos y ahorro juntos) tras el movimiento. <paramref name="Id"/> es null en las
/// líneas que no son un registro propio (aportaciones y saldo inicial); <paramref name="Ahorro"/> es la parte de una aportación que va a ahorro.
/// </summary>
public sealed record MovimientoBanco(
    TipoMovimientoBanco Tipo, Guid? Id, DateOnly Fecha, decimal Importe, Guid? MiembroId, Guid? CategoriaId, string? Concepto,
    decimal Ahorro, decimal Saldo);

/// <summary>Extracto bancario de la cuenta común entre dos fechas (ambas incluidas): saldo anterior, movimientos, totales y saldo final.</summary>
public sealed record ExtractoBanco(
    DateOnly Desde, DateOnly Hasta, decimal SaldoAnterior, decimal Entradas, decimal Salidas, decimal SaldoFinal,
    IReadOnlyList<MovimientoBanco> Movimientos);

/// <summary>
/// Extracto de la cuenta común línea a línea, para cuadrarlo con el del banco. Supone que el ahorro está en la misma cuenta que el dinero
/// para gastos, así que hay un único saldo. Las aportaciones no tienen fecha real: cada cobro se fecha el día 1 de su mes. Los ingresos
/// sueltos sí cuentan aquí (entran en el banco) aunque no sumen al saldo de gastos de la cuenta.
/// </summary>
public static class MovimientosBanco
{
    /// <summary>Texto con que viaja cada tipo en la API.</summary>
    public static string ATexto(TipoMovimientoBanco tipo) => tipo switch
    {
        TipoMovimientoBanco.SaldoInicial => "saldo_inicial",
        TipoMovimientoBanco.Aportacion => "aportacion",
        TipoMovimientoBanco.Ingreso => "ingreso",
        TipoMovimientoBanco.DepositoAhorro => "deposito_ahorro",
        TipoMovimientoBanco.Gasto => "gasto",
        TipoMovimientoBanco.GastoAhorro => "gasto_ahorro",
        TipoMovimientoBanco.Reembolso => "reembolso",
        TipoMovimientoBanco.RetiradaAhorro => "retirada_ahorro",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    /// <summary>
    /// Extracto entre <paramref name="desde"/> y <paramref name="hasta"/> (incluidos), en orden cronológico. El saldo de cada línea se
    /// acumula desde el primer movimiento de la historia, así que el saldo anterior es el del banco al cerrar el día previo a <paramref name="desde"/>.
    /// </summary>
    /// <param name="desde">Primer día incluido.</param>
    /// <param name="hasta">Último día incluido; no puede ser anterior a <paramref name="desde"/>.</param>
    /// <param name="datos">Movimientos del hogar; los posteriores a <paramref name="hasta"/> se ignoran.</param>
    public static ExtractoBanco Calcular(DateOnly desde, DateOnly hasta, DatosMovimientosBanco datos)
    {
        if (hasta < desde) throw new ArgumentException("«hasta» no puede ser anterior a «desde».", nameof(hasta));

        var todos = Lineas(hasta, datos)
            .Where(l => l.Fecha <= hasta)
            .OrderBy(l => l.Fecha).ThenBy(l => l.Tipo).ThenBy(l => l.Id ?? l.MiembroId ?? Guid.Empty)
            .ToList();

        var saldo = 0m;
        var anterior = 0m;
        var lineas = new List<MovimientoBanco>();
        foreach (var l in todos)
        {
            saldo += l.Importe;
            if (l.Fecha < desde) anterior = saldo;
            else lineas.Add(l with { Saldo = saldo });
        }

        var entradas = lineas.Where(l => l.Importe > 0).Sum(l => l.Importe);
        var salidas = -lineas.Where(l => l.Importe < 0).Sum(l => l.Importe);
        return new ExtractoBanco(desde, hasta, anterior, entradas, salidas, anterior + entradas - salidas, lineas);
    }

    /// <summary>Todas las líneas hasta <paramref name="hasta"/>, sin ordenar ni saldo.</summary>
    private static IEnumerable<MovimientoBanco> Lineas(DateOnly hasta, DatosMovimientosBanco d)
    {
        if (d.SaldoInicial is { Importe: > 0 } si)
            yield return new(TipoMovimientoBanco.SaldoInicial, null, si.Fecha, si.Importe, null, null, null, 0m, 0m);

        // Un cobro por persona y mes de cobro, desde su primera aportación; un mes sin cobro (o con importe 0) no genera línea.
        foreach (var m in d.Aportaciones.Select(a => a.MiembroId).Distinct())
        {
            var primera = d.Aportaciones.Where(a => a.MiembroId == m).Min(a => a.Desde);
            for (var mes = CuentaComun.InicioMes(primera); mes <= hasta; mes = mes.AddMonths(1))
            {
                var importe = CuentaComun.AportacionDelMes(m, mes, d.Aportaciones);
                if (importe > 0)
                    yield return new(TipoMovimientoBanco.Aportacion, null, mes, importe, m, null, null,
                        CuentaComun.AhorroDelMes(m, mes, d.Aportaciones), 0m);
            }
        }

        foreach (var x in d.Ingresos) yield return Suelto(TipoMovimientoBanco.Ingreso, x, 1);
        foreach (var x in d.Depositos) yield return Suelto(TipoMovimientoBanco.DepositoAhorro, x, 1);
        foreach (var g in d.Gastos)
            yield return new(g.DesdeAhorro ? TipoMovimientoBanco.GastoAhorro : TipoMovimientoBanco.Gasto, g.Id, g.Fecha, -g.Importe,
                null, g.CategoriaId, g.Concepto, 0m, 0m);
        foreach (var x in d.Reembolsos) yield return Suelto(TipoMovimientoBanco.Reembolso, x, -1);
        foreach (var x in d.Retiradas) yield return Suelto(TipoMovimientoBanco.RetiradaAhorro, x, -1);
    }

    /// <summary>Línea de un movimiento suelto con el signo de su sentido.</summary>
    private static MovimientoBanco Suelto(TipoMovimientoBanco tipo, MovimientoCuenta x, int signo)
        => new(tipo, x.Id, x.Fecha, signo * x.Importe, x.MiembroId, null, x.Concepto, 0m, 0m);
}
