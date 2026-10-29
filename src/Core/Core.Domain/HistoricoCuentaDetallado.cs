namespace MiParte.Core.Domain;

/// <summary>Aportación de un miembro a la cuenta común tal como está registrada, con su identificador.</summary>
public sealed record AportacionRegistrada(Guid Id, Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro);

/// <summary>
/// Histórico de la cuenta común hasta el fin de un mes con el detalle que necesita la pantalla de la cuenta: las aportaciones, los
/// movimientos del mes uno a uno (también los de deuda con la cuenta, con su identificador) y los nombres de los miembros.
/// </summary>
public sealed record HistoricoCuentaDetallado(
    HistoricoCuenta Historico, bool AhorroActivo, IReadOnlyList<AportacionRegistrada> Aportaciones,
    IReadOnlyList<MovimientoCuenta> ReembolsosDelMes, IReadOnlyList<MovimientoCuenta> RetiradasDelMes,
    IReadOnlyList<MovimientoCuenta> DepositosDelMes, IReadOnlyDictionary<Guid, string> Nombres,
    IReadOnlyList<MovimientoDeuda>? DeudasDelMes = null);
