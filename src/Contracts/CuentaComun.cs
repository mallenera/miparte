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
/// <param name="DesdeAhorro">Si sale del fondo del ahorro (true) o de Hogar (false).</param>
public record ReembolsoCuentaDto(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto, bool DesdeAhorro = false);

/// <summary>
/// Lo que una persona debe a la cuenta común (o a su ahorro) por adelantos aún no devueltos ni compensados.
/// </summary>
/// <param name="MiembroId">Persona que debe.</param>
/// <param name="Nombre">Nombre de la persona.</param>
/// <param name="Importe">Lo que debe a ese fondo (positivo).</param>
/// <param name="DesdeAhorro">Si la deuda es con el ahorro (true) o con Hogar (false).</param>
public record DeudaCuentaDto(Guid MiembroId, string Nombre, decimal Importe, bool DesdeAhorro = false);

/// <summary>Movimiento de la deuda de una persona con la cuenta común, tal como se guarda.</summary>
/// <param name="Id">Identificador del movimiento.</param>
/// <param name="MiembroId">Persona a la que se refiere.</param>
/// <param name="Fecha">Fecha del movimiento.</param>
/// <param name="Importe">Importe (positivo).</param>
/// <param name="Tipo">«adelanto», «devolucion» o «compensacion».</param>
/// <param name="DesdeAhorro">Si es del fondo del ahorro (true) o de Hogar (false).</param>
/// <param name="Concepto">Nota opcional.</param>
public record MovimientoDeudaCuentaDto(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string Tipo, bool DesdeAhorro, string? Concepto);

/// <summary>
/// POST /api/cuenta-comun/deudas. <paramref name="Tipo"/> es «adelanto» (la cuenta paga por la persona, que le debe), «devolucion»
/// (la persona ingresa para pagar su deuda) o «compensacion» (la cuenta resta lo que le debe por reembolsos pendientes).
/// Fecha por defecto: hoy (UTC).
/// </summary>
/// <param name="MiembroId">Persona a la que se refiere.</param>
/// <param name="Tipo">«adelanto», «devolucion» o «compensacion».</param>
/// <param name="Importe">Importe, mayor que 0.</param>
/// <param name="Fecha">Fecha del movimiento; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Nota opcional.</param>
/// <param name="DesdeAhorro">Si es del fondo del ahorro (true) o de Hogar (false, por defecto).</param>
public record CrearDeudaCuentaRequest(Guid MiembroId, string Tipo, decimal Importe, DateOnly? Fecha, string? Concepto, bool DesdeAhorro = false);

/// <summary>POST /api/cuenta-comun/reembolsos. Fecha por defecto: hoy (UTC).</summary>
/// <param name="MiembroId">Miembro que recibe el reembolso.</param>
/// <param name="Importe">Importe reembolsado.</param>
/// <param name="Fecha">Fecha del reembolso; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Descripción opcional.</param>
/// <param name="DesdeAhorro">Fondo del que se paga: el ahorro (true) o Hogar (false, por defecto). Un reembolso nunca mezcla fondos.</param>
public record CrearReembolsoRequest(Guid MiembroId, decimal Importe, DateOnly? Fecha, string? Concepto, bool DesdeAhorro = false);

/// <summary>Dinero que entra a la cuenta común fuera de la aportación mensual y del ahorro (p. ej. lo que pagan las amigas de una cena). Suma al saldo y al efectivo de la cuenta desde su fecha; no toca el ahorro ni los reembolsos pendientes.</summary>
/// <param name="Id">Identificador del ingreso.</param>
/// <param name="MiembroId">Miembro que lo registra.</param>
/// <param name="Fecha">Fecha del ingreso.</param>
/// <param name="Importe">Importe ingresado.</param>
/// <param name="Concepto">De dónde viene (opcional).</param>
public record IngresoCuentaComunDto(Guid Id, Guid MiembroId, DateOnly Fecha, decimal Importe, string? Concepto);

