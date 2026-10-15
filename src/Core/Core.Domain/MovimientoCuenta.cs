namespace MiParte.Core.Domain;

/// <summary>Movimiento suelto de la cuenta común (reembolso, retirada o depósito de ahorro) de un miembro, con su nota opcional.</summary>
public sealed record MovimientoCuenta(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);
