namespace MiParte.Core.Domain;

/// <summary>
/// Lo que hace falta saber para calcular el ahorro disponible: las aportaciones y los depósitos, y lo ya retirado o gastado
/// desde el ahorro (sin contar, si se está editando, el gasto que se va a sustituir).
/// </summary>
public sealed record DatosAhorro(
    IReadOnlyList<AportacionVigente> Aportaciones, IReadOnlyList<DepositoDeAhorro> Depositos, decimal Retirado, decimal Gastado);

/// <summary>Reglas del ahorro de la cuenta común que no dependen de persistencia.</summary>
public static class AhorroCuenta
{
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
