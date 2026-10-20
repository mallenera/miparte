using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Cliente tipado de Core.Api. Cubre usuario, hogares, miembros, invitaciones, categorías, perfiles, gastos, gastos recurrentes, cuenta común, resumen, liquidación y auditoría; el resto se añadirá con cada pantalla.</summary>
public sealed class CoreApiClient
{
    /// <summary>Rutas cuyas lecturas se guardan en <see cref="CacheLecturas"/>.</summary>
    private static readonly string[] RutasEnCache = ["api/miembros", "api/categorias", "api/perfiles", "api/cierres-mes"];

    private readonly HttpClient _http;
    private readonly CacheLecturas? _cache;

    /// <summary>Crea el cliente.</summary>
    /// <param name="http">Cliente HTTP con la URL base de Core.Api y el <see cref="ManejadorCoreApi"/>.</param>
    /// <param name="cache">Caché de lecturas compartida por toda la aplicación; sin ella cada lectura va a la API.</param>
    public CoreApiClient(HttpClient http, CacheLecturas? cache = null)
    {
        _http = http;
        _cache = cache;
    }

    /// <summary>Usuario autenticado, sus hogares y el hogar actual (<c>GET /api/yo</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<YoResponse> YoAsync(CancellationToken ct = default) => ObtenerAsync<YoResponse>("api/yo", ct);

    /// <summary>Crea un hogar del que el usuario es admin (<c>POST /api/hogares</c>).</summary>
    /// <param name="peticion">Nombre del hogar y del miembro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> CrearHogarAsync(CrearHogarRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<HogarResumen>(HttpMethod.Post, "api/hogares", peticion, ct);

    /// <summary>Acepta una invitación y devuelve el hogar al que se une (<c>POST /api/invitaciones/aceptar</c>).</summary>
    /// <param name="peticion">Token de la invitación y, si hace falta, el nombre del nuevo miembro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> AceptarInvitacionAsync(AceptarInvitacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<HogarResumen>(HttpMethod.Post, "api/invitaciones/aceptar", peticion, ct);

    /// <summary>
    /// Une al usuario al hogar de un enlace de grupo reutilizable (<c>POST /api/enlaces-grupo/unirse</c>, sin hogar actual).
    /// Sin <c>Nombre</c> no se une a nadie: 400 si el enlace es válido y pide nombre, 404 si el código no es de un grupo.
    /// </summary>
    /// <param name="peticion">Código del enlace y, para entrar como miembro nuevo, el nombre.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> UnirseEnlaceGrupoAsync(UnirseEnlaceGrupoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<HogarResumen>(HttpMethod.Post, "api/enlaces-grupo/unirse", peticion, ct);

    /// <summary>Miembros activos del hogar actual (<c>GET /api/miembros</c>); <c>EsYo</c> marca al usuario autenticado.</summary>
    /// <param name="incluirInactivos">Incluye también a los desactivados (<c>?incluirInactivos=true</c>), para poner nombre a quien aparece en el historial.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<MiembroDto>> ListarMiembrosAsync(bool incluirInactivos = false, CancellationToken ct = default) =>
        ObtenerListaAsync<MiembroDto>(incluirInactivos ? "api/miembros?incluirInactivos=true" : "api/miembros", ct);

    /// <summary>Añade una persona sin cuenta (<c>POST /api/miembros</c>, solo admin).</summary>
    /// <param name="peticion">Nombre, tipo y responsable (si es a cargo).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MiembroDto> CrearMiembroAsync(CrearMiembroRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<MiembroDto>(HttpMethod.Post, "api/miembros", peticion, ct);

    /// <summary>Edita un miembro; los campos null no cambian (<c>PUT /api/miembros/{id}</c>).</summary>
    /// <param name="id">Miembro a editar.</param>
    /// <param name="peticion">Cambios.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MiembroDto> ActualizarMiembroAsync(Guid id, ActualizarMiembroRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<MiembroDto>(HttpMethod.Put, $"api/miembros/{id}", peticion, ct);

