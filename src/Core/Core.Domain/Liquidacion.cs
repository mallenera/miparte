namespace MiParte.Core.Domain;

/// <summary>Gasto ya repartido: quién lo pagó y qué asume cada miembro.</summary>
public sealed record GastoCalculado(Guid PagadoPor, decimal Importe, IReadOnlyList<ParteAsumida> Partes);

/// <summary>Pago de liquidación ya registrado: <paramref name="De"/> pagó a <paramref name="A"/>.</summary>
public sealed record PagoLiquidacion(Guid De, Guid A, decimal Importe);

/// <summary>Saldo de un miembro: positivo = le deben, negativo = debe.</summary>
public sealed record SaldoMiembro(Guid MiembroId, decimal Importe);

public sealed record Transferencia(Guid De, Guid A, decimal Importe);

public static class Liquidacion
{
    /// <summary>
    /// Saldo = pagado - asumido, ajustado por pagos de liquidación (quien paga sube su saldo,
    /// quien recibe lo baja). Devuelve un saldo por cada miembro dado, en el mismo orden.
    /// </summary>
    public static IReadOnlyList<SaldoMiembro> CalcularSaldos(
        IReadOnlyList<Guid> miembros,
        IEnumerable<GastoCalculado> gastos,
        IEnumerable<PagoLiquidacion>? pagos = null)
    {
        var saldos = miembros.ToDictionary(id => id, _ => 0m);

        decimal Saldo(Guid id) => saldos.TryGetValue(id, out var s)
            ? s : throw new ArgumentException($"Miembro desconocido: {id}.", nameof(miembros));

        foreach (var g in gastos)
        {
            saldos[g.PagadoPor] = Saldo(g.PagadoPor) + g.Importe;
            foreach (var p in g.Partes)
                saldos[p.MiembroId] = Saldo(p.MiembroId) - p.Importe;
        }

        foreach (var p in pagos ?? [])
        {
            if (p.Importe <= 0) throw new ArgumentOutOfRangeException(nameof(pagos), "El importe del pago debe ser positivo.");
            saldos[p.De] = Saldo(p.De) + p.Importe;
            saldos[p.A] = Saldo(p.A) - p.Importe;
        }

        return miembros.Select(id => new SaldoMiembro(id, saldos[id])).ToList();
    }

    /// <summary>
    /// Liquida con pocas transferencias (greedy): el mayor deudor paga al mayor acreedor.
    /// Determinista: los empates se resuelven por Id. Con un solo miembro no hay transferencias.
    /// </summary>
    public static IReadOnlyList<Transferencia> Liquidar(IReadOnlyList<SaldoMiembro> saldos)
    {
        if (saldos.Sum(s => s.Importe) != 0)
            throw new ArgumentException("Los saldos deben sumar cero.", nameof(saldos));

        var deudores = saldos.Where(s => s.Importe < 0)
            .OrderBy(s => s.Importe).ThenBy(s => s.MiembroId)
            .Select(s => (s.MiembroId, Pendiente: -s.Importe)).ToList();
        var acreedores = saldos.Where(s => s.Importe > 0)
            .OrderByDescending(s => s.Importe).ThenBy(s => s.MiembroId)
            .Select(s => (s.MiembroId, Pendiente: s.Importe)).ToList();

        var resultado = new List<Transferencia>();
        int d = 0, a = 0;
        while (d < deudores.Count && a < acreedores.Count)
        {
            var importe = Math.Min(deudores[d].Pendiente, acreedores[a].Pendiente);
            resultado.Add(new Transferencia(deudores[d].MiembroId, acreedores[a].MiembroId, importe));
            deudores[d] = (deudores[d].MiembroId, deudores[d].Pendiente - importe);
            acreedores[a] = (acreedores[a].MiembroId, acreedores[a].Pendiente - importe);
            if (deudores[d].Pendiente == 0) d++;
            if (acreedores[a].Pendiente == 0) a++;
        }
        return resultado;
    }
}
