namespace MiParte.Contracts;

/// <summary>Miembro del hogar. Tipo: "adulto" | "a_cargo". Rol: "admin" | "miembro".</summary>
public record MiembroDto(
    Guid Id, string Nombre, string Tipo, Guid? ResponsableId, bool Activo, string Rol, bool Vinculado);

/// <summary>Alta de una persona sin cuenta. Un "a_cargo" necesita ResponsableId (adulto activo del hogar).</summary>
public record CrearMiembroRequest(string Nombre, string Tipo, Guid? ResponsableId = null);

/// <summary>Campos null = sin cambios. Rol ("admin" | "miembro") solo lo cambia un admin.</summary>
public record ActualizarMiembroRequest(
    string? Nombre = null, bool? Activo = null, Guid? ResponsableId = null, string? Rol = null);

/// <summary>MiembroId opcional: miembro existente sin usuario al que se vinculará quien acepte.</summary>
public record CrearInvitacionRequest(Guid? MiembroId = null);

/// <summary>El Token en claro solo se devuelve en esta respuesta; en base de datos solo queda su hash.</summary>
public record InvitacionCreada(Guid Id, string Token, DateTimeOffset CaducaEn);

public record AceptarInvitacionRequest(string Token, string? Nombre = null);
