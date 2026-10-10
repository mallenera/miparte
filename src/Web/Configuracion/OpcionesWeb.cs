namespace MiParte.Web.Configuracion;

/// <summary>Configuración del front leída de <c>wwwroot/appsettings.json</c> (visible en el navegador: sin secretos).</summary>
public sealed class OpcionesWeb
{
    /// <summary>URL del proyecto Supabase, p. ej. <c>https://xxxx.supabase.co</c>.</summary>
    public string SupabaseUrl { get; init; } = "";

    /// <summary>Anon key (clave pública) de Supabase.</summary>
    public string SupabaseAnonKey { get; init; } = "";

    /// <summary>URL base de Core.Api.</summary>
    public string CoreUrl { get; init; } = "";

    /// <summary>URL base de Assistant.Api (chat). Opcional: vacía, el asistente flotante avisa de que no está configurado.</summary>
    public string AssistantUrl { get; init; } = "";

    /// <summary>Indica si hay un asistente al que preguntar.</summary>
    public bool AsistenteConfigurado => !string.IsNullOrWhiteSpace(AssistantUrl);

    /// <summary>Indica si están definidos los valores mínimos para autenticarse y hablar con Core.Api.</summary>
    public bool EstaConfigurada =>
        !string.IsNullOrWhiteSpace(SupabaseUrl) && !string.IsNullOrWhiteSpace(SupabaseAnonKey) && !string.IsNullOrWhiteSpace(CoreUrl);

    /// <summary>Lee las opciones de las secciones <c>Supabase</c> y <c>Api</c> de la configuración.</summary>
    /// <param name="configuracion">Configuración del host de Blazor.</param>
    public static OpcionesWeb Leer(IConfiguration configuracion) => new()
    {
        SupabaseUrl = configuracion["Supabase:Url"] ?? "",
        SupabaseAnonKey = configuracion["Supabase:AnonKey"] ?? "",
        CoreUrl = configuracion["Api:CoreUrl"] ?? "",
        AssistantUrl = configuracion["Api:AssistantUrl"] ?? "",
    };
}
