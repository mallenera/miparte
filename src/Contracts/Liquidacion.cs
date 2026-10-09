namespace MiParte.Contracts;

/// <summary>Importe asociado a un miembro (p. ej. su parte del total de una categoría).</summary>
/// <param name="MiembroId">Miembro al que corresponde el importe.</param>
/// <param name="Importe">Importe del miembro.</param>
public record ImporteMiembroDto(Guid MiembroId, decimal Importe);

/// <summary>Resumen mensual de un miembro: lo que pagó frente a lo que le correspondía asumir.</summary>
/// <param name="MiembroId">Identificador del miembro.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Pagado">Total de gastos del mes pagados por el miembro, incluidos los que asume la cuenta común.</param>
/// <param name="Asumido">Total de gastos del mes que le corresponden según los repartos.</param>
/// <param name="DebeCuentaComun">Lo que la cuenta común le debe por gastos que adelantó (acumulado hasta el fin del mes, descontados los reembolsos).</param>
/// <param name="Personal">Total de sus gastos personales del mes, que no entran en <paramref name="Pagado"/>, <paramref name="Asumido"/> ni en la liquidación.</param>
public record ResumenMiembroDto(Guid MiembroId, string Nombre, decimal Pagado, decimal Asumido, decimal DebeCuentaComun = 0m, decimal Personal = 0m);

/// <summary>Resumen mensual de una categoría.</summary>
/// <param name="CategoriaId">Identificador de la categoría.</param>
/// <param name="Nombre">Nombre de la categoría.</param>
/// <param name="Total">Total de gastos de la categoría en el mes.</param>
/// <param name="PorMiembro">Desglose del total por miembro.</param>
public record ResumenCategoriaDto(
    Guid CategoriaId, string Nombre, decimal Total, IReadOnlyList<ImporteMiembroDto> PorMiembro);

/// <summary>Saldos de la cuenta común dentro del resumen mensual (solo con la cuenta común activada).</summary>
/// <param name="AportadoMes">Aportaciones del mes (incluido el ahorro).</param>
/// <param name="GastadoMes">Gastos cargados a la cuenta en el mes, también los pagados desde el ahorro.</param>
/// <param name="Saldo">Saldo de gastos al final del mes (aportado sin ahorro menos gastado), acumulado.</param>
/// <param name="Efectivo">Dinero de gastos que hay en la cuenta al final del mes (saldo más lo pendiente de reembolsar).</param>
/// <param name="Pendiente">Total que la cuenta debe a quienes adelantaron gastos.</param>
/// <param name="AhorroMes">Ahorro del mes (parte de ahorro de las aportaciones y depósitos).</param>
/// <param name="AhorroDisponible">Ahorro disponible al final del mes.</param>
public record ResumenCuentaComunDto(
    decimal AportadoMes, decimal GastadoMes, decimal Saldo, decimal Efectivo, decimal Pendiente,
    decimal AhorroMes, decimal AhorroDisponible);

/// <summary>GET /api/resumen?mes=YYYY-MM.</summary>
/// <param name="Mes">Mes consultado, en formato YYYY-MM.</param>
/// <param name="GastosTotales">Suma de los gastos del mes, sin contar los personales.</param>
/// <param name="Miembros">Pagado y asumido por cada miembro.</param>
/// <param name="Categorias">Total y desglose por categoría (sin gastos personales).</param>
/// <param name="CuentaComun">Saldos de la cuenta común al final del mes; null si el hogar no la tiene activada.</param>
/// <param name="GastosPersonales">Suma de los gastos personales del mes de todos los miembros, fuera de los totales anteriores.</param>
public record ResumenMensualResponse(
    string Mes, decimal GastosTotales,
    IReadOnlyList<ResumenMiembroDto> Miembros, IReadOnlyList<ResumenCategoriaDto> Categorias,
    ResumenCuentaComunDto? CuentaComun = null, decimal GastosPersonales = 0m);

/// <summary>Saldo positivo = le deben; negativo = debe. Ya descuenta los pagos registrados.</summary>
/// <param name="MiembroId">Identificador del miembro.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Saldo">Saldo del miembro en el mes (positivo: le deben; negativo: debe).</param>
public record SaldoMiembroDto(Guid MiembroId, string Nombre, decimal Saldo);

/// <summary>Transferencia sugerida para saldar la liquidación.</summary>
/// <param name="De">Miembro que debe pagar.</param>
/// <param name="A">Miembro que debe recibir el pago.</param>
/// <param name="Importe">Importe a transferir.</param>
public record TransferenciaDto(Guid De, Guid A, decimal Importe);

/// <summary>Pago registrado entre dos miembros para saldar un mes.</summary>
/// <param name="Id">Identificador del pago.</param>
/// <param name="Mes">Mes que salda, como fecha del día 1.</param>
/// <param name="DeMiembroId">Miembro que paga.</param>
/// <param name="AMiembroId">Miembro que recibe.</param>
/// <param name="Importe">Importe pagado.</param>
/// <param name="Fecha">Fecha en que se realizó el pago.</param>
/// <param name="Concepto">Descripción opcional del pago.</param>
public record PagoLiquidacionDto(
    Guid Id, DateOnly Mes, Guid DeMiembroId, Guid AMiembroId, decimal Importe, DateOnly Fecha, string? Concepto);

/// <summary>GET /api/liquidacion?mes=YYYY-MM: saldos, transferencias sugeridas y pagos del mes.</summary>
/// <param name="Mes">Mes consultado, en formato YYYY-MM.</param>
/// <param name="Saldos">Saldo de cada miembro, ya descontados los pagos registrados.</param>
/// <param name="Transferencias">Transferencias sugeridas para saldar los saldos.</param>
/// <param name="Pagos">Pagos de liquidación ya registrados en el mes.</param>
public record LiquidacionResponse(
    string Mes, IReadOnlyList<SaldoMiembroDto> Saldos, IReadOnlyList<TransferenciaDto> Transferencias,
    IReadOnlyList<PagoLiquidacionDto> Pagos);

/// <summary>Mes debe ser el día 1. Fecha por defecto: hoy (UTC).</summary>
/// <param name="Mes">Mes que se salda, como fecha del día 1.</param>
/// <param name="DeMiembroId">Miembro que paga.</param>
/// <param name="AMiembroId">Miembro que recibe.</param>
/// <param name="Importe">Importe pagado.</param>
/// <param name="Fecha">Fecha del pago; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Descripción opcional del pago.</param>
public record CrearPagoLiquidacionRequest(
    DateOnly Mes, Guid DeMiembroId, Guid AMiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);
