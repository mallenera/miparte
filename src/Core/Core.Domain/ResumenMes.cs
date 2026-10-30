using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Histórico de la cuenta común hasta el fin de un mes, ya agrupado, en la forma que usa <see cref="CuentaComun.Calcular"/>.</summary>
public sealed record HistoricoCuenta(
    IReadOnlyList<AportacionVigente> Vigentes, IReadOnlyList<GastoDeCuenta> Gastos, IReadOnlyList<ReembolsoDeCuenta> Reembolsos,
    IReadOnlyList<RetiradaDeAhorro> Retiradas, IReadOnlyList<DepositoDeAhorro> Depositos, bool CuentaActiva, decimal GastadoMes,
    IReadOnlyList<GastoDeCuenta>? GastosDelMes = null, SaldoInicialCuenta? SaldoInicial = null,
    FrecuenciaAportacion Frecuencia = FrecuenciaAportacion.Mensual, IReadOnlyList<IngresoDeCuenta>? Ingresos = null);

/// <summary>Importe asociado a un miembro.</summary>
public sealed record ImporteMiembro(Guid MiembroId, decimal Importe);

/// <summary>Lo pagado, asumido, pendiente de reembolso y personal de un miembro en el mes.</summary>
public sealed record ResumenMiembro(Guid MiembroId, string Nombre, decimal Pagado, decimal Asumido, decimal PendienteCuenta, decimal Personal);

/// <summary>Gasto de una categoría en el mes y lo que asume cada miembro.</summary>
public sealed record ResumenCategoria(Guid CategoriaId, string Nombre, decimal Total, IReadOnlyList<ImporteMiembro> PorMiembro);

/// <summary>
/// Saldos de la cuenta común al final del mes. <paramref name="GastadoMes"/> es lo gastado de la cuenta en el mes sin lo pagado
/// desde el ahorro, que va aparte en <paramref name="GastadoAhorroMes"/>.
/// </summary>
public sealed record ResumenCuenta(
    decimal AportadoMes, decimal GastadoMes, decimal Saldo, decimal Efectivo, decimal PendienteReembolso,
    decimal AhorroMes, decimal AhorroDisponible, decimal GastadoAhorroMes = 0m);

/// <summary>Resumen mensual del hogar; <see cref="Cuenta"/> es null si el hogar no tiene activada la cuenta común.</summary>
public sealed record ResumenDelMes(
    DateOnly Mes, decimal GastosTotales, IReadOnlyList<ResumenMiembro> PorMiembro, IReadOnlyList<ResumenCategoria> PorCategoria,
    ResumenCuenta? Cuenta, decimal Personales);

/// <summary>Cálculo del resumen mensual a partir de lo ya leído; no depende de persistencia.</summary>
public static class ResumenMes
{
    /// <summary>
    /// Arma el resumen del mes que empieza en <paramref name="inicio"/> con los gastos del mes (con reparto), los miembros,
    /// los nombres de categoría y el histórico de la cuenta común.
    /// </summary>
    public static ResumenDelMes Construir(
        DateOnly inicio, IReadOnlyList<Gasto> delMes, IReadOnlyList<Miembro> miembros,
        IReadOnlyDictionary<Guid, string> categorias, HistoricoCuenta historico)
    {
        var gastos = delMes.Where(g => !g.EsPersonal).ToList(); // los personales van aparte: no entran en pagado, asumido ni totales
        var personales = delMes.Where(g => g.EsPersonal).ToList();
        var gastosTotales = gastos.Sum(g => g.Importe);

        // Lo que adelantó un miembro para la cuenta común cuenta como pagado, y la cuenta le debe lo aún no reembolsado.
        var cuenta = CuentaComun.Calcular(inicio, [], historico.Gastos, historico.Reembolsos);
        // Los acreedores con pendiente de meses anteriores aparecen aunque no participen en los gastos de este mes (p. ej. un miembro ya inactivo).
        var implicados = miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).Select(m => m.Id)
            .Concat(gastos.Where(g => g.PagadoPor is not null).Select(g => g.PagadoPor!.Value))
            .Concat(gastos.SelectMany(g => g.Repartos).Select(r => r.MiembroId))
            .Concat(cuenta.Pendientes.Where(p => p.Importe > 0m).Select(p => p.MiembroId))
            .Concat(personales.Select(g => g.PagadoPor!.Value))
            .ToHashSet();
        var porMiembro = miembros.Where(m => implicados.Contains(m.Id)).OrderBy(m => m.Nombre).ThenBy(m => m.Id)
            .Select(m => new ResumenMiembro(
                m.Id, m.Nombre,
                gastos.Where(g => g.PagadoPor == m.Id).Sum(g => g.Importe),
                gastos.SelectMany(g => g.Repartos).Where(r => r.MiembroId == m.Id).Sum(r => r.ImporteAsumido),
                Math.Max(0m, CuentaComun.PendienteNeto(cuenta.Pendientes, historico.Ingresos, m.Id, inicio)),
                personales.Where(g => g.PagadoPor == m.Id).Sum(g => g.Importe)))
            .ToList();

        var porCategoria = gastos.GroupBy(g => g.CategoriaId)
            .Select(c => new ResumenCategoria(
                c.Key, categorias.GetValueOrDefault(c.Key, ""), c.Sum(g => g.Importe),
                c.SelectMany(g => g.Repartos).GroupBy(r => r.MiembroId)
                    .Select(r => new ImporteMiembro(r.Key, r.Sum(x => x.ImporteAsumido)))
                    .OrderBy(x => x.MiembroId).ToList()))
            .OrderBy(c => c.Nombre).ThenBy(c => c.CategoriaId).ToList();

        // Saldos de la cuenta al final del mes, solo si el hogar la ha activado.
        ResumenCuenta? resumenCuenta = null;
        if (historico.CuentaActiva)
        {
            var estado = CuentaComun.Calcular(
                inicio, historico.Vigentes, historico.Gastos, historico.Reembolsos, historico.Retiradas, historico.Depositos,
                historico.SaldoInicial, historico.Ingresos);
            // Lo gastado de la cuenta y lo gastado del ahorro se informan por separado.
            var delAhorro = (historico.GastosDelMes ?? []).Where(g => g.DesdeAhorro).Sum(g => g.Importe);
            resumenCuenta = new ResumenCuenta(
                estado.AportadoMes, historico.GastadoMes - delAhorro, estado.Saldo,
                estado.Efectivo,
                estado.Pendientes.Select(p => CuentaComun.PendienteNeto(estado.Pendientes, historico.Ingresos, p.MiembroId, inicio))
                    .Where(n => n > 0m).Sum(),
                estado.AhorroMes, estado.AhorroDisponible, delAhorro);
        }

        return new ResumenDelMes(inicio, gastosTotales, porMiembro, porCategoria, resumenCuenta, personales.Sum(g => g.Importe));
    }
}
