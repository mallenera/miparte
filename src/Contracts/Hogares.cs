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
/// <param name="Periodo">Periodo propio del hogar («YYYY-MM-DD/YYYY-MM-DD» o «YYYY-MM-DD/» si está abierto); null si usa meses.</param>
/// <param name="PeriodoCerrado">Si el periodo propio está cerrado: sus gastos no se pueden editar ni borrar.</param>
/// <param name="AportacionFrecuencia">Con qué frecuencia aportan a la cuenta común (ver <see cref="FrecuenciasAportacion"/>); "mensual" por defecto.</param>
public record HogarResumen(
    Guid Id, string Nombre, bool CuentaComunActiva = false, bool AhorroActivo = false, string Tipo = TiposGrupo.Hogar,
    bool RecurrentesActivas = true, string LiquidacionModo = ModosLiquidacion.Mensual,
    string? Periodo = null, bool PeriodoCerrado = false, string AportacionFrecuencia = FrecuenciasAportacion.Mensual);

/// <summary>Frecuencias de aportación a la cuenta común, tal como viajan por la API.</summary>
public static class FrecuenciasAportacion
{
    /// <summary>Una sola aportación por persona, sin periodo.</summary>
    public const string Unica = "unica";

    /// <summary>Cada mes (por defecto; el comportamiento de los hogares existentes).</summary>
    public const string Mensual = "mensual";

    /// <summary>Cada dos meses.</summary>
    public const string Bimestral = "bimestral";

    /// <summary>Cada tres meses.</summary>
    public const string Trimestral = "trimestral";

    /// <summary>Cada seis meses.</summary>
    public const string Semestral = "semestral";

    /// <summary>Cada doce meses.</summary>
    public const string Anual = "anual";
}

/// <summary>PUT /api/hogar/aportaciones: con qué frecuencia aportan los miembros a la cuenta común. Permiso hogar.funciones.</summary>
/// <param name="Frecuencia">Una de las de <see cref="FrecuenciasAportacion"/>.</param>
public record FijarFrecuenciaAportacionRequest(string? Frecuencia);

/// <summary>Respuesta de PUT /api/hogar/aportaciones.</summary>
/// <param name="Frecuencia">Frecuencia guardada.</param>
public record FrecuenciaAportacionResponse(string Frecuencia);

/// <summary>Respuesta de POST /api/hogar/cerrar-periodo: el periodo ya congelado y la liquidación final, si había gastos pendientes.</summary>
/// <param name="Periodo">Periodo tras el cierre («YYYY-MM-DD/YYYY-MM-DD»).</param>
/// <param name="CerradoEn">Momento del cierre.</param>
/// <param name="Ciclo">Liquidación final; null si no quedaban gastos pendientes.</param>
public record CierrePeriodoResponse(string Periodo, DateTimeOffset CerradoEn, CicloLiquidacionDto? Ciclo);

/// <summary>Respuesta de GET /api/yo: usuario, sus hogares y el hogar actual (null si no se puede determinar).</summary>
/// <param name="UserId">Identificador del usuario (claim "sub" del JWT), o null si no consta.</param>
/// <param name="Hogares">Hogares a los que pertenece el usuario.</param>
/// <param name="HogarActual">Hogar resuelto para la petición, o null si no se puede determinar.</param>
public record YoResponse(string? UserId, IReadOnlyList<HogarResumen> Hogares, HogarResumen? HogarActual);
