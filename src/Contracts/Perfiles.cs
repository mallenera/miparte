namespace MiParte.Contracts;

/// <summary>Valor de un miembro en un perfil: porcentaje o partes según el modo.</summary>
public record PerfilDetalleDto(Guid MiembroId, decimal Valor);

/// <summary>Modo en texto: "porcentaje", "partes", "ingresos" o "individual".</summary>
public record PerfilRepartoDto(Guid Id, string Nombre, string Modo, IReadOnlyList<PerfilDetalleDto> Detalle);

/// <summary>Alta y edición de perfil (PUT reemplaza nombre, modo y detalle).</summary>
public record GuardarPerfilRequest(string Nombre, string Modo, IReadOnlyList<PerfilDetalleDto>? Detalle);
