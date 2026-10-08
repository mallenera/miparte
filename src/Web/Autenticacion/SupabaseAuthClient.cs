using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MiParte.Web.Autenticacion;

/// <summary>Cliente mínimo de la API REST de Supabase Auth (GoTrue); su <see cref="HttpClient"/> trae base <c>{url}/auth/v1/</c> y la cabecera <c>apikey</c>.</summary>
public sealed class SupabaseAuthClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _reloj;

    /// <summary>Crea el cliente.</summary>
    /// <param name="http">Cliente HTTP ya configurado con la URL de Supabase Auth y la anon key.</param>
    /// <param name="reloj">Reloj usado para calcular la caducidad del token.</param>
    public SupabaseAuthClient(HttpClient http, TimeProvider reloj)
    {
        _http = http;
        _reloj = reloj;
    }

    /// <summary>Inicia sesión con correo y contraseña.</summary>
    /// <param name="email">Correo.</param>
    /// <param name="password">Contraseña.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="AuthException">Credenciales inválidas u otro error de Supabase.</exception>
    public async Task<SesionSupabase> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        var json = await EnviarAsync("token?grant_type=password", new { email, password }, null, ct);
        return LeerSesion(json) ?? throw new AuthException("Respuesta de autenticación no válida.", 502);
    }

    /// <summary>Crea la cuenta. Devuelve null si Supabase exige confirmar el correo antes de iniciar sesión.</summary>
    /// <param name="email">Correo.</param>
    /// <param name="password">Contraseña.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="AuthException">Correo ya registrado, contraseña débil u otro error.</exception>
    public async Task<SesionSupabase?> RegistrarAsync(string email, string password, CancellationToken ct = default)
    {
        var json = await EnviarAsync("signup", new { email, password }, null, ct);
        return LeerSesion(json);
    }

    /// <summary>Confirma la cuenta con el código de 6 dígitos que Supabase envió por correo; devuelve la sesión ya iniciada.</summary>
    /// <param name="email">Correo con el que se registró.</param>
    /// <param name="codigo">Código recibido por correo.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="AuthException">Código incorrecto o caducado.</exception>
    public async Task<SesionSupabase> VerificarCodigoAsync(string email, string codigo, CancellationToken ct = default)
    {
        var json = await EnviarAsync("verify", new { type = "signup", email, token = codigo }, null, ct);
        return LeerSesion(json) ?? throw new AuthException("Respuesta de autenticación no válida.", 502);
    }

    /// <summary>Reenvía el correo con el código de confirmación.</summary>
    /// <param name="email">Correo con el que se registró.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="AuthException">Demasiados reenvíos u otro error.</exception>
    public async Task ReenviarCodigoAsync(string email, CancellationToken ct = default) =>
        await EnviarAsync("resend", new { type = "signup", email }, null, ct);

    /// <summary>Renueva la sesión con el refresh token.</summary>
    /// <param name="refreshToken">Refresh token vigente.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="AuthException">Refresh token inválido o ya usado.</exception>
    public async Task<SesionSupabase> RefrescarAsync(string refreshToken, CancellationToken ct = default)
    {
        var json = await EnviarAsync("token?grant_type=refresh_token", new { refresh_token = refreshToken }, null, ct);
        return LeerSesion(json) ?? throw new AuthException("Respuesta de autenticación no válida.", 502);
    }

    /// <summary>Cierra la sesión en Supabase; es de mejor esfuerzo y no lanza si falla.</summary>
    /// <param name="accessToken">Token de la sesión a cerrar.</param>
    /// <param name="ct">Token de cancelación.</param>
    public async Task CerrarSesionAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            await EnviarAsync("logout", new { }, accessToken, ct);
        }
        catch (Exception e) when (e is AuthException or HttpRequestException or TaskCanceledException)
        {
            // Si falla, la sesión local se descarta igualmente.
        }
    }

    private async Task<JsonElement> EnviarAsync(string ruta, object cuerpo, string? bearer, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        if (bearer is not null) peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var respuesta = await _http.SendAsync(peticion, ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        if (!respuesta.IsSuccessStatusCode) throw new AuthException(Traducir(texto), (int)respuesta.StatusCode);
        if (string.IsNullOrWhiteSpace(texto)) return default;
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }

    private SesionSupabase? LeerSesion(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("access_token", out var acceso) || acceso.GetString() is not { Length: > 0 } token
            || !json.TryGetProperty("refresh_token", out var refresco) || refresco.GetString() is not { Length: > 0 } refrescoToken
            || !json.TryGetProperty("user", out var usuario) || usuario.ValueKind != JsonValueKind.Object
            || !usuario.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } userId)
            return null;

        var segundos = json.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var s) ? s : 3600;
        var email = usuario.TryGetProperty("email", out var e) ? e.GetString() : null;
        return new SesionSupabase(token, refrescoToken, _reloj.GetUtcNow().AddSeconds(segundos), userId, email);
    }

    private static string Traducir(string cuerpo)
    {
        string? codigo = null, mensaje = null;
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            var raiz = doc.RootElement;
            string? Texto(string nombre) =>
                raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;
            codigo = Texto("error_code") ?? Texto("error");
            mensaje = Texto("msg") ?? Texto("error_description") ?? Texto("message");
        }
        catch (JsonException)
        {
            // Cuerpo que no es JSON: se usa el mensaje genérico.
        }

        return codigo switch
        {
            "invalid_credentials" or "invalid_grant" => "Correo o contraseña incorrectos.",
            "email_not_confirmed" => "Confirma tu correo antes de iniciar sesión.",
            "user_already_exists" or "email_exists" => "Ya existe una cuenta con ese correo.",
            "otp_expired" => "El código es incorrecto o ha caducado. Pide uno nuevo.",
            "weak_password" => "La contraseña es demasiado débil (mínimo 6 caracteres).",
            "over_request_rate_limit" or "over_email_send_rate_limit" => "Demasiados intentos. Espera un momento.",
            "signup_disabled" => "El registro está desactivado.",
            _ => mensaje ?? "No se pudo completar la operación de autenticación.",
        };
    }
}
