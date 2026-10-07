namespace MiParte.Contracts;

/// <summary>Valor de un miembro en un perfil: porcentaje o partes según el modo.</summary>
/// <param name="MiembroId">Miembro al que corresponde el valor.</param>
/// <param name="Valor">Porcentaje (modo "porcentaje") o número de partes (modo "partes").</param>
public record PerfilDetalleDto(Guid MiembroId, decimal Valor);

/// <summary>Modo en texto: "porcentaje", "partes", "cuenta_comun" o "individual".</summary>
/// <param name="Id">Identificador del perfil.</param>
/// <param name="Nombre">Nombre del perfil.</param>
/// <param name="Modo">Modo de reparto: "porcentaje", "partes", "cuenta_comun" o "individual".</param>
/// <param name="Detalle">Valor por miembro; vacío en los modos "cuenta_comun" e "individual".</param>
public record PerfilRepartoDto(Guid Id, string Nombre, string Modo, IReadOnlyList<PerfilDetalleDto> Detalle);

/// <summary>Alta y edición de perfil (PUT reemplaza nombre, modo y detalle).</summary>
/// <param name="Nombre">Nombre del perfil (máximo 100 caracteres).</param>
/// <param name="Modo">Modo de reparto: "porcentaje", "partes", "cuenta_comun" o "individual".</param>
/// <param name="Detalle">Valor por miembro; obligatorio en "porcentaje" y "partes", debe estar vacío u omitirse en "cuenta_comun" e "individual" (si no, la API responde 400).</param>
public record GuardarPerfilRequest(string Nombre, string Modo, IReadOnlyList<PerfilDetalleDto>? Detalle);
