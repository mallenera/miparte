namespace MiParte.Contracts;

/// <summary>Alta/edición de un gasto. El reparto lo calcula el servidor según el perfil.</summary>
public record GastoRequest(
    DateOnly Fecha, decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId, string? Concepto);

public record RepartoGastoDto(Guid MiembroId, decimal ImporteAsumido);

public record GastoResponse(
    Guid Id, DateOnly Fecha, decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId,
    string? Concepto, Guid? GastoRecurrenteId, IReadOnlyList<RepartoGastoDto> Repartos);

/// <summary>Plantilla de gasto mensual. DiaMes entre 1 y 28.</summary>
public record GastoRecurrenteRequest(
    decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId, short DiaMes, string? Concepto, bool Activo = true);

public record GastoRecurrenteResponse(
    Guid Id, decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId, short DiaMes,
    string? Concepto, bool Activo);

/// <summary>Resultado de POST /api/gastos-recurrentes/generar: gastos creados y plantillas que ya tenían gasto ese mes.</summary>
public record GenerarRecurrentesResponse(string Mes, int Creados, int YaExistentes);
