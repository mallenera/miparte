namespace MiParte.Contracts;

public record CrearHogarRequest(string NombreHogar, string NombreMiembro);

public record HogarResumen(Guid Id, string Nombre);
