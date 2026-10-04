namespace MiParte.Contracts;

/// <summary>Alta/edición de un ingreso de un adulto activo del hogar. Importe &gt; 0, máximo 2 decimales.</summary>
public record IngresoRequest(Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);

public record IngresoResponse(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);
