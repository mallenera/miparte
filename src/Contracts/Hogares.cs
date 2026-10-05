namespace MiParte.Contracts;

/// <summary>Alta de un hogar; quien lo crea queda como su primer miembro (admin y adulto).</summary>
/// <param name="NombreHogar">Nombre del hogar.</param>
/// <param name="NombreMiembro">Nombre del miembro que crea el hogar.</param>
public record CrearHogarRequest(string NombreHogar, string NombreMiembro);

/// <summary>Datos básicos de un hogar.</summary>
/// <param name="Id">Identificador del hogar.</param>
/// <param name="Nombre">Nombre del hogar.</param>
public record HogarResumen(Guid Id, string Nombre);

/// <summary>Respuesta de GET /api/yo: usuario, sus hogares y el hogar actual (null si no se puede determinar).</summary>
/// <param name="UserId">Identificador del usuario (claim "sub" del JWT), o null si no consta.</param>
/// <param name="Hogares">Hogares a los que pertenece el usuario.</param>
/// <param name="HogarActual">Hogar resuelto para la petición, o null si no se puede determinar.</param>
public record YoResponse(string? UserId, IReadOnlyList<HogarResumen> Hogares, HogarResumen? HogarActual);