    /// <summary>Crea una invitación (<c>POST /api/invitaciones</c>, solo admin); el token solo se devuelve aquí.</summary>
    /// <param name="peticion">Miembro existente al que vincular, o null para un nuevo adulto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<InvitacionCreada> CrearInvitacionAsync(CrearInvitacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<InvitacionCreada>(HttpMethod.Post, "api/invitaciones", peticion, ct);

    /// <summary>Categorías del hogar actual (<c>GET /api/categorias</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<CategoriaDto>> ListarCategoriasAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<CategoriaDto>("api/categorias", ct);

    /// <summary>Crea una categoría (<c>POST /api/categorias</c>).</summary>
    /// <param name="peticion">Nombre, categoría padre y perfil por defecto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CategoriaDto> CrearCategoriaAsync(GuardarCategoriaRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<CategoriaDto>(HttpMethod.Post, "api/categorias", peticion, ct);

    /// <summary>Reemplaza todos los campos de una categoría (<c>PUT /api/categorias/{id}</c>).</summary>
    /// <param name="id">Categoría a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CategoriaDto> GuardarCategoriaAsync(Guid id, GuardarCategoriaRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<CategoriaDto>(HttpMethod.Put, $"api/categorias/{id}", peticion, ct);

    /// <summary>
    /// Elimina una categoría sin subcategorías (<c>DELETE /api/categorias/{id}</c>). Si tiene gastos hay que indicar a qué categoría
    /// pasan; sin destino la API responde 409 y la excepción lleva <see cref="ApiException.EnUso"/>.
    /// </summary>
    /// <param name="id">Categoría a eliminar.</param>
    /// <param name="moverA">Categoría que recibe sus gastos y plantillas, o null si no tiene.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarCategoriaAsync(Guid id, Guid? moverA = null, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, moverA is { } d ? $"api/categorias/{id}?moverA={d}" : $"api/categorias/{id}", ct);

    /// <summary>Perfiles de reparto con su detalle (<c>GET /api/perfiles</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<PerfilRepartoDto>> ListarPerfilesAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<PerfilRepartoDto>("api/perfiles", ct);

    /// <summary>Crea un perfil de reparto (<c>POST /api/perfiles</c>).</summary>
    /// <param name="peticion">Nombre, modo y detalle.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PerfilRepartoDto> CrearPerfilAsync(GuardarPerfilRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<PerfilRepartoDto>(HttpMethod.Post, "api/perfiles", peticion, ct);

    /// <summary>Reemplaza nombre, modo y detalle de un perfil (<c>PUT /api/perfiles/{id}</c>).</summary>
    /// <param name="id">Perfil a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PerfilRepartoDto> GuardarPerfilAsync(Guid id, GuardarPerfilRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<PerfilRepartoDto>(HttpMethod.Put, $"api/perfiles/{id}", peticion, ct);

    /// <summary>
    /// Elimina un perfil (<c>DELETE /api/perfiles/{id}</c>). Si está en uso hay que indicar a qué perfil pasa; sin destino la API
    /// responde 409 y la excepción lleva <see cref="ApiException.EnUso"/>.
    /// </summary>
    /// <param name="id">Perfil a eliminar.</param>
    /// <param name="moverA">Perfil que recibe sus categorías, gastos y plantillas, o null si no está en uso.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarPerfilAsync(Guid id, Guid? moverA = null, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, moverA is { } d ? $"api/perfiles/{id}?moverA={d}" : $"api/perfiles/{id}", ct);

    /// <summary>Gastos de un mes del hogar con su reparto (<c>GET /api/gastos</c>); la API exige acotar por mes o por fechas.</summary>
    /// <param name="mes">Mes en formato <c>YYYY-MM</c>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosAsync(string mes, CancellationToken ct = default) =>
        ListarGastosAsync(mes, null, null, null, ct);