/// <summary>POST /api/cuenta-comun/ingresos. Fecha por defecto: hoy (UTC).</summary>
/// <param name="MiembroId">Miembro que registra el ingreso.</param>
/// <param name="Importe">Importe ingresado.</param>
/// <param name="Fecha">Fecha del ingreso; si es null se usa hoy (UTC).</param>
/// <param name="Concepto">Descripción opcional.</param>
public record CrearIngresoCuentaComunRequest(Guid MiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);

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
/// <param name="DesdeAhorro">Si el pendiente es de adelantos pagados desde el ahorro (true) o de Hogar (false).</param>
public record PendienteCuentaDto(Guid MiembroId, string Nombre, decimal Importe, bool DesdeAhorro = false);

/// <summary>«Su parte» de un miembro en la cuenta común: lo que ha puesto y la porción que le toca del saldo y del ahorro.</summary>
/// <param name="MiembroId">Miembro.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Aportado">Aportado para gastos (sin ahorro) hasta el mes.</param>
/// <param name="Ahorrado">Apartado a ahorro hasta el mes: parte de ahorro de sus aportaciones más sus depósitos.</param>
/// <param name="PorcentajeGastos">Su peso sobre lo aportado para gastos (0-100).</param>
/// <param name="PorcentajeAhorro">Su peso sobre lo ahorrado (0-100).</param>
/// <param name="ParteSaldo">Su porción del saldo de gastos, en proporción a lo aportado; negativa si la cuenta está en descubierto.</param>
/// <param name="ParteAhorro">Su porción del ahorro disponible, en proporción a lo ahorrado.</param>
/// <param name="Pendiente">Lo que la cuenta le debe por gastos que adelantó (puede ser 0).</param>
public record PartePersonaDto(
    Guid MiembroId, string Nombre, decimal Aportado, decimal Ahorrado, decimal PorcentajeGastos, decimal PorcentajeAhorro,
    decimal ParteSaldo, decimal ParteAhorro, decimal Pendiente);

/// <summary>PUT /api/cuenta-comun/activacion (solo admin): activa o desactiva la cuenta común del hogar.</summary>
/// <param name="Activa">true para usar la cuenta común; false la oculta sin borrar sus datos.</param>
public record ActivarCuentaComunRequest(bool Activa);

/// <summary>PUT /api/cuenta-comun/ahorro/activacion. Solo admin; exige la cuenta común activada.</summary>
/// <param name="Activo">true para usar el ahorro; false lo bloquea sin borrar sus datos.</param>
public record ActivarAhorroRequest(bool Activo);

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
/// <param name="Activa">Si el hogar tiene activada la cuenta común; si es false las escrituras de la cuenta y los gastos a su cargo responden 409 (las cifras se devuelven igualmente).</param>
/// <param name="Partes">«Su parte» de cada miembro con aportaciones o depósitos hasta el mes.</param>
/// <param name="AhorroActivo">Si el hogar tiene activado el ahorro; si es false las escrituras del ahorro responden 409.</param>
/// <param name="Extracto">Cómo cambia el efectivo de la cuenta en el mes, como un extracto bancario.</param>
/// <param name="SaldoInicial">Saldo inicial fijado para la cuenta (con su fecha), o null si no hay.</param>
/// <param name="Resumen">Resumen mensual por Hogar y Ahorro, con el inicial arrastrado del mes anterior.</param>
public record CuentaComunResponse(
    string Mes, decimal AportadoMes, decimal Aportado, decimal Gastado, decimal Saldo,
    IReadOnlyList<PendienteCuentaDto> Pendientes, decimal Efectivo,
    IReadOnlyList<AportacionCuentaDto> Aportaciones, IReadOnlyList<ReembolsoCuentaDto> Reembolsos,
    decimal AhorroMes = 0m, decimal AhorroAcumulado = 0m, decimal AhorroRetirado = 0m, decimal AhorroDisponible = 0m,
    IReadOnlyList<RetiradaAhorroDto>? RetiradasAhorro = null,
    decimal AhorroDepositado = 0m, IReadOnlyList<DepositoAhorroDto>? DepositosAhorro = null,
    decimal AhorroGastado = 0m, bool Activa = true, IReadOnlyList<PartePersonaDto>? Partes = null,
    bool AhorroActivo = true, ExtractoCuentaComunDto? Extracto = null, SaldoInicialDto? SaldoInicial = null,
    string AportacionFrecuencia = FrecuenciasAportacion.Mensual, ResumenCuentaMesDto? Resumen = null,
    IReadOnlyList<DeudaCuentaDto>? Deudas = null, IReadOnlyList<MovimientoDeudaCuentaDto>? MovimientosDeuda = null);

