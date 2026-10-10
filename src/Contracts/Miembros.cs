namespace MiParte.Contracts;

/// <summary>Miembro del hogar. Tipo: "adulto" | "a_cargo". Rol: "admin" | "miembro".</summary>
/// <param name="Id">Identificador del miembro.</param>
/// <param name="Nombre">Nombre del miembro.</param>
/// <param name="Tipo">Tipo de miembro: "adulto" o "a_cargo".</param>
/// <param name="ResponsableId">Adulto responsable (solo para "a_cargo"), o null.</param>
/// <param name="Activo">Si el miembro está activo en el hogar.</param>
/// <param name="Rol">Rol en el hogar: "admin" o "miembro".</param>
/// <param name="Vinculado">Si el miembro está vinculado a un usuario con cuenta.</param>
/// <param name="EsYo">Si el miembro es el usuario autenticado que hace la petición (se informa en GET, PUT y DELETE; en el alta es false).</param>
/// <param name="Permisos">Claves de los permisos efectivos del miembro: la lista propia que se le haya asignado o, si no la tiene, la plantilla de su rol (admin todas, miembro las de por defecto); los adultos sin cuenta y los a cargo, ninguna.</param>
public record MiembroDto(
    Guid Id, string Nombre, string Tipo, Guid? ResponsableId, bool Activo, string Rol, bool Vinculado, bool EsYo = false,
    IReadOnlyList<string>? Permisos = null);

/// <summary>Alta de una persona sin cuenta. Un "a_cargo" necesita ResponsableId (adulto activo del hogar).</summary>
/// <param name="Nombre">Nombre de la persona (máximo 100 caracteres).</param>
/// <param name="Tipo">Tipo de miembro: "adulto" o "a_cargo".</param>
/// <param name="ResponsableId">Adulto activo responsable; obligatorio para "a_cargo".</param>
public record CrearMiembroRequest(string Nombre, string Tipo, Guid? ResponsableId = null);

/// <summary>Campos null = sin cambios. Rol ("admin" | "miembro") y Permisos solo los cambia quien tenga el permiso permisos.gestionar; el resto de campos, miembros.gestionar (cualquiera puede renombrarse).</summary>
/// <param name="Nombre">Nuevo nombre, o null para no cambiarlo.</param>
/// <param name="Activo">Nuevo estado activo, o null para no cambiarlo.</param>
/// <param name="ResponsableId">Nuevo responsable (solo "a_cargo"), o null para no cambiarlo.</param>
/// <param name="Rol">Nuevo rol ("admin" o "miembro"), o null para no cambiarlo.</param>
/// <param name="Permisos">Lista completa de claves de permiso que tendrá un adulto con cuenta, admins incluidos (la cambia quien tenga el permiso <c>permisos.gestionar</c>), o null para no cambiarla. Cambiar <c>Rol</c> sin indicarla devuelve al miembro a la plantilla del rol nuevo.</param>
public record ActualizarMiembroRequest(
    string? Nombre = null, bool? Activo = null, Guid? ResponsableId = null, string? Rol = null,
    IReadOnlyList<string>? Permisos = null);

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