    /// <summary>Gastos de un mes filtrados (<c>GET /api/gastos</c>); los filtros nulos o vacíos no se envían.</summary>
    /// <param name="mes">Mes en formato <c>YYYY-MM</c>.</param>
    /// <param name="categoriaId">Solo gastos de esta categoría.</param>
    /// <param name="miembroId">Solo gastos que paga o en cuyo reparto asume algo este miembro.</param>
    /// <param name="buscar">Texto que debe contener el concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosAsync(string mes, Guid? categoriaId, Guid? miembroId, string? buscar, CancellationToken ct = default)
    {
        var filtros = new List<string> { $"mes={Uri.EscapeDataString(mes)}" };
        if (categoriaId is { } c) filtros.Add($"categoriaId={c}");
        if (miembroId is { } m) filtros.Add($"miembroId={m}");
        if (!string.IsNullOrWhiteSpace(buscar)) filtros.Add($"buscar={Uri.EscapeDataString(buscar.Trim())}");
        return ObtenerAsync<List<GastoResponse>>("api/gastos?" + string.Join('&', filtros), ct);
    }

    /// <summary>Gastos del hogar con fecha entre dos días, ambos incluidos (<c>GET /api/gastos?desde=&amp;hasta=</c>).</summary>
    /// <param name="desde">Primer día del periodo.</param>
    /// <param name="hasta">Último día del periodo.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosPorRangoAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        ListarGastosPorRangoAsync(desde, hasta, null, null, ct);

    /// <summary>Gastos entre dos días, ambos incluidos, con filtros opcionales de quién paga o asume y de texto.</summary>
    /// <param name="desde">Primer día del periodo.</param>
    /// <param name="hasta">Último día del periodo.</param>
    /// <param name="miembroId">Solo gastos que paga o en cuyo reparto asume algo este miembro.</param>
    /// <param name="buscar">Texto que debe contener el concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoResponse>> ListarGastosPorRangoAsync(DateOnly desde, DateOnly hasta, Guid? miembroId, string? buscar, CancellationToken ct = default)
    {
        var url = $"api/gastos?desde={desde.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&hasta={hasta.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";
        if (miembroId is { } m) url += $"&miembroId={m}";
        if (!string.IsNullOrWhiteSpace(buscar)) url += $"&buscar={Uri.EscapeDataString(buscar.Trim())}";
        return ObtenerAsync<List<GastoResponse>>(url, ct);
    }

    /// <summary>Crea un gasto; el servidor calcula y guarda el reparto (<c>POST /api/gastos</c>).</summary>
    /// <param name="peticion">Datos del gasto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoResponse> CrearGastoAsync(GastoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<GastoResponse>(HttpMethod.Post, "api/gastos", peticion, ct);

    /// <summary>Edita un gasto y recalcula su reparto (<c>PUT /api/gastos/{id}</c>).</summary>
    /// <param name="id">Gasto a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoResponse> GuardarGastoAsync(Guid id, GastoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<GastoResponse>(HttpMethod.Put, $"api/gastos/{id}", peticion, ct);

    /// <summary>Elimina un gasto y su reparto (<c>DELETE /api/gastos/{id}</c>).</summary>
    /// <param name="id">Gasto a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarGastoAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/gastos/{id}", ct);

    /// <summary>Plantillas de gasto mensual del hogar, por día del mes (<c>GET /api/gastos-recurrentes</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<GastoRecurrenteResponse>> ListarRecurrentesAsync(CancellationToken ct = default) =>
        ObtenerAsync<List<GastoRecurrenteResponse>>("api/gastos-recurrentes", ct);

