namespace MiParte.Contracts;

public record CrearHogarRequest(string NombreHogar, string NombreMiembro);

public record HogarResumen(Guid Id, string Nombre);

/// <summary>Respuesta de GET /api/yo: usuario, sus hogares y el hogar actual (null si no se puede determinar).</summary>
public record YoResponse(string? UserId, IReadOnlyList<HogarResumen> Hogares, HogarResumen? HogarActual);
