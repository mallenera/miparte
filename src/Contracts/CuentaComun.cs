namespace MiParte.Contracts;

/// <summary>Aportación fija mensual de un adulto a la cuenta común.</summary>
/// <param name="Id">Identificador de la aportación.</param>
/// <param name="MiembroId">Adulto que aporta.</param>
/// <param name="Desde">Mes desde el que vale el importe, como fecha del día 1.</param>
/// <param name="Importe">Importe mensual; 0 deja de aportar.</param>
public record AportacionCuentaDto(Guid Id, Guid MiembroId, DateOnly Desde, decimal Importe);

/// <summary>PUT /api/cuenta-comun/aportaciones: fija lo que aporta un adulto desde un mes (sustituye la de ese mismo mes).</summary>
/// <param name="MiembroId">Adulto activo que aporta.</param>
/// <param name="Desde">Mes desde el que vale, como fecha del día 1.</param>
/// <param name="Importe">Importe mensual (0 o más, máx. 2 decimales).</param>
public record FijarAportacionRequest(Guid MiembroId, DateOnly Desde, decimal Importe);

/// <summary>Reembolso de la cuenta común a quien adelantó un gasto.</summary>
/// <param name="Id">Identificador del reembolso.</param>
/// <param name="MiembroId">Miembro reembolsado.</param>
/// <param name="Fecha">Fecha del reembolso.</param>
/// <param name="Importe">Importe reembolsado.</param>
/// <param name="Concepto">Descripción opcional.</param>
public record ReembolsoCuentaDto(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);

/// <summary>POST /api/cuenta-comun/reembolsos. Fecha por defecto: hoy (UTC).</summary>
/// <param name="MiembroId">Miembro que recibe el reembolso.</param>
/// <param name="Importe">Importe reembolsado.</param>
/// <param name="Fecha">Fecha del reembolso; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Descripción opcional.</param>
public record CrearReembolsoRequest(Guid MiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);

/// <summary>Lo que la cuenta común debe aún a un miembro por gastos que adelantó.</summary>
/// <param name="MiembroId">Miembro acreedor.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Importe">Importe pendiente de reembolsar.</param>
public record PendienteCuentaDto(Guid MiembroId, string Nombre, decimal Importe);

/// <summary>GET /api/cuenta-comun?mes=YYYY-MM: estado de la cuenta común al final del mes.</summary>
/// <param name="Mes">Mes consultado, en formato YYYY-MM.</param>
/// <param name="AportadoMes">Suma de las aportaciones del mes.</param>
/// <param name="Aportado">Aportaciones acumuladas hasta el mes.</param>
/// <param name="Gastado">Gastos cargados a la cuenta hasta el mes.</param>
/// <param name="Saldo">Aportado menos gastado; negativo si la cuenta no cubre los gastos.</param>
/// <param name="Pendientes">Reembolsos pendientes por miembro.</param>
/// <param name="Efectivo">Dinero que hay realmente en la cuenta: saldo más lo pendiente de reembolsar.</param>
/// <param name="Aportaciones">Aportaciones configuradas, ordenadas por miembro y mes.</param>
/// <param name="Reembolsos">Reembolsos registrados en el mes.</param>
public record CuentaComunResponse(
    string Mes, decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteCuentaDto> Pendientes, decimal Efectivo,
    IReadOnlyList<AportacionCuentaDto> Aportaciones, IReadOnlyList<ReembolsoCuentaDto> Reembolsos);
