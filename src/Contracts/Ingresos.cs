namespace MiParte.Contracts;

/// <summary>Alta/edición de un ingreso de un adulto activo del hogar. Importe &gt; 0, máximo 2 decimales.</summary>
/// <param name="MiembroId">Adulto activo que percibe el ingreso.</param>
/// <param name="Fecha">Fecha del ingreso.</param>
/// <param name="Importe">Importe del ingreso (mayor que 0, máximo 2 decimales).</param>
/// <param name="Concepto">Descripción opcional del ingreso (máximo 200 caracteres).</param>
public record IngresoRequest(Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);

/// <summary>Ingreso guardado.</summary>
/// <param name="Id">Identificador del ingreso.</param>
/// <param name="MiembroId">Miembro que percibe el ingreso.</param>
/// <param name="Fecha">Fecha del ingreso.</param>
/// <param name="Importe">Importe del ingreso.</param>
/// <param name="Concepto">Descripción opcional del ingreso.</param>
public record IngresoResponse(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);
