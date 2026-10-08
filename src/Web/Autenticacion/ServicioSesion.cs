using System.Text.Json;

namespace MiParte.Web.Autenticacion;

/// <summary>
/// Mantiene la sesión de Supabase: la restaura de <c>localStorage</c>, la renueva antes de que caduque y la descarta
/// al cerrar sesión. El token se guarda en <c>localStorage</c> (patrón habitual de Supabase en SPA): en el navegador
/// es accesible a cualquier script de la página, por lo que no se debe cargar JavaScript de terceros no confiable.
/// </summary>
public sealed class ServicioSesion
{
    private const string ClaveAlmacen = "miparte.sesion";
    private static readonly TimeSpan MargenRenovacion = TimeSpan.FromSeconds(60);

    private readonly SupabaseAuthClient _auth;
    private readonly IAlmacenLocal _almacen;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _bloqueo = new(1, 1);
    private Task? _inicializacion;

    /// <summary>Se dispara cuando la sesión cambia (inicio, renovación, cierre).</summary>
    public event Action? SesionCambiada;

    /// <summary>Sesión actual, o null si no hay usuario autenticado.</summary>
    public SesionSupabase? SesionActual { get; private set; }

    /// <summary>Crea el servicio.</summary>
    /// <param name="auth">Cliente de Supabase Auth.</param>
    /// <param name="almacen">Almacén persistente del navegador.</param>
    /// <param name="reloj">Reloj para comprobar la caducidad.</param>
    public ServicioSesion(SupabaseAuthClient auth, IAlmacenLocal almacen, TimeProvider reloj)
    {
        _auth = auth;
        _almacen = almacen;
        _reloj = reloj;
    }

    /// <summary>Restaura la sesión guardada (una sola vez); si está caducada intenta renovarla.</summary>
    public Task InicializarAsync() => _inicializacion ??= RestaurarAsync();

    /// <summary>Inicia sesión y la guarda.</summary>
    /// <param name="email">Correo.</param>
    /// <param name="password">Contraseña.</param>
    /// <exception cref="AuthException">Credenciales inválidas u otro error.</exception>
    public async Task IniciarSesionAsync(string email, string password)
    {
        await InicializarAsync();
        await EstablecerAsync(await _auth.IniciarSesionAsync(email, password));
    }

    /// <summary>Crea la cuenta; devuelve true si ya queda con sesión iniciada, false si hay que confirmar el correo.</summary>
    /// <param name="email">Correo.</param>
    /// <param name="password">Contraseña.</param>
    /// <exception cref="AuthException">Correo ya registrado, contraseña débil u otro error.</exception>
    public async Task<bool> RegistrarAsync(string email, string password)
    {
        await InicializarAsync();
        var sesion = await _auth.RegistrarAsync(email, password);
        if (sesion is null) return false;
        await EstablecerAsync(sesion);
        return true;
    }

    /// <summary>Confirma la cuenta con el código recibido por correo e inicia sesión.</summary>
    /// <param name="email">Correo.</param>
    /// <param name="codigo">Código de 6 dígitos.</param>
    /// <exception cref="AuthException">Código incorrecto o caducado.</exception>
    public async Task VerificarCodigoAsync(string email, string codigo)
    {
        await InicializarAsync();
        await EstablecerAsync(await _auth.VerificarCodigoAsync(email, codigo));
    }

    /// <summary>Reenvía el código de confirmación al correo.</summary>
    /// <param name="email">Correo.</param>
    public async Task ReenviarCodigoAsync(string email) => await _auth.ReenviarCodigoAsync(email);

    /// <summary>URL de Supabase para iniciar sesión con Google.</summary>
    /// <param name="redirectTo">URL absoluta a la que vuelve el usuario.</param>
    public Uri UrlGoogle(string redirectTo) => _auth.UrlGoogle(redirectTo);

