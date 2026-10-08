namespace MiParte.Contracts;

/// <summary>Un mes cerrado: sus gastos están congelados.</summary>
/// <param name="Mes">Mes cerrado, en formato YYYY-MM.</param>
/// <param name="CerradoEn">Instante en que se cerró.</param>
/// <param name="CerradoPor">Nombre del miembro que lo cerró, si se conoce.</param>
public record MesCerradoDto(string Mes, DateTimeOffset CerradoEn, string? CerradoPor);

/// <summary>POST /api/cierres-mes.</summary>
/// <param name="Mes">Mes que se cierra, en formato YYYY-MM.</param>
public record CerrarMesRequest(string Mes);
