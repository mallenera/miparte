namespace MiParte.Assistant.Api;

/// <summary>
/// Opciones del asistente (sección <c>Asistente</c>; variables <c>Asistente__Modelo</c>, etc.). La clave de la API
/// de Anthropic no está aquí a propósito: solo se lee de la variable de entorno <c>ANTHROPIC_API_KEY</c>.
/// </summary>
public sealed class OpcionesAsistente
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Asistente";

    /// <summary>Modelo de Anthropic. Configurable para cambiarlo sin recompilar.</summary>
    public string Modelo { get; set; } = "claude-sonnet-5-5";

    /// <summary>URL base de Core.Api, de la que se consultan los datos del hogar con el JWT de la persona.</summary>
    public string CoreUrl { get; set; } = "http://localhost:5001";

    /// <summary>Tope de tokens de salida por turno del modelo.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Vueltas máximas del bucle modelo, herramientas, modelo en una sola pregunta.</summary>
    public int MaxIteraciones { get; set; } = 5;

    /// <summary>Llamadas a herramientas máximas por pregunta (suma de todas las vueltas).</summary>
    public int MaxLlamadasHerramienta { get; set; } = 8;

    /// <summary>Mensajes máximos de historial aceptados por petición (los más recientes).</summary>
    public int MaxMensajes { get; set; } = 20;

    /// <summary>Longitud máxima de cada mensaje de la persona, en caracteres.</summary>
    public int MaxCaracteresMensaje { get; set; } = 1500;

    /// <summary>Preguntas por minuto y usuario (cada una cuesta tokens de pago).</summary>
    public int MensajesPorMinuto { get; set; } = 10;

    /// <summary>Tamaño máximo del cuerpo de la petición, en bytes.</summary>
    public long MaxCuerpoBytes { get; set; } = 64 * 1024;
}