    /// <summary>
    /// Completa el login con Google a partir del fragmento de la URL de retorno (<c>#access_token=…</c>).
    /// Devuelve false si el fragmento no trae sesión ni error (visita normal).
    /// </summary>
    /// <param name="fragmento">Fragmento de la URL, con o sin <c>#</c>.</param>
    /// <exception cref="AuthException">Google o Supabase devolvieron un error, o los tokens no son válidos.</exception>
    public async Task<bool> CompletarGoogleAsync(string? fragmento)
    {
        var valores = new Dictionary<string, string>();
        foreach (var par in (fragmento ?? "").TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            if (i > 0) valores[Uri.UnescapeDataString(par[..i])] = Uri.UnescapeDataString(par[(i + 1)..].Replace('+', ' '));
        }

        if (valores.ContainsKey("error"))
            throw new AuthException(valores["error"] == "access_denied"
                ? "Has cancelado el acceso con Google."
                : "No se pudo iniciar sesión con Google.", 400);
        if (!valores.TryGetValue("access_token", out var acceso) || !valores.TryGetValue("refresh_token", out var refresco)) return false;

        await InicializarAsync();
        var segundos = valores.TryGetValue("expires_in", out var exp) && int.TryParse(exp, out var s) ? s : 3600;
        await EstablecerAsync(_auth.CrearSesion(acceso, refresco, segundos));
        return true;
    }

    /// <summary>Cierra la sesión en Supabase (mejor esfuerzo) y la descarta localmente.</summary>
    public async Task CerrarSesionAsync()
    {
        var sesion = SesionActual;
        await DescartarAsync();
        if (sesion is not null) await _auth.CerrarSesionAsync(sesion.AccessToken);
    }

    /// <summary>Descarta la sesión local sin avisar a Supabase (p. ej. tras un 401 de la API).</summary>
    public async Task DescartarAsync()
    {
        SesionActual = null;
        await _almacen.EliminarAsync(ClaveAlmacen);
        SesionCambiada?.Invoke();
    }

    /// <summary>Devuelve un access token válido, renovándolo si caduca pronto; null si no hay sesión utilizable.</summary>
    public async Task<string?> ObtenerTokenAsync()
    {
        await InicializarAsync();
        var sesion = SesionActual;
        if (sesion is null) return null;
        if (!sesion.CaducaPronto(_reloj.GetUtcNow(), MargenRenovacion)) return sesion.AccessToken;

        await _bloqueo.WaitAsync();
        try
        {
            sesion = SesionActual;
            if (sesion is null) return null;
            if (!sesion.CaducaPronto(_reloj.GetUtcNow(), MargenRenovacion)) return sesion.AccessToken;
            return await RenovarAsync(sesion);
        }
        finally
        {
            _bloqueo.Release();
        }
    }

    private async Task RestaurarAsync()
    {
        var json = await _almacen.LeerAsync(ClaveAlmacen);
        if (string.IsNullOrWhiteSpace(json)) return;
        SesionSupabase? guardada = null;
        try { guardada = JsonSerializer.Deserialize<SesionSupabase>(json); }
        catch (JsonException) { }

        if (guardada is null)
        {
            await _almacen.EliminarAsync(ClaveAlmacen);
            return;
        }

        SesionActual = guardada;
        if (guardada.CaducaPronto(_reloj.GetUtcNow(), MargenRenovacion)) await RenovarAsync(guardada);
        else SesionCambiada?.Invoke();
    }

    private async Task<string?> RenovarAsync(SesionSupabase sesion)
    {
        try
        {
            var nueva = await _auth.RefrescarAsync(sesion.RefreshToken);
            await EstablecerAsync(nueva);
            return nueva.AccessToken;
        }
        catch (AuthException)
        {
            await DescartarAsync();
            return null;
        }
        catch (HttpRequestException)
        {
            // Sin red: se conserva la sesión y se usa el token mientras no haya caducado de verdad.
            return sesion.CaducaPronto(_reloj.GetUtcNow(), TimeSpan.Zero) ? null : sesion.AccessToken;
        }
    }

    private async Task EstablecerAsync(SesionSupabase sesion)
    {
        SesionActual = sesion;
        await _almacen.EscribirAsync(ClaveAlmacen, JsonSerializer.Serialize(sesion));
        SesionCambiada?.Invoke();
    }
}
