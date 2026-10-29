using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Application.CuentaComun;

/// <summary>
/// Acceso a datos de la cuenta común y el ahorro del hogar actual (el implementador aplica el aislamiento por hogar).
/// Los métodos «Guardar» y «Eliminar» persisten en el momento.
/// </summary>
public interface IRepositorioCuentaComun
{
    /// <summary>Si el hogar tiene activada la cuenta común.</summary>
    Task<bool> CuentaActivaAsync(CancellationToken ct);

    /// <summary>Si el hogar tiene activado el ahorro.</summary>
    Task<bool> AhorroActivoAsync(CancellationToken ct);

    /// <summary>Activa o desactiva la cuenta común del hogar (no borra datos).</summary>
    Task EstablecerCuentaActivaAsync(bool activa, CancellationToken ct);

    /// <summary>Saldo inicial de la cuenta para gastos, o null si no se ha fijado.</summary>
    Task<SaldoInicialCuenta?> SaldoInicialAsync(CancellationToken ct);

    /// <summary>Fija el saldo inicial de la cuenta para gastos, o lo quita si <paramref name="saldo"/> es null.</summary>
    Task EstablecerSaldoInicialAsync(SaldoInicialCuenta? saldo, CancellationToken ct);

    /// <summary>Activa o desactiva el ahorro del hogar (no borra datos).</summary>
    Task EstablecerAhorroActivoAsync(bool activo, CancellationToken ct);

    /// <summary>Histórico de la cuenta con el detalle del mes que empieza en <paramref name="inicio"/>.</summary>
    Task<HistoricoCuentaDetallado> HistoricoDetalladoAsync(DateOnly inicio, CancellationToken ct);

    /// <summary>Todo lo que mueve dinero en el banco de la cuenta hasta <paramref name="hasta"/> (incluido), con las aportaciones completas.</summary>
    Task<DatosMovimientosBanco> MovimientosBancoAsync(DateOnly hasta, CancellationToken ct);

    /// <summary>Si el miembro existe en el hogar, esté activo o no.</summary>
    Task<bool> ExisteMiembroAsync(Guid id, CancellationToken ct);

    /// <summary>Si el miembro existe en el hogar, está activo y es adulto.</summary>
    Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct);

    /// <summary>Todas las aportaciones del hogar, en la forma que usa el cálculo (cada una con la frecuencia del hogar).</summary>
    Task<IReadOnlyList<AportacionVigente>> AportacionesAsync(CancellationToken ct);

    /// <summary>Con qué frecuencia aportan los miembros a la cuenta común.</summary>
    Task<FrecuenciaAportacion> FrecuenciaAsync(CancellationToken ct);

    /// <summary>Fija con qué frecuencia aportan los miembros a la cuenta común.</summary>
    Task EstablecerFrecuenciaAsync(FrecuenciaAportacion frecuencia, CancellationToken ct);

    /// <summary>Todos los depósitos de ahorro; <paramref name="excluir"/> deja fuera uno.</summary>
    Task<IReadOnlyList<DepositoDeAhorro>> DepositosAsync(Guid? excluir, CancellationToken ct);

    /// <summary>
    /// Todo lo que ya salió del ahorro: retiradas, gastos pagados desde él y las deudas con el ahorro, estas últimas con signo
    /// (un adelanto sale; una devolución o una compensación entran, como un uso negativo).
    /// </summary>
    Task<IReadOnlyList<UsoDeAhorro>> UsosDeAhorroAsync(CancellationToken ct);

    /// <summary>Datos para calcular el ahorro disponible.</summary>
    Task<DatosAhorro> DatosAhorroAsync(CancellationToken ct);

    /// <summary>Total de gastos a cargo de la cuenta que adelantó el miembro, del fondo de Hogar o del ahorro (<paramref name="desdeAhorro"/>).</summary>
    Task<decimal> GastadoACargoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct);

    /// <summary>Total que la cuenta ya ha reembolsado al miembro desde ese fondo (<paramref name="desdeAhorro"/>).</summary>
    Task<decimal> ReembolsadoAsync(Guid miembroId, bool desdeAhorro, CancellationToken ct);

    /// <summary>Fija la aportación del miembro desde el mes, sustituyendo la de ese mes si ya existía.</summary>
    Task<AportacionCuenta> FijarAportacionAsync(Guid hogarId, Guid miembroId, DateOnly desde, decimal importe, decimal ahorro, CancellationToken ct);

    /// <summary>Guarda un reembolso nuevo.</summary>
    Task GuardarReembolsoAsync(ReembolsoCuenta reembolso, CancellationToken ct);

    /// <summary>Borra un reembolso; false si no existe en el hogar.</summary>
    Task<bool> EliminarReembolsoAsync(Guid id, CancellationToken ct);

    /// <summary>Todos los movimientos de deuda de las personas con la cuenta común o su ahorro.</summary>
    Task<IReadOnlyList<MovimientoDeuda>> DeudasAsync(CancellationToken ct);

    /// <summary>Guarda un movimiento de deuda nuevo (adelanto, devolución o compensación).</summary>
    Task GuardarDeudaAsync(MovimientoDeudaCuenta deuda, CancellationToken ct);

    /// <summary>Un movimiento de deuda con su identificador y concepto, o null si no existe en el hogar.</summary>
    Task<MovimientoDeuda?> DeudaAsync(Guid id, CancellationToken ct);

    /// <summary>Borra un movimiento de deuda; false si no existe en el hogar.</summary>
    Task<bool> EliminarDeudaAsync(Guid id, CancellationToken ct);

    /// <summary>Guarda un ingreso a la cuenta común nuevo.</summary>
    Task GuardarIngresoAsync(IngresoCuentaComun ingreso, CancellationToken ct);

    /// <summary>Ingresos a la cuenta común del hogar, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<IngresoCuentaComun>> IngresosAsync(CancellationToken ct);

    /// <summary>Borra un ingreso a la cuenta común; false si no existe en el hogar.</summary>
    Task<bool> EliminarIngresoAsync(Guid id, CancellationToken ct);

    /// <summary>Guarda un depósito de ahorro nuevo.</summary>
    Task GuardarDepositoAsync(DepositoAhorro deposito, CancellationToken ct);

    /// <summary>Borra un depósito de ahorro; false si no existe en el hogar.</summary>
    Task<bool> EliminarDepositoAsync(Guid id, CancellationToken ct);

    /// <summary>Si existe el depósito en el hogar.</summary>
    Task<bool> ExisteDepositoAsync(Guid id, CancellationToken ct);

    /// <summary>Guarda una retirada de ahorro nueva.</summary>
    Task GuardarRetiradaAsync(RetiradaAhorro retirada, CancellationToken ct);

    /// <summary>Borra una retirada de ahorro; false si no existe en el hogar.</summary>
    Task<bool> EliminarRetiradaAsync(Guid id, CancellationToken ct);
}