/// <summary>
/// Extracto del mes de la cuenta común: el efectivo al inicio, lo que entra y sale, y el efectivo al final. Los importes llevan su signo
/// natural: las entradas son positivas y las salidas se restan al sumar, así que la suma de los movimientos da la diferencia exacta.
/// </summary>
/// <param name="EfectivoInicial">Efectivo al inicio del mes (el final del mes anterior).</param>
/// <param name="Aportaciones">Lo que ponen los adultos para gastos este mes (sin la parte de ahorro).</param>
/// <param name="SaldoInicialFijado">Saldo inicial fijado si cae en este mes; 0 si no.</param>
/// <param name="GastosCuenta">Gastos que paga la cuenta directamente este mes.</param>
/// <param name="GastosAdelantadosDelAhorro">Gastos pagados desde el ahorro por un miembro: suben el efectivo porque la cuenta queda a deber ese importe.</param>
/// <param name="Reembolsos">Reembolsos a quien adelantó gastos este mes.</param>
/// <param name="EfectivoFinal">Efectivo al final del mes.</param>
/// <param name="Ingresos">Ingresos sueltos del mes, fuera de las aportaciones.</param>
public record ExtractoCuentaComunDto(
    decimal EfectivoInicial, decimal Aportaciones, decimal SaldoInicialFijado, decimal GastosCuenta,
    decimal GastosAdelantadosDelAhorro, decimal Reembolsos, decimal EfectivoFinal, decimal Ingresos = 0m);

/// <summary>Una fila del resumen mensual de la cuenta común (Hogar, Ahorro o Total).</summary>
/// <param name="Inicial">Disponible al empezar el mes: el de la fila el mes anterior.</param>
/// <param name="Ingresos">Aportaciones del mes a la fila.</param>
/// <param name="IngresosSueltos">Ingresos aparte del mes (a Hogar, o depósitos de ahorro).</param>
/// <param name="Gastos">Lo que sale de la fila en el mes (en Ahorro, incluye las retiradas).</param>
/// <param name="Reembolsos">Lo devuelto a quien adelantó gastos en el mes (solo Hogar).</param>
/// <param name="Disponible">Lo que queda al final del mes: inicial, más lo que entra, menos lo que sale.</param>
/// <param name="Pendiente">Lo que la cuenta aún debe al cierre del mes por los adelantos de esa fila (Hogar o Ahorro).</param>
public record FilaResumenCuentaDto(decimal Inicial, decimal Ingresos, decimal IngresosSueltos, decimal Gastos, decimal Reembolsos, decimal Disponible, decimal Pendiente = 0m);

/// <summary>Resumen mensual de la cuenta común: Hogar (dinero para gastos), Ahorro y Total.</summary>
/// <param name="Hogar">Fila de Hogar.</param>
/// <param name="Ahorro">Fila de Ahorro.</param>
/// <param name="Total">Suma de Hogar y Ahorro.</param>
public record ResumenCuentaMesDto(FilaResumenCuentaDto Hogar, FilaResumenCuentaDto Ahorro, FilaResumenCuentaDto Total);

