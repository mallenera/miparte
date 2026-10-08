namespace MiParte.Core.Domain.Entidades;

/// <summary>Tipo de miembro: adulto con cuenta propia o persona a cargo de un responsable.</summary>
public enum TipoMiembro { Adulto, ACargo }

/// <summary>Modo en que se reparte un gasto: por porcentaje, partes, a cargo de la cuenta común (sin reparto entre personas) o íntegro a quien paga.</summary>
public enum ModoReparto { Porcentaje, Partes, CuentaComun, Individual }

/// <summary>Rol de un miembro dentro del hogar.</summary>
public enum RolMiembro { Admin, Miembro }

/// <summary>Hogar: unidad de multitenencia a la que pertenecen todos los datos de negocio.</summary>
public class Hogar
{
    /// <summary>Identificador del hogar.</summary>
    public Guid Id { get; set; }
    /// <summary>Nombre visible del hogar.</summary>
    public string Nombre { get; set; } = "";
    /// <summary>Fecha de creación, asignada por la base de datos.</summary>
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>Persona del hogar que participa en gastos e ingresos; puede o no tener usuario asociado.</summary>
public class Miembro
{
    /// <summary>Identificador del miembro.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Nombre visible del miembro.</summary>
    public string Nombre { get; set; } = "";
    /// <summary>Si es adulto o está a cargo de otro miembro.</summary>
    public TipoMiembro Tipo { get; set; }
    /// <summary>Miembro responsable cuando este está a cargo; null en otro caso.</summary>
    public Guid? ResponsableId { get; set; }
    /// <summary>Usuario de Supabase Auth vinculado (claim sub del JWT); null si aún no ha aceptado invitación.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Indica si el miembro está activo en el hogar.</summary>
    public bool Activo { get; set; } = true;
    /// <summary>Rol dentro del hogar (por defecto, miembro).</summary>
    public RolMiembro Rol { get; set; } = RolMiembro.Miembro;
}

/// <summary>
/// Invitación para unirse a un hogar. Solo se guarda el hash SHA-256 (hex minúscula)
/// del token. Si <see cref="MiembroId"/> es null, quien acepta entra como adulto nuevo.
/// </summary>
public class InvitacionHogar
{
    /// <summary>Identificador de la invitación.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que invita.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Miembro existente que se reclamará al aceptar; null para crear un adulto nuevo.</summary>
    public Guid? MiembroId { get; set; }
    /// <summary>Hash SHA-256 del token (nunca se almacena el token en claro).</summary>
    public string TokenHash { get; set; } = "";
    /// <summary>Usuario que creó la invitación.</summary>
    public Guid CreadaPor { get; set; }
    /// <summary>Fecha de creación, asignada por la base de datos.</summary>
    public DateTimeOffset CreadaEn { get; set; }
    /// <summary>Fecha a partir de la cual la invitación deja de ser válida.</summary>
    public DateTimeOffset CaducaEn { get; set; }
    /// <summary>Fecha de uso; null mientras no se haya aceptado.</summary>
    public DateTimeOffset? UsadaEn { get; set; }
    /// <summary>Usuario que la aceptó; null mientras no se haya usado.</summary>
    public Guid? UsadaPor { get; set; }
}

/// <summary>
/// Transferencia real entre miembros que salda la liquidación de un mes (tabla
/// pago_liquidacion). Se llama PagoLiquidacionRegistro porque el record de dominio
/// puro MiParte.Core.Domain.PagoLiquidacion (Liquidacion.cs) ya usa ese nombre.
/// </summary>
public class PagoLiquidacionRegistro
{
    /// <summary>Identificador del pago.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece el pago.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Primer día del mes liquidado.</summary>
    public DateOnly Mes { get; set; }
    /// <summary>Miembro que paga.</summary>
    public Guid DeMiembroId { get; set; }
    /// <summary>Miembro que recibe el pago.</summary>
    public Guid AMiembroId { get; set; }
    /// <summary>Importe transferido (positivo).</summary>
    public decimal Importe { get; set; }
    /// <summary>Fecha en que se realizó el pago.</summary>
    public DateOnly Fecha { get; set; }
    /// <summary>Nota opcional del pago.</summary>
    public string? Concepto { get; set; }
}

/// <summary>Aportación fija mensual de un adulto a la cuenta común, vigente desde un mes (tabla aportacion_cuenta).</summary>
public class AportacionCuenta
{
    /// <summary>Identificador de la aportación.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Adulto que aporta.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>Primer día del mes desde el que vale el importe.</summary>
    public DateOnly Desde { get; set; }
    /// <summary>Importe mensual; 0 deja de aportar.</summary>
    public decimal Importe { get; set; }
    /// <summary>Parte del importe mensual que va a ahorro (entre 0 e <see cref="Importe"/>); el resto queda para gastos.</summary>
    public decimal Ahorro { get; set; }
}

/// <summary>Dinero que entra al ahorro de la cuenta común fuera de la aportación mensual (tabla deposito_ahorro).</summary>
public class DepositoAhorro
{
    /// <summary>Identificador del depósito.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Miembro que lo registra.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>Fecha del depósito.</summary>
    public DateOnly Fecha { get; set; }
    /// <summary>Importe depositado (positivo).</summary>
    public decimal Importe { get; set; }
    /// <summary>Nota opcional (ahorro inicial, lotería...).</summary>
    public string? Concepto { get; set; }
}

/// <summary>Dinero que el hogar saca del ahorro de la cuenta común (tabla retirada_ahorro).</summary>
public class RetiradaAhorro
{
    /// <summary>Identificador de la retirada.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Miembro que la registra.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>Fecha de la retirada.</summary>
    public DateOnly Fecha { get; set; }
    /// <summary>Importe retirado (positivo).</summary>
    public decimal Importe { get; set; }
    /// <summary>Nota opcional (para qué se retira).</summary>
    public string? Concepto { get; set; }
}

/// <summary>Pago de la cuenta común a quien adelantó un gasto cargado a ella (tabla reembolso_cuenta).</summary>
public class ReembolsoCuenta
{
    /// <summary>Identificador del reembolso.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Miembro reembolsado.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>Fecha del reembolso.</summary>
    public DateOnly Fecha { get; set; }
    /// <summary>Importe reembolsado (positivo).</summary>
    public decimal Importe { get; set; }
    /// <summary>Nota opcional.</summary>
    public string? Concepto { get; set; }
}

/// <summary>Perfil de reparto reutilizable: un modo y los valores por miembro (detalles).</summary>
public class PerfilReparto
{
    /// <summary>Identificador del perfil.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece el perfil.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Nombre del perfil.</summary>
    public string Nombre { get; set; } = "";
    /// <summary>Modo de reparto que aplica el perfil.</summary>
    public ModoReparto Modo { get; set; }
    /// <summary>Valor asignado a cada miembro (porcentaje o partes).</summary>
    public List<PerfilRepartoDetalle> Detalles { get; set; } = [];
}

/// <summary>Valor de un miembro dentro de un perfil de reparto.</summary>
public class PerfilRepartoDetalle
{
    /// <summary>Identificador del detalle.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece el detalle.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Perfil al que pertenece.</summary>
    public Guid PerfilId { get; set; }
    /// <summary>Miembro al que se refiere el valor.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>Porcentaje o partes, según el modo del perfil.</summary>
    public decimal Valor { get; set; }
}

/// <summary>Categoría de gasto, opcionalmente jerárquica y con un perfil de reparto por defecto.</summary>
public class Categoria
{
    /// <summary>Identificador de la categoría.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece la categoría.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Nombre de la categoría.</summary>
    public string Nombre { get; set; } = "";
    /// <summary>Categoría padre; null si es de primer nivel.</summary>
    public Guid? CategoriaPadreId { get; set; }
    /// <summary>Perfil de reparto por defecto de la categoría; null si no tiene.</summary>
    public Guid? PerfilRepartoId { get; set; }
}

/// <summary>Plantilla de gasto que se genera periódicamente un día fijo del mes.</summary>
public class GastoRecurrente
{
    /// <summary>Identificador del gasto recurrente.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Importe de cada gasto generado.</summary>
    public decimal Importe { get; set; }
    /// <summary>Categoría de los gastos generados.</summary>
    public Guid CategoriaId { get; set; }
    /// <summary>Miembro que paga los gastos generados.</summary>
    public Guid PagadoPor { get; set; }
    /// <summary>Perfil de reparto aplicado a los gastos generados.</summary>
    public Guid PerfilRepartoId { get; set; }
    /// <summary>Día del mes en que se genera el gasto.</summary>
    public short DiaMes { get; set; }
    /// <summary>Descripción opcional.</summary>
    public string? Concepto { get; set; }
    /// <summary>Indica si la recurrencia sigue generando gastos.</summary>
    public bool Activo { get; set; } = true;
}

/// <summary>Gasto del hogar pagado por un miembro y repartido entre varios según un perfil.</summary>
public class Gasto
{
    /// <summary>Identificador del gasto.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece el gasto.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Fecha del gasto.</summary>
    public DateOnly Fecha { get; set; }
    /// <summary>Importe total del gasto.</summary>
    public decimal Importe { get; set; }
    /// <summary>Categoría del gasto.</summary>
    public Guid CategoriaId { get; set; }
    /// <summary>Miembro que adelantó el pago; null si lo paga directamente la cuenta común.</summary>
    public Guid? PagadoPor { get; set; }
    /// <summary>Perfil de reparto con el que se calcularon las partes.</summary>
    public Guid PerfilRepartoId { get; set; }
    /// <summary>Descripción opcional.</summary>
    public string? Concepto { get; set; }
    /// <summary>Gasto recurrente que lo originó; null si se registró a mano.</summary>
    public Guid? GastoRecurrenteId { get; set; }
    /// <summary>Si lo asume la cuenta común (perfil «cuenta común»): no se reparte entre personas ni genera deuda.</summary>
    public bool ACargoCuentaComun { get; set; }
    /// <summary>Si se paga con el ahorro de la cuenta común: se descuenta del ahorro disponible, no del saldo de gastos.</summary>
    public bool PagadoDesdeAhorro { get; set; }
    /// <summary>Si es un gasto personal de quien lo paga: se asume al 100 % y queda fuera de la liquidación y de los totales del hogar.</summary>
    public bool EsPersonal { get; set; }
    /// <summary>Importe asumido por cada miembro.</summary>
    public List<GastoReparto> Repartos { get; set; } = [];
}

/// <summary>Importe asumido por cada miembro, calculado al registrar el gasto.</summary>
public class GastoReparto
{
    /// <summary>Gasto al que pertenece la parte.</summary>
    public Guid GastoId { get; set; }
    /// <summary>Miembro que asume la parte.</summary>
    public Guid MiembroId { get; set; }
    /// <summary>Hogar al que pertenece (para el filtro por hogar).</summary>
    public Guid HogarId { get; set; }
    /// <summary>Importe que asume el miembro.</summary>
    public decimal ImporteAsumido { get; set; }
}

/// <summary>
/// Cambio registrado en la auditoría (tabla auditoria, solo de añadir): quién hizo qué sobre qué entidad y
/// cuándo. <see cref="Antes"/> y <see cref="Despues"/> son JSON con los campos afectados.
/// </summary>
public class EventoAuditoria
{
    /// <summary>Identificador del evento.</summary>
    public Guid Id { get; set; }
    /// <summary>Hogar al que pertenece el cambio.</summary>
    public Guid HogarId { get; set; }
    /// <summary>Usuario (claim sub) que hizo el cambio; null si no hay sesión asociada.</summary>
    public Guid? UsuarioId { get; set; }
    /// <summary>Instante del cambio.</summary>
    public DateTimeOffset Cuando { get; set; }
    /// <summary>Acción: crear, editar, borrar, vincular o usar.</summary>
    public string Accion { get; set; } = "";
    /// <summary>Tipo de entidad afectada (gasto, miembro, pago_liquidacion...).</summary>
    public string Entidad { get; set; } = "";
    /// <summary>Identificador de la entidad afectada.</summary>
    public Guid EntidadId { get; set; }
    /// <summary>JSON con el valor anterior de los campos que cambian (o el estado completo al borrar); null al crear.</summary>
    public string? Antes { get; set; }
    /// <summary>JSON con el valor nuevo de los campos que cambian (o el estado completo al crear); null al borrar.</summary>
    public string? Despues { get; set; }
}
