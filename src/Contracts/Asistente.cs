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
/// <param name="Borrador">Gasto propuesto en esta respuesta, si la hubo; la persona debe confirmarlo.</param>
public record ChatResponse(string Respuesta, IReadOnlyList<string> Herramientas, BorradorGastoDto? Borrador = null);

/// <summary>
/// Borrador de gasto que el asistente propone desde el chat. No está guardado: la persona lo revisa y lo confirma
/// (<c>POST /api/chat/confirmar-gasto</c>). Categoría y perfil son los marcados como predeterminados para gastos.
/// </summary>
/// <param name="Importe">Importe en euros (mayor que 0, máximo 2 decimales).</param>
/// <param name="Concepto">Concepto del gasto.</param>
/// <param name="Fecha">Fecha del gasto, indicada por la persona (el asistente no la supone).</param>
/// <param name="Categoria">Nombre de la categoría predeterminada que se usará.</param>
/// <param name="Perfil">Nombre del perfil de reparto predeterminado que se usará.</param>
public record BorradorGastoDto(decimal Importe, string Concepto, DateOnly Fecha, string Categoria, string Perfil);

/// <summary>
/// Confirmación de un borrador (<c>POST /api/chat/confirmar-gasto</c>). Categoría y perfil no viajan: el servidor usa
/// los marcados como predeterminados en ese momento. <paramref name="PagadoPor"/> lo elige la persona al confirmar.
/// </summary>
/// <param name="Importe">Importe en euros.</param>
/// <param name="Concepto">Concepto del gasto.</param>
/// <param name="Fecha">Fecha del gasto.</param>
/// <param name="PagadoPor">Miembro adulto activo que paga el gasto.</param>
public record ConfirmarGastoRequest(decimal Importe, string Concepto, DateOnly Fecha, Guid PagadoPor);
