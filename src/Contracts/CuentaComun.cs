namespace MiParte.Contracts;

/// <summary>Aportación fija mensual de un adulto a la cuenta común.</summary>
/// <param name="Id">Identificador de la aportación.</param>
/// <param name="MiembroId">Adulto que aporta.</param>
/// <param name="Desde">Mes desde el que vale el importe, como fecha del día 1.</param>
/// <param name="Importe">Importe mensual; 0 deja de aportar.</param>
/// <param name="Ahorro">Parte del importe que va a ahorro; el resto queda para gastos.</param>
public record AportacionCuentaDto(Guid Id, Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro = 0m);

/// <summary>PUT /api/cuenta-comun/aportaciones: fija lo que aporta un adulto desde un mes (sustituye la de ese mismo mes).</summary>
/// <param name="MiembroId">Adulto activo que aporta.</param>
/// <param name="Desde">Mes desde el que vale, como fecha del día 1.</param>
/// <param name="Importe">Importe mensual (0 o más, máx. 2 decimales).</param>
/// <param name="Ahorro">Parte del importe que va a ahorro (entre 0 y <paramref name="Importe"/>, máx. 2 decimales); por defecto 0.</param>
public record FijarAportacionRequest(Guid MiembroId, DateOnly Desde, decimal Importe, decimal Ahorro = 0m);

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

/// <summary>Dinero que entra al ahorro de la cuenta común fuera de la aportación mensual (ahorro inicial, lotería...).</summary>
/// <param name="Id">Identificador del depósito.</param>
/// <param name="MiembroId">Miembro que lo registra.</param>
/// <param name="Fecha">Fecha del depósito.</param>
/// <param name="Importe">Importe depositado.</param>
/// <param name="Concepto">De dónde viene (opcional).</param>
public record DepositoAhorroDto(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);

/// <summary>POST /api/cuenta-comun/depositos-ahorro. Fecha por defecto: hoy (UTC).</summary>
/// <param name="MiembroId">Miembro que registra el depósito.</param>
/// <param name="Importe">Importe depositado.</param>
/// <param name="Fecha">Fecha del depósito; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Descripción opcional.</param>
public record CrearDepositoAhorroRequest(Guid MiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);

/// <summary>Dinero que el hogar saca del ahorro de la cuenta común.</summary>
/// <param name="Id">Identificador de la retirada.</param>
/// <param name="MiembroId">Miembro que la registra.</param>
/// <param name="Fecha">Fecha de la retirada.</param>
/// <param name="Importe">Importe retirado.</param>
/// <param name="Concepto">Para qué se retira (opcional).</param>
public record RetiradaAhorroDto(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);

/// <summary>POST /api/cuenta-comun/retiradas-ahorro. Fecha por defecto: hoy (UTC).</summary>
/// <param name="MiembroId">Miembro que registra la retirada.</param>
/// <param name="Importe">Importe retirado.</param>
/// <param name="Fecha">Fecha de la retirada; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Descripción opcional.</param>
public record CrearRetiradaAhorroRequest(Guid MiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);

/// <summary>Lo que la cuenta común debe aún a un miembro por gastos que adelantó.</summary>
/// <param name="MiembroId">Miembro acreedor.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Importe">Importe pendiente de reembolsar.</param>
public record PendienteCuentaDto(Guid MiembroId, string Nombre, decimal Importe);

/// <summary>GET /api/cuenta-comun?mes=YYYY-MM: estado de la cuenta común al final del mes.</summary>
/// <param name="Mes">Mes consultado, en formato YYYY-MM.</param>
/// <param name="AportadoMes">Suma de las aportaciones del mes.</param>
/// <param name="Aportado">Aportaciones acumuladas hasta el mes, incluida la parte de ahorro.</param>
/// <param name="Gastado">Gastos cargados a la cuenta hasta el mes.</param>
/// <param name="Saldo">Aportado para gastos (sin ahorro) menos gastado; negativo si la cuenta no cubre los gastos.</param>
/// <param name="Pendientes">Reembolsos pendientes por miembro.</param>
/// <param name="Efectivo">Dinero de gastos que hay realmente en la cuenta (sin ahorro): saldo más lo pendiente de reembolsar.</param>
/// <param name="Aportaciones">Aportaciones configuradas, ordenadas por miembro y mes.</param>
/// <param name="Reembolsos">Reembolsos registrados en el mes.</param>
/// <param name="AhorroMes">Ahorro del mes: la parte de ahorro de las aportaciones más los depósitos del mes.</param>
/// <param name="AhorroAcumulado">Ahorro acumulado hasta el mes: aportaciones más depósitos.</param>
/// <param name="AhorroRetirado">Ahorro retirado hasta el mes.</param>
/// <param name="AhorroDisponible">Ahorro acumulado menos retirado y menos gastado desde el ahorro; no baja de 0: la API rechaza los cambios que lo dejarían en negativo.</param>
/// <param name="RetiradasAhorro">Retiradas de ahorro registradas en el mes.</param>
/// <param name="AhorroDepositado">Parte del ahorro acumulado que entró como depósitos aparte de las aportaciones.</param>
/// <param name="DepositosAhorro">Depósitos de ahorro registrados en el mes.</param>
/// <param name="AhorroGastado">Gastos pagados desde el ahorro hasta el mes; restan del ahorro disponible.</param>
public record CuentaComunResponse(
    string Mes, decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteCuentaDto> Pendientes, decimal Efectivo,
    IReadOnlyList<AportacionCuentaDto> Aportaciones, IReadOnlyList<ReembolsoCuentaDto> Reembolsos,
    decimal AhorroMes = 0m, decimal AhorroAcumulado = 0m, decimal AhorroRetirado = 0m, decimal AhorroDisponible = 0m,
    IReadOnlyList<RetiradaAhorroDto>? RetiradasAhorro = null,
    decimal AhorroDepositado = 0m, IReadOnlyList<DepositoAhorroDto>? DepositosAhorro = null,
    decimal AhorroGastado = 0m);