/// <summary>Saldo inicial de la cuenta común: dinero que ya había en la cuenta para gastos a una fecha.</summary>
/// <param name="Fecha">Día al que corresponde el saldo.</param>
/// <param name="Importe">Importe (0 o más, máx. 2 decimales).</param>
public record SaldoInicialDto(DateOnly Fecha, decimal Importe);

/// <summary>PUT /api/cuenta-comun/saldo-inicial: fija (o sustituye) el saldo inicial de la cuenta para gastos.</summary>
/// <param name="Fecha">Día al que corresponde el saldo.</param>
/// <param name="Importe">Dinero que había en la cuenta a esa fecha (0 o más, máx. 2 decimales).</param>
public record FijarSaldoInicialRequest(DateOnly Fecha, decimal Importe);

/// <summary>Una línea del extracto bancario de la cuenta común (gastos y ahorro juntos).</summary>
/// <param name="Tipo">saldo_inicial, aportacion, ingreso, deposito_ahorro, gasto, gasto_ahorro, gasto_ahorro_adelantado, reembolso o retirada_ahorro.</param>
/// <param name="Id">Registro de origen (gasto, reembolso, ingreso, depósito o retirada); null en aportaciones y saldo inicial.</param>
/// <param name="Fecha">Fecha del movimiento; las aportaciones se fechan el día 1 del mes en que cobran.</param>
/// <param name="Importe">Importe con signo: positivo si entra en la cuenta, negativo si sale.</param>
/// <param name="MiembroId">Miembro que aporta, recibe el reembolso, registra el movimiento o adelanta un gasto del ahorro; null en los gastos que paga la cuenta y en el saldo inicial.</param>
/// <param name="CategoriaId">Categoría del gasto; null en el resto.</param>
/// <param name="Concepto">Nota del movimiento, si la tiene.</param>
/// <param name="Ahorro">Lo que la línea cambia el ahorro sin verse en el importe: la parte de una aportación que va a ahorro (positiva) o lo que un gasto del ahorro adelantado (<c>gasto_ahorro_adelantado</c>, importe 0) saca de él para pasarlo al efectivo (negativo); 0 en el resto.</param>
/// <param name="Saldo">Saldo de la cuenta tras el movimiento.</param>
public record MovimientoCuentaComunDto(
    string Tipo, Guid? Id, DateOnly Fecha, decimal Importe, Guid? MiembroId, Guid? CategoriaId, string? Concepto, decimal Ahorro, decimal Saldo);

/// <summary>GET /api/cuenta-comun/movimientos?desde=YYYY-MM-DD&amp;hasta=YYYY-MM-DD: extracto de la cuenta para cuadrarlo con el banco.</summary>
/// <param name="Desde">Primer día incluido.</param>
/// <param name="Hasta">Último día incluido.</param>
/// <param name="SaldoAnterior">Saldo de la cuenta al terminar el día anterior a <paramref name="Desde"/>.</param>
/// <param name="Entradas">Suma de lo que entra en el periodo.</param>
/// <param name="Salidas">Suma de lo que sale en el periodo (positiva).</param>
/// <param name="SaldoFinal">Saldo al terminar <paramref name="Hasta"/>: <paramref name="EfectivoFinal"/> más <paramref name="AhorroFinal"/>.</param>
/// <param name="Movimientos">Líneas del periodo en orden cronológico; en un mismo día, primero las entradas.</param>
/// <param name="EfectivoFinal">Parte del saldo final que es efectivo para gastos (el mismo cálculo que el efectivo de la cuenta).</param>
/// <param name="AhorroFinal">Parte del saldo final que es ahorro disponible.</param>
public record MovimientosCuentaComunResponse(
    DateOnly Desde, DateOnly Hasta, decimal SaldoAnterior, decimal Entradas, decimal Salidas, decimal SaldoFinal,
    IReadOnlyList<MovimientoCuentaComunDto> Movimientos, decimal EfectivoFinal = 0m, decimal AhorroFinal = 0m);
