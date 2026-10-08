namespace MiParte.Contracts;

/// <summary>Mensaje de la conversación con el asistente.</summary>
/// <param name="Rol"><c>user</c> (la persona) o <c>assistant</c> (el asistente).</param>
/// <param name="Texto">Contenido del mensaje.</param>
public record MensajeChatDto(string Rol, string Texto);

/// <summary>Conversación enviada a <c>POST /api/chat</c>: el historial reciente terminado en una pregunta de la persona.</summary>
/// <param name="Mensajes">Mensajes en orden cronológico; el último debe ser de <c>user</c>.</param>
public record ChatRequest(IReadOnlyList<MensajeChatDto> Mensajes);

/// <summary>Respuesta del asistente.</summary>
/// <param name="Respuesta">Texto de la respuesta.</param>
/// <param name="Herramientas">Nombres de las funciones de consulta que se usaron para responder (transparencia para la persona).</param>
public record ChatResponse(string Respuesta, IReadOnlyList<string> Herramientas);