    /// <summary>Crea una plantilla de gasto mensual (<c>POST /api/gastos-recurrentes</c>).</summary>
    /// <param name="peticion">Importe, categoría, pagador, perfil, día del mes y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoRecurrenteResponse> CrearRecurrenteAsync(GastoRecurrenteRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<GastoRecurrenteResponse>(HttpMethod.Post, "api/gastos-recurrentes", peticion, ct);

    /// <summary>Edita una plantilla sin tocar los gastos ya generados (<c>PUT /api/gastos-recurrentes/{id}</c>).</summary>
    /// <param name="id">Plantilla a editar.</param>
    /// <param name="peticion">Nuevos valores.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GastoRecurrenteResponse> GuardarRecurrenteAsync(Guid id, GastoRecurrenteRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<GastoRecurrenteResponse>(HttpMethod.Put, $"api/gastos-recurrentes/{id}", peticion, ct);

    /// <summary>Borra una plantilla sin gastos generados (<c>DELETE /api/gastos-recurrentes/{id}</c>).</summary>
    /// <param name="id">Plantilla a borrar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarRecurrenteAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/gastos-recurrentes/{id}", ct);

    /// <summary>Crea los gastos del mes de las plantillas activas que aún no lo tienen; es idempotente (<c>POST /api/gastos-recurrentes/generar</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<GenerarRecurrentesResponse> GenerarRecurrentesAsync(string mes, CancellationToken ct = default) =>
        EnviarAsync<GenerarRecurrentesResponse>(HttpMethod.Post, $"api/gastos-recurrentes/generar?mes={Uri.EscapeDataString(mes)}", null, ct);

