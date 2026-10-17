namespace MiParte.Contracts;

/// <summary>Alta de un hogar; quien lo crea queda como su primer miembro (admin y adulto).</summary>
/// <param name="NombreHogar">Nombre del hogar.</param>
/// <param name="NombreMiembro">Nombre del miembro que crea el hogar.</param>
/// <param name="Tipo">Tipo de grupo: "hogar" (por defecto si se omite), "piso_compartido", "viaje", "evento" u "otro".</param>
public record CrearHogarRequest(string NombreHogar, string NombreMiembro, string? Tipo = null);

/// <summary>Valores de <see cref="HogarResumen.Tipo"/>: texto con el que viaja el tipo de grupo (TipoGrupo en Core.Domain).</summary>
public static class TiposGrupo
{
    /// <summary>Hogar familiar o de convivencia habitual (valor por defecto).</summary>
    public const string Hogar = "hogar";

    /// <summary>Piso compartido por personas que no forman una familia.</summary>
    public const string PisoCompartido = "piso_compartido";

    /// <summary>Viaje con gastos comunes durante un periodo acotado.</summary>
    public const string Viaje = "viaje";

    /// <summary>Evento puntual, como una boda o una cena de grupo.</summary>
    public const string Evento = "evento";

    /// <summary>Cualquier otro grupo de gastos compartidos.</summary>
    public const string Otro = "otro";
}

/// <summary>Modos de liquidación del hogar (texto de la API y de la base de datos).</summary>
public static class ModosLiquidacion
{
    /// <summary>Un saldo por mes natural (por defecto).</summary>
    public const string Mensual = "mensual";

    /// <summary>Cada cierre liquida los gastos pendientes desde el cierre anterior.</summary>
    public const string Ciclos = "ciclos";
}

/// <summary>Datos básicos de un hogar.</summary>
/// <param name="Id">Identificador del hogar.</param>
/// <param name="Nombre">Nombre del hogar.</param>
/// <param name="CuentaComunActiva">Si el hogar tiene activada la cuenta común.</param>
/// <param name="AhorroActivo">Si el hogar tiene activado el ahorro de la cuenta común.</param>
/// <param name="Tipo">Tipo de grupo (ver <see cref="TiposGrupo"/>); "hogar" por defecto.</param>
/// <param name="RecurrentesActivas">Si el hogar genera gastos recurrentes; con false no se crean plantillas ni se generan gastos.</param>
/// <param name="LiquidacionModo">Cómo liquida el hogar (ver <see cref="ModosLiquidacion"/>); "mensual" por defecto.</param>
public record HogarResumen(
    Guid Id, string Nombre, bool CuentaComunActiva = false, bool AhorroActivo = false, string Tipo = TiposGrupo.Hogar,
    bool RecurrentesActivas = true, string LiquidacionModo = ModosLiquidacion.Mensual);

/// <summary>Respuesta de GET /api/yo: usuario, sus hogares y el hogar actual (null si no se puede determinar).</summary>
/// <param name="UserId">Identificador del usuario (claim "sub" del JWT), o null si no consta.</param>
/// <param name="Hogares">Hogares a los que pertenece el usuario.</param>
/// <param name="HogarActual">Hogar resuelto para la petición, o null si no se puede determinar.</param>
public record YoResponse(string? UserId, IReadOnlyList<HogarResumen> Hogares, HogarResumen? HogarActual);
