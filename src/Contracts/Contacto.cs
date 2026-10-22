namespace MiParte.Contracts;

/// <summary>Mensaje del formulario de contacto (<c>POST /api/contacto</c>).</summary>
/// <param name="Motivo">Clave del motivo: <c>duda</c>, <c>mejora</c> u <c>otro</c>.</param>
/// <param name="Email">Correo de quien escribe, para poder contestarle.</param>
/// <param name="Mensaje">Texto del mensaje (10-4000 caracteres).</param>
/// <param name="Nombre">Nombre de quien escribe; opcional.</param>
/// <param name="Web">Campo trampa oculto: el formulario lo envía vacío; si llega relleno, la API no envía nada pero responde 204.</param>
public record ContactoRequest(string Motivo, string Email, string Mensaje, string? Nombre = null, string? Web = null);