    /// <summary>Historial de cambios del hogar, del más reciente al más antiguo (<c>GET /api/auditoria</c>, solo admin).</summary>
    /// <param name="entidad">Tipo de entidad a filtrar, o null para todas.</param>
    /// <param name="limite">Eventos por página (1-200).</param>
    /// <param name="hasta">Instante del último evento recibido, para pedir la página siguiente.</param>
    /// <param name="despuesDeId">Id del último evento recibido; desempata los que comparten <paramref name="hasta"/>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<EventoAuditoriaDto>> ListarAuditoriaAsync(
        string? entidad = null, int limite = 50, DateTimeOffset? hasta = null, Guid? despuesDeId = null, CancellationToken ct = default)
    {
        var consulta = new List<string> { $"limite={limite}" };
        if (!string.IsNullOrEmpty(entidad)) consulta.Add($"entidad={Uri.EscapeDataString(entidad)}");
        if (hasta is { } h) consulta.Add($"hasta={Uri.EscapeDataString(h.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");
        if (despuesDeId is { } d) consulta.Add($"despuesDeId={d}");
        return ObtenerAsync<List<EventoAuditoriaDto>>($"api/auditoria?{string.Join('&', consulta)}", ct);
    }

    /// <summary>Estado de la cuenta común al final de un mes (<c>GET /api/cuenta-comun</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CuentaComunResponse> ObtenerCuentaComunAsync(string mes, CancellationToken ct = default) =>
        ObtenerAsync<CuentaComunResponse>($"api/cuenta-comun?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Activa o desactiva la cuenta común del hogar; solo un admin (<c>PUT /api/cuenta-comun/activacion</c>).</summary>
    /// <param name="activa">true para usar la cuenta común.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ActivarCuentaComunAsync(bool activa, CancellationToken ct = default) =>
        EnviarAsync<System.Text.Json.JsonElement>(HttpMethod.Put, "api/cuenta-comun/activacion", new ActivarCuentaComunRequest(activa), ct);

    /// <summary>
    /// Elimina el hogar actual con todos sus datos, sin vuelta atrás (<c>DELETE /api/hogar</c>); exige el permiso <c>hogar.eliminar</c> y el nombre exacto del hogar.
    /// </summary>
    /// <param name="nombre">Nombre del hogar, como confirmación.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarHogarAsync(string nombre, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/hogar?nombre={Uri.EscapeDataString(nombre)}", ct);

    /// <summary>Activa o desactiva el ahorro del hogar; solo un admin y con la cuenta común activada (<c>PUT /api/cuenta-comun/ahorro/activacion</c>).</summary>
    /// <param name="activo">true para usar el ahorro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ActivarAhorroAsync(bool activo, CancellationToken ct = default) =>
        EnviarAsync<System.Text.Json.JsonElement>(HttpMethod.Put, "api/cuenta-comun/ahorro/activacion", new ActivarAhorroRequest(activo), ct);

    /// <summary>Activa o desactiva los gastos recurrentes del hogar; solo con <c>hogar.funciones</c> (<c>PUT /api/hogar/recurrentes</c>).</summary>
    /// <param name="activas">true para crear plantillas y generar gastos.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ActivarRecurrentesAsync(bool activas, CancellationToken ct = default) =>
        EnviarAsync<System.Text.Json.JsonElement>(HttpMethod.Put, "api/hogar/recurrentes", new ActivarRecurrentesRequest(activas), ct);

    /// <summary>Cambia el modo de liquidación del hogar: <c>mensual</c> o <c>ciclos</c> (<c>PUT /api/hogar/liquidacion</c>, permiso <c>hogar.funciones</c>).</summary>
    /// <param name="modo">Modo nuevo (ver <c>ModosLiquidacion</c>).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ModoLiquidacionResponse> ActivarLiquidacionAsync(string modo, CancellationToken ct = default) =>
        EnviarAsync<ModoLiquidacionResponse>(HttpMethod.Put, "api/hogar/liquidacion", new ModoLiquidacionRequest(modo), ct);

    /// <summary>Fija con qué frecuencia aportan los miembros a la cuenta común (<c>PUT /api/hogar/aportaciones</c>, permiso <c>hogar.funciones</c>).</summary>
    /// <param name="frecuencia">Una de las frecuencias de <c>FrecuenciasAportacion</c>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<FrecuenciaAportacionResponse> FijarFrecuenciaAportacionAsync(string frecuencia, CancellationToken ct = default) =>
        EnviarAsync<FrecuenciaAportacionResponse>(HttpMethod.Put, "api/hogar/aportaciones", new FijarFrecuenciaAportacionRequest(frecuencia), ct);

    /// <summary>
    /// Cierra el periodo propio del hogar (viaje o evento): congela sus gastos y genera la liquidación final (<c>POST /api/hogar/cerrar-periodo</c>,
    /// permiso <c>mes.cerrar</c>).
    /// </summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CierrePeriodoResponse> CerrarPeriodoAsync(CancellationToken ct = default) =>
        EnviarAsync<CierrePeriodoResponse>(HttpMethod.Post, "api/hogar/cerrar-periodo", null, ct);

    /// <summary>Cierra el ciclo de liquidación actual: marca los gastos pendientes como liquidados (<c>POST /api/liquidacion/ciclos</c>, permiso <c>pagos.registrar</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CicloLiquidacionDto> CerrarCicloLiquidacionAsync(CancellationToken ct = default) =>
        EnviarAsync<CicloLiquidacionDto>(HttpMethod.Post, "api/liquidacion/ciclos", null, ct);

    /// <summary>Ciclos de liquidación cerrados, del más reciente al más antiguo (<c>GET /api/liquidacion/ciclos</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<CicloLiquidacionDto>> ListarCiclosLiquidacionAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<CicloLiquidacionDto>("api/liquidacion/ciclos", ct);

    /// <summary>Fija lo que aporta un adulto a la cuenta común desde un mes (<c>PUT /api/cuenta-comun/aportaciones</c>).</summary>
    /// <param name="peticion">Adulto, mes de inicio (día 1) e importe.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<AportacionCuentaDto> FijarAportacionAsync(FijarAportacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<AportacionCuentaDto>(HttpMethod.Put, "api/cuenta-comun/aportaciones", peticion, ct);

    /// <summary>Fija el saldo inicial de la cuenta para gastos a una fecha, sustituyendo el anterior (<c>PUT /api/cuenta-comun/saldo-inicial</c>).</summary>
    /// <param name="peticion">Fecha e importe (0 o más).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<SaldoInicialDto> FijarSaldoInicialAsync(FijarSaldoInicialRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<SaldoInicialDto>(HttpMethod.Put, "api/cuenta-comun/saldo-inicial", peticion, ct);

    /// <summary>Quita el saldo inicial de la cuenta (<c>DELETE /api/cuenta-comun/saldo-inicial</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarSaldoInicialAsync(CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, "api/cuenta-comun/saldo-inicial", ct);

    /// <summary>Registra un reembolso de la cuenta común a quien adelantó gastos (<c>POST /api/cuenta-comun/reembolsos</c>).</summary>
    /// <param name="peticion">Miembro, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ReembolsoCuentaDto> CrearReembolsoAsync(CrearReembolsoRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<ReembolsoCuentaDto>(HttpMethod.Post, "api/cuenta-comun/reembolsos", peticion, ct);

    /// <summary>Elimina un reembolso (<c>DELETE /api/cuenta-comun/reembolsos/{id}</c>).</summary>
    /// <param name="id">Reembolso a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarReembolsoAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/reembolsos/{id}", ct);

    /// <summary>Registra un ingreso aparte en el ahorro de la cuenta común (<c>POST /api/cuenta-comun/depositos-ahorro</c>).</summary>
    /// <param name="peticion">Miembro, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<DepositoAhorroDto> CrearDepositoAhorroAsync(CrearDepositoAhorroRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<DepositoAhorroDto>(HttpMethod.Post, "api/cuenta-comun/depositos-ahorro", peticion, ct);

    /// <summary>Elimina un ingreso de ahorro (<c>DELETE /api/cuenta-comun/depositos-ahorro/{id}</c>).</summary>
    /// <param name="id">Ingreso a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarDepositoAhorroAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/depositos-ahorro/{id}", ct);

    /// <summary>Registra una retirada del ahorro de la cuenta común (<c>POST /api/cuenta-comun/retiradas-ahorro</c>).</summary>
    /// <param name="peticion">Miembro, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<RetiradaAhorroDto> CrearRetiradaAhorroAsync(CrearRetiradaAhorroRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<RetiradaAhorroDto>(HttpMethod.Post, "api/cuenta-comun/retiradas-ahorro", peticion, ct);

    /// <summary>Elimina una retirada de ahorro (<c>DELETE /api/cuenta-comun/retiradas-ahorro/{id}</c>).</summary>
    /// <param name="id">Retirada a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarRetiradaAhorroAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/retiradas-ahorro/{id}", ct);

    /// <summary>Resumen del mes: pagado y asumido por miembro y total por categoría (<c>GET /api/resumen</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ResumenMensualResponse> ObtenerResumenAsync(string mes, CancellationToken ct = default) =>
        ObtenerAsync<ResumenMensualResponse>($"api/resumen?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Saldos, transferencias sugeridas y pagos del mes (<c>GET /api/liquidacion</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<LiquidacionResponse> ObtenerLiquidacionAsync(string mes, CancellationToken ct = default) =>
        ObtenerAsync<LiquidacionResponse>($"api/liquidacion?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>
    /// Resumen y liquidación del mes en una sola petición (<c>GET /api/resumen-liquidacion</c>): lo que pinta la pestaña Resumen, sin
    /// leer dos veces los gastos y los miembros del mes.
    /// </summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ResumenYLiquidacionResponse> ObtenerResumenYLiquidacionAsync(string mes, CancellationToken ct = default) =>
        ObtenerAsync<ResumenYLiquidacionResponse>($"api/resumen-liquidacion?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Registra un pago entre dos miembros para saldar un mes (<c>POST /api/pagos-liquidacion</c>).</summary>
    /// <param name="peticion">Mes (día 1), pagador, receptor, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<PagoLiquidacionDto> CrearPagoLiquidacionAsync(CrearPagoLiquidacionRequest peticion, CancellationToken ct = default) =>
        EnviarAsync<PagoLiquidacionDto>(HttpMethod.Post, "api/pagos-liquidacion", peticion, ct);

    /// <summary>Elimina un pago de liquidación (<c>DELETE /api/pagos-liquidacion/{id}</c>).</summary>
    /// <param name="id">Pago a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarPagoLiquidacionAsync(Guid id, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/pagos-liquidacion/{id}", ct);

    /// <summary>Meses cerrados del hogar (<c>GET /api/cierres-mes</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<MesCerradoDto>> ListarCierresMesAsync(CancellationToken ct = default) =>
        ObtenerListaAsync<MesCerradoDto>("api/cierres-mes", ct);

    /// <summary>Cierra un mes; puede cualquier miembro activo (<c>POST /api/cierres-mes</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MesCerradoDto> CerrarMesAsync(string mes, CancellationToken ct = default) =>
        EnviarAsync<MesCerradoDto>(HttpMethod.Post, "api/cierres-mes", new CerrarMesRequest(mes), ct);

    /// <summary>Reabre un mes cerrado; solo admin (<c>DELETE /api/cierres-mes/{mes}</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ReabrirMesAsync(string mes, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cierres-mes/{Uri.EscapeDataString(mes)}", ct);

    /// <summary>Clave pública VAPID para suscribir este navegador a los avisos (<c>GET /api/notificaciones/clave-publica</c>). 404 si el servidor no tiene avisos configurados.</summary>
    /// <param name="ct">Token de cancelación.</param>
    public async Task<string> ObtenerClaveAvisosAsync(CancellationToken ct = default) =>
        (await ObtenerAsync<ClavePublicaPushResponse>("api/notificaciones/clave-publica", ct)).ClavePublica;

    /// <summary>Registra la suscripción de este dispositivo a los avisos (<c>POST /api/notificaciones/suscripcion</c>).</summary>
    /// <param name="peticion">Endpoint y claves que devuelve el navegador al suscribirse.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task GuardarSuscripcionAvisosAsync(SuscripcionPushRequest peticion, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Post, "api/notificaciones/suscripcion", ct, peticion);

    /// <summary>Quita la suscripción de este dispositivo (<c>DELETE /api/notificaciones/suscripcion</c>). No falla si ya no existía.</summary>
    /// <param name="endpoint">Endpoint de la suscripción que se quita.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task QuitarSuscripcionAvisosAsync(string endpoint, CancellationToken ct = default) =>
        EnviarSinRespuestaAsync(HttpMethod.Delete, "api/notificaciones/suscripcion", ct, new BajaSuscripcionPushRequest(endpoint));

    private Task<T> ObtenerAsync<T>(string ruta, CancellationToken ct) => EnviarAsync<T>(HttpMethod.Get, ruta, null, ct);

    /// <summary>Lectura de una lista que casi no cambia: pasa por la caché, que la reutiliza entre pantallas durante <see cref="CacheLecturas.Vigencia"/>.</summary>
    private Task<List<T>> ObtenerListaAsync<T>(string ruta, CancellationToken ct) =>
        _cache is null
            ? ObtenerAsync<List<T>>(ruta, ct)
            : _cache.ListaAsync(ruta, () => ObtenerAsync<List<T>>(ruta, CancellationToken.None), ct);

    /// <summary>
    /// Tras cualquier escritura (también si falla: un 409 o un 404 suelen indicar que lo cacheado ya no es cierto) se olvidan las
    /// listas guardadas que ese cambio puede afectar; sin esto la pantalla siguiente enseñaría la categoría borrada o el miembro nuevo sin él.
    /// </summary>
    private void InvalidarTrasEscritura(HttpMethod metodo, string ruta)
    {
        if (_cache is null || metodo == HttpMethod.Get) return;
        // Aceptar una invitación o unirse por enlace añade o vincula un miembro; crear o borrar un hogar cambia de contexto por completo.
        if (ruta.StartsWith("api/invitaciones", StringComparison.Ordinal) || ruta.StartsWith("api/enlaces-grupo", StringComparison.Ordinal)
            || ruta.StartsWith("api/hogar", StringComparison.Ordinal))
        {
            _cache.Limpiar();
            return;
        }

        var afectadas = RutasEnCache.Where(r => ruta.StartsWith(r, StringComparison.Ordinal)).ToList();
        // Borrar un perfil en uso pasa sus categorías a otro perfil (?moverA=): la lista de categorías guardada apuntaría a un perfil
        // que ya no existe y el formulario de gastos lo propondría como reparto por defecto.
        if (metodo == HttpMethod.Delete && ruta.StartsWith("api/perfiles", StringComparison.Ordinal) && !afectadas.Contains("api/categorias"))
            afectadas.Add("api/categorias");
        if (afectadas.Count > 0) _cache.Invalidar(afectadas.ToArray());
    }

    private async Task EnviarSinRespuestaAsync(HttpMethod metodo, string ruta, CancellationToken ct, object? cuerpo = null)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) peticion.Content = JsonContent.Create(cuerpo, cuerpo.GetType());
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "No se pudo conectar con el servidor. Comprueba tu conexión.");
        }
        finally
        {
            InvalidarTrasEscritura(metodo, ruta);
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                var (mensaje, enUso) = await LeerErrorAsync(respuesta, ct);
                throw new ApiException(respuesta.StatusCode, mensaje) { EnUso = enUso };
            }
        }
    }

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) peticion.Content = JsonContent.Create(cuerpo, cuerpo.GetType());

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "No se pudo conectar con el servidor. Comprueba tu conexión.");
        }
        finally
        {
            InvalidarTrasEscritura(metodo, ruta);
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                var (mensaje, enUso) = await LeerErrorAsync(respuesta, ct);
                throw new ApiException(respuesta.StatusCode, mensaje) { EnUso = enUso };
            }

            try
            {
                return await respuesta.Content.ReadFromJsonAsync<T>(ct)
                       ?? throw new ApiException(HttpStatusCode.BadGateway, "Respuesta vacía del servidor.");
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                // Típico de un Api:CoreUrl que apunta a otra cosa (p. ej. al propio front, que responde con index.html).
                throw new ApiException(HttpStatusCode.BadGateway,
                    "El servidor no ha respondido con datos válidos. Comprueba que Api:CoreUrl apunta a Core.Api y que está en marcha.");
            }
        }
    }

    /// <summary>Mensaje del cuerpo <c>{ "error": ... }</c> (o uno por código) y si trae <c>enUso: true</c>, en una sola lectura del cuerpo.</summary>
    private static async Task<(string Mensaje, bool EnUso)> LeerErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (cuerpo.ValueKind == JsonValueKind.Object && cuerpo.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } texto)
                return (texto, cuerpo.TryGetProperty("enUso", out var u) && u.ValueKind == JsonValueKind.True);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Sin cuerpo JSON: se usa el mensaje por código.
        }

        return (MensajePorCodigo(respuesta.StatusCode), false);
    }

    private static string MensajePorCodigo(HttpStatusCode codigo) =>
        codigo switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión ha caducado. Vuelve a iniciar sesión.",
            HttpStatusCode.Forbidden => "No tienes permiso para hacer esto.",
            HttpStatusCode.NotFound => "No se ha encontrado lo que buscas.",
            HttpStatusCode.Conflict => "La operación no se puede realizar en el estado actual.",
            _ => "Ha ocurrido un error en el servidor.",
        };
}
