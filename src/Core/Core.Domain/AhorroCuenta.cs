namespace MiParte.Core.Domain;

/// <summary>
/// Lo que hace falta saber para calcular el ahorro disponible: las aportaciones y los depósitos, y lo ya retirado o gastado
/// desde el ahorro (sin contar, si se está editando, el gasto que se va a sustituir). <c>Gastado</c> incluye el efecto neto de las
/// deudas con el ahorro: lo adelantado por la cuenta suma y lo devuelto o compensado resta (<see cref="AhorroCuenta.UsosPorDeudas"/>).
/// </summary>
public sealed record DatosAhorro(
    IReadOnlyList<AportacionVigente> Aportaciones, IReadOnlyList<DepositoDeAhorro> Depositos, decimal Retirado, decimal Gastado);

/// <summary>Reglas del ahorro de la cuenta común que no dependen de persistencia.</summary>
public static class AhorroCuenta
{
    /// <summary>
    /// Lo que las deudas con el ahorro hacen al ahorro, como usos con signo (positivo sale del ahorro, negativo entra): un adelanto lo saca;
    /// una devolución o una compensación lo reponen. Solo cuentan los movimientos del fondo del ahorro.
    /// </summary>
    public static IReadOnlyList<UsoDeAhorro> UsosPorDeudas(IEnumerable<MovimientoDeuda> deudas)
        => deudas.Where(d => d.DesdeAhorro)
            .Select(d => new UsoDeAhorro(d.Fecha, d.Tipo == TipoMovimientoDeuda.Adelanto ? d.Importe : -d.Importe)).ToList();

    /// <summary>
    /// Ahorro del que se puede disponer con fecha <paramref name="fecha"/>: lo ahorrado (aportaciones y depósitos) hasta ese mes
    /// menos todo lo ya retirado o gastado desde el ahorro. Puede salir negativo si ya se usó más de lo ahorrado.
    /// </summary>
    public static decimal Disponible(DateOnly fecha, DatosAhorro datos)
    {
        var ahorrado = CuentaComun.Calcular(CuentaComun.InicioMes(fecha), datos.Aportaciones, [], [], null, datos.Depositos).AhorroAcumulado;
        return ahorrado - datos.Retirado - datos.Gastado;
    }
}

/// <summary>Dinero que ya salió del ahorro en una fecha: una retirada o un gasto pagado desde el ahorro.</summary>
public sealed record UsoDeAhorro(DateOnly Fecha, decimal Importe);

/// <summary>Reglas del ahorro que comparan lo ahorrado con lo ya usado, mes a mes.</summary>
public static class HolguraAhorro
{
    /// <summary>
    /// Lo que sobraría de ahorro, en el peor de los meses en que se retiró o se gastó desde el ahorro, si las aportaciones y los
    /// depósitos fueran los dados (null si no hay usos). En cada uno de esos meses se compara lo ahorrado hasta él con todo lo
    /// usado hasta él; un valor negativo es la cantidad que faltaría: el cambio dejaría sin respaldo dinero ya usado.
    /// </summary>
    public static decimal? Calcular(
        IReadOnlyList<AportacionVigente> aportaciones, IReadOnlyList<DepositoDeAhorro> depositos, IReadOnlyList<UsoDeAhorro> usos)
    {
        if (usos.Count == 0) return null;

        // Mirar solo el último mes no basta: una rebaja en enero podría dejar sin respaldo una retirada de enero aunque junio siga cubierto.
        decimal? peor = null;
        foreach (var mes in usos.Select(u => CuentaComun.InicioMes(u.Fecha)).Distinct())
        {
            var fin = mes.AddMonths(1);
            var ahorrado = CuentaComun.Calcular(mes, aportaciones, [], [], null, depositos).AhorroAcumulado;
            var holgura = ahorrado - usos.Where(u => u.Fecha < fin).Sum(u => u.Importe);
            peor = peor is null ? holgura : Math.Min(peor.Value, holgura);
        }

        return peor;
    }
}
