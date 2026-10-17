using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>
/// Liquidación de un mes ya calculada: saldos, transferencias propuestas, pagos registrados, nombres de los miembros
/// y si el mes tiene gastos que repartir entre personas.
/// </summary>
public sealed record LiquidacionCalculada(
    IReadOnlyList<SaldoMiembro> Saldos, IReadOnlyList<Transferencia> Transferencias,
    IReadOnlyList<PagoLiquidacionRegistro> Pagos, IReadOnlyDictionary<Guid, string> Nombres, bool HayGastos)
{
    /// <summary>
    /// Lo máximo que <paramref name="de"/> puede pagar a <paramref name="a"/> sin pasarse de su deuda ni de lo que se le debe a este.
    /// </summary>
    public decimal PendienteEntre(Guid de, Guid a)
    {
        var deuda = -Saldos.FirstOrDefault(s => s.MiembroId == de)?.Importe ?? 0m;
        var credito = Saldos.FirstOrDefault(s => s.MiembroId == a)?.Importe ?? 0m;
        return Math.Max(0m, Math.Min(deuda, credito));
    }
}

/// <summary>Cálculo de la liquidación de un mes a partir de los gastos y pagos ya leídos; no depende de persistencia.</summary>
public static class LiquidacionMes
{
    /// <summary>
    /// Calcula los saldos del mes y las transferencias que los saldan. Lo que asume la cuenta común no entra en la deuda entre
    /// personas (y es lo único que puede pagar la cuenta: <c>PagadoPor</c> nulo); los gastos personales tampoco, porque los
    /// asume íntegros quien los paga.
    /// </summary>
    public static LiquidacionCalculada Calcular(
        IReadOnlyList<Gasto> delMes, IReadOnlyList<PagoLiquidacionRegistro> pagos, IReadOnlyList<Miembro> miembros)
    {
        var gastos = delMes.Where(g => !g.ACargoCuentaComun && !g.EsPersonal).ToList();

        var ids = miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).Select(m => m.Id)
            .Concat(gastos.Select(g => g.PagadoPor!.Value)).Concat(gastos.SelectMany(g => g.Repartos).Select(r => r.MiembroId))
            .Concat(pagos.SelectMany(p => new[] { p.DeMiembroId, p.AMiembroId }))
            .Distinct().OrderBy(id => id).ToList();

        var calculados = gastos.Select(g => new GastoCalculado(
            g.PagadoPor!.Value, g.Importe, g.Repartos.Select(r => new ParteAsumida(r.MiembroId, r.ImporteAsumido)).ToList()));
        var saldos = Liquidacion.CalcularSaldos(ids, calculados, pagos.Select(p => new PagoLiquidacion(p.DeMiembroId, p.AMiembroId, p.Importe)));
        var transferencias = Liquidacion.Liquidar(saldos); // un solo miembro: sin transferencias

        return new LiquidacionCalculada(saldos, transferencias, pagos, miembros.ToDictionary(m => m.Id, m => m.Nombre), gastos.Count > 0);
    }
}
