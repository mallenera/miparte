using System.Globalization;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Cuenta común, ahorro y ajustes del hogar: activaciones, modos de liquidación, periodo propio y eliminación del hogar.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Estado de la cuenta común al final de un mes (<c>GET /api/cuenta-comun</c>).</summary>
    /// <param name="mes">Mes en formato YYYY-MM.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CuentaComunResponse> ObtenerCuentaComunAsync(string mes, CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<CuentaComunResponse>($"api/cuenta-comun?mes={Uri.EscapeDataString(mes)}", ct);

    /// <summary>Extracto de la cuenta común entre dos fechas incluidas, línea a línea (<c>GET /api/cuenta-comun/movimientos</c>).</summary>
    /// <param name="desde">Primer día incluido.</param>
    /// <param name="hasta">Último día incluido.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MovimientosCuentaComunResponse> MovimientosCuentaComunAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<MovimientosCuentaComunResponse>(
            $"api/cuenta-comun/movimientos?desde={desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&hasta={hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", ct);

    /// <summary>Activa o desactiva la cuenta común del hogar; solo un admin (<c>PUT /api/cuenta-comun/activacion</c>).</summary>
    /// <param name="activa">true para usar la cuenta común.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ActivarCuentaComunAsync(bool activa, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<System.Text.Json.JsonElement>(HttpMethod.Put, "api/cuenta-comun/activacion", new ActivarCuentaComunRequest(activa), ct);

    /// <summary>
    /// Elimina el hogar actual con todos sus datos, sin vuelta atrás (<c>DELETE /api/hogar</c>); exige el permiso <c>hogar.eliminar</c> y el nombre exacto del hogar.
    /// </summary>
    /// <param name="nombre">Nombre del hogar, como confirmación.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarHogarAsync(string nombre, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/hogar?nombre={Uri.EscapeDataString(nombre)}", ct);

    /// <summary>Activa o desactiva el ahorro del hogar; solo un admin y con la cuenta común activada (<c>PUT /api/cuenta-comun/ahorro/activacion</c>).</summary>
    /// <param name="activo">true para usar el ahorro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ActivarAhorroAsync(bool activo, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<System.Text.Json.JsonElement>(HttpMethod.Put, "api/cuenta-comun/ahorro/activacion", new ActivarAhorroRequest(activo), ct);

    /// <summary>Activa o desactiva los gastos recurrentes del hogar; solo con <c>hogar.funciones</c> (<c>PUT /api/hogar/recurrentes</c>).</summary>
    /// <param name="activas">true para crear plantillas y generar gastos.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task ActivarRecurrentesAsync(bool activas, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<System.Text.Json.JsonElement>(HttpMethod.Put, "api/hogar/recurrentes", new ActivarRecurrentesRequest(activas), ct);

    /// <summary>Cambia el modo de liquidación del hogar: <c>mensual</c> o <c>ciclos</c> (<c>PUT /api/hogar/liquidacion</c>, permiso <c>hogar.funciones</c>).</summary>
    /// <param name="modo">Modo nuevo (ver <c>ModosLiquidacion</c>).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ModoLiquidacionResponse> ActivarLiquidacionAsync(string modo, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ModoLiquidacionResponse>(HttpMethod.Put, "api/hogar/liquidacion", new ModoLiquidacionRequest(modo), ct);

    /// <summary>Fija con qué frecuencia aportan los miembros a la cuenta común (<c>PUT /api/hogar/aportaciones</c>, permiso <c>hogar.funciones</c>).</summary>
    /// <param name="frecuencia">Una de las frecuencias de <c>FrecuenciasAportacion</c>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<FrecuenciaAportacionResponse> FijarFrecuenciaAportacionAsync(string frecuencia, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<FrecuenciaAportacionResponse>(HttpMethod.Put, "api/hogar/aportaciones", new FijarFrecuenciaAportacionRequest(frecuencia), ct);

    /// <summary>
    /// Cierra el periodo propio del hogar (viaje o evento): congela sus gastos y genera la liquidación final (<c>POST /api/hogar/cerrar-periodo</c>,
    /// permiso <c>mes.cerrar</c>).
    /// </summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CierrePeriodoResponse> CerrarPeriodoAsync(CancellationToken ct = default) =>
        _nucleo.EnviarAsync<CierrePeriodoResponse>(HttpMethod.Post, "api/hogar/cerrar-periodo", null, ct);

    /// <summary>Cierra el ciclo de liquidación actual: marca los gastos pendientes como liquidados (<c>POST /api/liquidacion/ciclos</c>, permiso <c>pagos.registrar</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<CicloLiquidacionDto> CerrarCicloLiquidacionAsync(CancellationToken ct = default) =>
        _nucleo.EnviarAsync<CicloLiquidacionDto>(HttpMethod.Post, "api/liquidacion/ciclos", null, ct);

    /// <summary>Ciclos de liquidación cerrados, del más reciente al más antiguo (<c>GET /api/liquidacion/ciclos</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<CicloLiquidacionDto>> ListarCiclosLiquidacionAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerListaAsync<CicloLiquidacionDto>("api/liquidacion/ciclos", ct);

    /// <summary>Fija lo que aporta un adulto a la cuenta común desde un mes (<c>PUT /api/cuenta-comun/aportaciones</c>).</summary>
    /// <param name="peticion">Adulto, mes de inicio (día 1) e importe.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<AportacionCuentaDto> FijarAportacionAsync(FijarAportacionRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<AportacionCuentaDto>(HttpMethod.Put, "api/cuenta-comun/aportaciones", peticion, ct);

    /// <summary>Fija el saldo inicial de la cuenta para gastos a una fecha, sustituyendo el anterior (<c>PUT /api/cuenta-comun/saldo-inicial</c>).</summary>
    /// <param name="peticion">Fecha e importe (0 o más).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<SaldoInicialDto> FijarSaldoInicialAsync(FijarSaldoInicialRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<SaldoInicialDto>(HttpMethod.Put, "api/cuenta-comun/saldo-inicial", peticion, ct);

    /// <summary>Quita el saldo inicial de la cuenta (<c>DELETE /api/cuenta-comun/saldo-inicial</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarSaldoInicialAsync(CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, "api/cuenta-comun/saldo-inicial", ct);

    /// <summary>Registra un movimiento de la deuda de una persona con la cuenta común: adelanto, devolución o compensación (<c>POST /api/cuenta-comun/deudas</c>).</summary>
    /// <param name="peticion">Movimiento a registrar.</param>
    /// <param name="ct">Cancelación.</param>
    public Task<MovimientoDeudaCuentaDto> CrearDeudaAsync(CrearDeudaCuentaRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<MovimientoDeudaCuentaDto>(HttpMethod.Post, "api/cuenta-comun/deudas", peticion, ct);

    /// <summary>Borra un movimiento de deuda con la cuenta común (<c>DELETE /api/cuenta-comun/deudas/{id}</c>).</summary>
    /// <param name="id">Movimiento a borrar.</param>
    /// <param name="ct">Cancelación.</param>
    public Task EliminarDeudaAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/deudas/{id}", ct);

    /// <summary>Registra un reembolso de la cuenta común a quien adelantó gastos (<c>POST /api/cuenta-comun/reembolsos</c>).</summary>
    /// <param name="peticion">Miembro, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<ReembolsoCuentaDto> CrearReembolsoAsync(CrearReembolsoRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<ReembolsoCuentaDto>(HttpMethod.Post, "api/cuenta-comun/reembolsos", peticion, ct);

    /// <summary>Elimina un reembolso (<c>DELETE /api/cuenta-comun/reembolsos/{id}</c>).</summary>
    /// <param name="id">Reembolso a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarReembolsoAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/reembolsos/{id}", ct);

    /// <summary>Ingresos a la cuenta común fuera de aportaciones y ahorro, del más reciente al más antiguo (<c>GET /api/cuenta-comun/ingresos</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<IngresoCuentaComunDto>> ListarIngresosCuentaComunAsync(CancellationToken ct = default) =>
        _nucleo.ObtenerAsync<List<IngresoCuentaComunDto>>("api/cuenta-comun/ingresos", ct); // sin caché: nada la invalida tras un ingreso

    /// <summary>Registra dinero que entra a la cuenta común, solo como histórico (<c>POST /api/cuenta-comun/ingresos</c>).</summary>
    /// <param name="peticion">Miembro que ingresa, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<IngresoCuentaComunDto> CrearIngresoCuentaComunAsync(CrearIngresoCuentaComunRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<IngresoCuentaComunDto>(HttpMethod.Post, "api/cuenta-comun/ingresos", peticion, ct);

    /// <summary>Elimina un ingreso a la cuenta común (<c>DELETE /api/cuenta-comun/ingresos/{id}</c>).</summary>
    /// <param name="id">Ingreso a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarIngresoCuentaComunAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/ingresos/{id}", ct);

    /// <summary>Registra un ingreso aparte en el ahorro de la cuenta común (<c>POST /api/cuenta-comun/depositos-ahorro</c>).</summary>
    /// <param name="peticion">Miembro, importe, fecha y concepto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<DepositoAhorroDto> CrearDepositoAhorroAsync(CrearDepositoAhorroRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<DepositoAhorroDto>(HttpMethod.Post, "api/cuenta-comun/depositos-ahorro", peticion, ct);

    /// <summary>Elimina un ingreso de ahorro (<c>DELETE /api/cuenta-comun/depositos-ahorro/{id}</c>).</summary>
    /// <param name="id">Ingreso a eliminar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task EliminarDepositoAhorroAsync(Guid id, CancellationToken ct = default) =>
        _nucleo.EnviarSinRespuestaAsync(HttpMethod.Delete, $"api/cuenta-comun/depositos-ahorro/{id}", ct);

}
