namespace MiParte.Auth;

public class SupabaseAuthOptions
{
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

    public string Audience { get; set; } = "authenticated";

    public string? EmisorEfectivo =>
        !string.IsNullOrWhiteSpace(Issuer) ? Issuer
        : !string.IsNullOrWhiteSpace(Url) ? Url.TrimEnd('/') + "/auth/v1"
        : null;

    public string? JwksUrl =>
        string.IsNullOrWhiteSpace(Url) ? null : Url.TrimEnd('/') + "/auth/v1/.well-known/jwks.json";
}
