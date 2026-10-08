namespace MiParte.Contracts;

/// <summary>Alta/edición de un gasto. El reparto lo calcula el servidor según el perfil.</summary>
/// <param name="Fecha">Fecha del gasto.</param>
/// <param name="Importe">Importe total del gasto (positivo, máximo 2 decimales).</param>
/// <param name="CategoriaId">Categoría del gasto.</param>
/// <param name="PagadoPor">Miembro (adulto activo) que pagó el gasto; null si lo paga directamente la cuenta común (solo con perfil «cuenta común»).</param>
/// <param name="PerfilRepartoId">Perfil de reparto con el que se divide el gasto.</param>
/// <param name="Concepto">Descripción opcional del gasto.</param>
/// <param name="PagadoDesdeAhorro">Si se paga con el ahorro de la cuenta común (descuenta del ahorro disponible); exige <paramref name="PagadoPor"/> nulo y el perfil «cuenta común».</param>
public record GastoRequest(
    DateOnly Fecha, decimal Importe, Guid CategoriaId, Guid? PagadoPor, Guid PerfilRepartoId, string? Concepto,
    bool PagadoDesdeAhorro = false);

/// <summary>Parte de un gasto asumida por un miembro.</summary>
/// <param name="MiembroId">Miembro que asume la parte.</param>
/// <param name="ImporteAsumido">Importe del gasto que le corresponde, calculado por el servidor.</param>
public record RepartoGastoDto(Guid MiembroId, decimal ImporteAsumido);

/// <summary>Gasto guardado junto con su reparto.</summary>
/// <param name="Id">Identificador del gasto.</param>
/// <param name="Fecha">Fecha del gasto.</param>
/// <param name="Importe">Importe total del gasto.</param>
/// <param name="CategoriaId">Categoría del gasto.</param>
/// <param name="PagadoPor">Miembro que pagó el gasto; null si lo pagó directamente la cuenta común.</param>
/// <param name="PerfilRepartoId">Perfil de reparto usado al crear el gasto.</param>
/// <param name="Concepto">Descripción opcional del gasto.</param>
/// <param name="GastoRecurrenteId">Plantilla recurrente que lo generó, o null si es manual.</param>
/// <param name="Repartos">Importe asumido por cada miembro, guardado al crear el gasto; vacío si lo asume la cuenta común.</param>
/// <param name="ACargoCuentaComun">Si lo asume la cuenta común (perfil «cuenta común»): sin reparto entre personas ni deuda.</param>
/// <param name="PagadoDesdeAhorro">Si se pagó con el ahorro de la cuenta común.</param>
public record GastoResponse(
    Guid Id, DateOnly Fecha, decimal Importe, Guid CategoriaId, Guid? PagadoPor, Guid PerfilRepartoId,
    string? Concepto, Guid? GastoRecurrenteId, IReadOnlyList<RepartoGastoDto> Repartos, bool ACargoCuentaComun = false,
    bool PagadoDesdeAhorro = false);

/// <summary>Plantilla de gasto mensual. DiaMes entre 1 y 28.</summary>
/// <param name="Importe">Importe del gasto que se generará cada mes.</param>
/// <param name="CategoriaId">Categoría de los gastos generados.</param>
/// <param name="PagadoPor">Miembro (adulto activo) que paga el gasto.</param>
/// <param name="PerfilRepartoId">Perfil de reparto de los gastos generados.</param>
/// <param name="DiaMes">Día del mes (1 a 28) en que se genera el gasto.</param>
/// <param name="Concepto">Descripción opcional del gasto.</param>
/// <param name="Activo">Si la plantilla está activa; por defecto true.</param>
public record GastoRecurrenteRequest(
    decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId, short DiaMes, string? Concepto, bool Activo = true);

/// <summary>Plantilla de gasto mensual guardada.</summary>
/// <param name="Id">Identificador de la plantilla.</param>
/// <param name="Importe">Importe del gasto que se genera cada mes.</param>
/// <param name="CategoriaId">Categoría de los gastos generados.</param>
/// <param name="PagadoPor">Miembro que paga el gasto.</param>
/// <param name="PerfilRepartoId">Perfil de reparto de los gastos generados.</param>
/// <param name="DiaMes">Día del mes (1 a 28) en que se genera el gasto.</param>
/// <param name="Concepto">Descripción opcional del gasto.</param>
/// <param name="Activo">Si la plantilla está activa.</param>
public record GastoRecurrenteResponse(
    Guid Id, decimal Importe, Guid CategoriaId, Guid PagadoPor, Guid PerfilRepartoId, short DiaMes,
    string? Concepto, bool Activo);

/// <summary>Resultado de POST /api/gastos-recurrentes/generar: gastos creados y plantillas que ya tenían gasto ese mes.</summary>
/// <param name="Mes">Mes procesado, en formato YYYY-MM.</param>
/// <param name="Creados">Número de gastos creados.</param>
/// <param name="YaExistentes">Número de plantillas que ya tenían gasto en ese mes.</param>
public record GenerarRecurrentesResponse(string Mes, int Creados, int YaExistentes);
