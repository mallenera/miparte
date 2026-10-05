namespace MiParte.Auth;

/// <summary>Opciones de validación de los JWT de Supabase Auth (sección "Supabase").</summary>
public class SupabaseAuthOptions
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Supabase";

    /// <summary>URL del proyecto, p. ej. https://abcd.supabase.co</summary>
    public string? Url { get; set; }

    /// <summary>
    /// Secreto HS256 legado. Si se define, solo se aceptan tokens HS256 firmados con él.
    /// Si no, solo se aceptan tokens asimétricos validados con el JWKS del proyecto.
    /// </summary>
    public string? JwtSecret { get; set; }

    /// <summary>Emisor esperado. Por defecto {Url}/auth/v1.</summary>
    public string? Issuer { get; set; }

    /// <summary>Audiencia esperada del token. Por defecto "authenticated".</summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>Emisor esperado: <see cref="Issuer"/> si está definido; si no, {Url}/auth/v1; null si no hay ninguno.</summary>
    public string? EmisorEfectivo =>
        !string.IsNullOrWhiteSpace(Issuer) ? Issuer
        : !string.IsNullOrWhiteSpace(Url) ? Url.TrimEnd('/') + "/auth/v1"
        : null;

    /// <summary>URL del JWKS del proyecto ({Url}/auth/v1/.well-known/jwks.json), o null si no hay <see cref="Url"/>.</summary>
    public string? JwksUrl =>
        string.IsNullOrWhiteSpace(Url) ? null : Url.TrimEnd('/') + "/auth/v1/.well-known/jwks.json";
}
