namespace MiParte.Contracts;

public record ImporteMiembroDto(Guid MiembroId, decimal Importe);

public record ResumenMiembroDto(Guid MiembroId, string Nombre, decimal Pagado, decimal Asumido);

public record ResumenCategoriaDto(
    Guid CategoriaId, string Nombre, decimal Total, IReadOnlyList<ImporteMiembroDto> PorMiembro);

/// <summary>GET /api/resumen?mes=YYYY-MM. Queda = ingresos - gastos.</summary>
public record ResumenMensualResponse(
    string Mes, decimal IngresosTotales, decimal GastosTotales, decimal Queda,
    IReadOnlyList<ResumenMiembroDto> Miembros, IReadOnlyList<ResumenCategoriaDto> Categorias);

/// <summary>Saldo positivo = le deben; negativo = debe. Ya descuenta los pagos registrados.</summary>
public record SaldoMiembroDto(Guid MiembroId, string Nombre, decimal Saldo);

public record TransferenciaDto(Guid De, Guid A, decimal Importe);

public record PagoLiquidacionDto(
    Guid Id, DateOnly Mes, Guid DeMiembroId, Guid AMiembroId, decimal Importe, DateOnly Fecha, string? Concepto);

public record LiquidacionResponse(
    string Mes, IReadOnlyList<SaldoMiembroDto> Saldos, IReadOnlyList<TransferenciaDto> Transferencias,
    IReadOnlyList<PagoLiquidacionDto> Pagos);

/// <summary>Mes debe ser el día 1. Fecha por defecto: hoy (UTC).</summary>
public record CrearPagoLiquidacionRequest(
    DateOnly Mes, Guid DeMiembroId, Guid AMiembroId, decimal Importe, DateOnly? Fecha, string? Concepto);
