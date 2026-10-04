namespace MiParte.Contracts;

/// <summary>Miembro del hogar. Tipo: "adulto" | "a_cargo". Rol: "admin" | "miembro".</summary>
/// <param name="Id">Identificador del miembro.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Tipo">Tipo de miembro: "adulto" o "a_cargo".</param>
/// <param name="ResponsableId">Adulto responsable (solo para "a_cargo"), o null.</param>
/// <param name="Activo">Si el miembro está activo en el hogar.</param>
/// <param name="Rol">Rol en el hogar: "admin" o "miembro".</param>
/// <param name="Vinculado">Si el miembro está vinculado a un usuario con cuenta.</param>
public record MiembroDto(
    Guid Id, string Nombre, string Tipo, Guid? ResponsableId, bool Activo, string Rol, bool Vinculado);

/// <summary>Alta de una persona sin cuenta. Un "a_cargo" necesita ResponsableId (adulto activo del hogar).</summary>
/// <param name="Nombre">Nombre de la persona (máximo 100 caracteres).</param>
/// <param name="Tipo">Tipo de miembro: "adulto" o "a_cargo".</param>
/// <param name="ResponsableId">Adulto activo responsable; obligatorio para "a_cargo".</param>
public record CrearMiembroRequest(string Nombre, string Tipo, Guid? ResponsableId = null);

/// <summary>Campos null = sin cambios. Rol ("admin" | "miembro") solo lo cambia un admin.</summary>
/// <param name="Nombre">Nuevo nombre, o null para no cambiarlo.</param>
/// <param name="Activo">Nuevo estado activo, o null para no cambiarlo.</param>
/// <param name="ResponsableId">Nuevo responsable (solo "a_cargo"), o null para no cambiarlo.</param>
/// <param name="Rol">Nuevo rol ("admin" o "miembro"), o null para no cambiarlo.</param>
public record ActualizarMiembroRequest(
    string? Nombre = null, bool? Activo = null, Guid? ResponsableId = null, string? Rol = null);

/// <summary>MiembroId opcional: miembro existente sin usuario al que se vinculará quien acepte.</summary>
/// <param name="MiembroId">Miembro existente sin usuario al que se vinculará; si es null, quien acepte entra como nuevo adulto.</param>
public record CrearInvitacionRequest(Guid? MiembroId = null);

/// <summary>El Token en claro solo se devuelve en esta respuesta; en base de datos solo queda su hash.</summary>
/// <param name="Id">Identificador de la invitación.</param>
/// <param name="Token">Token en claro que se entrega a quien se invita.</param>
/// <param name="CaducaEn">Fecha y hora de caducidad de la invitación.</param>
public record InvitacionCreada(Guid Id, string Token, DateTimeOffset CaducaEn);

/// <summary>Aceptación de una invitación con su token.</summary>
/// <param name="Token">Token en claro de la invitación.</param>
/// <param name="Nombre">Nombre del nuevo miembro; obligatorio si la invitación no apunta a un miembro existente.</param>
public record AceptarInvitacionRequest(string Token, string? Nombre = null);
