namespace MiParte.Core.Domain.Entidades;

public enum TipoMiembro { Adulto, ACargo }

public enum ModoReparto { Porcentaje, Partes, Ingresos, Individual }

public enum RolMiembro { Admin, Miembro }

public class Hogar
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public DateTimeOffset CreadoEn { get; set; }
}

public class Miembro
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public string Nombre { get; set; } = "";
    public TipoMiembro Tipo { get; set; }
    public Guid? ResponsableId { get; set; }
    public Guid? UserId { get; set; }
    public bool Activo { get; set; } = true;
    public RolMiembro Rol { get; set; } = RolMiembro.Miembro;
}

/// <summary>
/// Invitación para unirse a un hogar. Solo se guarda el hash SHA-256 (hex minúscula)
/// del token. Si <see cref="MiembroId"/> es null, quien acepta entra como adulto nuevo.
/// </summary>
public class InvitacionHogar
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public Guid? MiembroId { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid CreadaPor { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
    public DateTimeOffset CaducaEn { get; set; }
    public DateTimeOffset? UsadaEn { get; set; }
    public Guid? UsadaPor { get; set; }
}

/// <summary>
/// Transferencia real entre miembros que salda la liquidación de un mes (tabla
/// pago_liquidacion). Se llama PagoLiquidacionRegistro porque el record de dominio
/// puro MiParte.Core.Domain.PagoLiquidacion (Liquidacion.cs) ya usa ese nombre.
/// </summary>
public class PagoLiquidacionRegistro
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    /// <summary>Primer día del mes liquidado.</summary>
    public DateOnly Mes { get; set; }
    public Guid DeMiembroId { get; set; }
    public Guid AMiembroId { get; set; }
    public decimal Importe { get; set; }
    public DateOnly Fecha { get; set; }
    public string? Concepto { get; set; }
}

public class PerfilReparto
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public string Nombre { get; set; } = "";
    public ModoReparto Modo { get; set; }
    public List<PerfilRepartoDetalle> Detalles { get; set; } = [];
}

public class PerfilRepartoDetalle
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public Guid PerfilId { get; set; }
    public Guid MiembroId { get; set; }
    /// <summary>Porcentaje o partes, según el modo del perfil.</summary>
    public decimal Valor { get; set; }
}

public class Categoria
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public string Nombre { get; set; } = "";
    public Guid? CategoriaPadreId { get; set; }
    public Guid? PerfilRepartoId { get; set; }
}

public class Ingreso
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public Guid MiembroId { get; set; }
    public DateOnly Fecha { get; set; }
    public decimal Importe { get; set; }
    public string? Concepto { get; set; }
}

public class GastoRecurrente
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public decimal Importe { get; set; }
    public Guid CategoriaId { get; set; }
    public Guid PagadoPor { get; set; }
    public Guid PerfilRepartoId { get; set; }
    public short DiaMes { get; set; }
    public string? Concepto { get; set; }
    public bool Activo { get; set; } = true;
}

public class Gasto
{
    public Guid Id { get; set; }
    public Guid HogarId { get; set; }
    public DateOnly Fecha { get; set; }
    public decimal Importe { get; set; }
    public Guid CategoriaId { get; set; }
    public Guid PagadoPor { get; set; }
    public Guid PerfilRepartoId { get; set; }
    public string? Concepto { get; set; }
    public Guid? GastoRecurrenteId { get; set; }
    public List<GastoReparto> Repartos { get; set; } = [];
}

/// <summary>Importe asumido por cada miembro, calculado al registrar el gasto.</summary>
public class GastoReparto
{
    public Guid GastoId { get; set; }
    public Guid MiembroId { get; set; }
    public Guid HogarId { get; set; }
    public decimal ImporteAsumido { get; set; }
}
