using System.Text.Json;
using MiParte.Web.Demo;

namespace MiParte.Web.Autenticacion;

/// <summary>
/// Mantiene la sesión de Supabase: la restaura de <c>localStorage</c>, la renueva antes de que caduque y la descarta
/// al cerrar sesión. El token se guarda en <c>localStorage</c> (patrón habitual de Supabase en SPA): en el navegador
/// es accesible a cualquier script de la página, por lo que no se debe cargar JavaScript de terceros no confiable.
/// </summary>
public sealed class ServicioSesion
{
    private const string ClaveAlmacen = "miparte.sesion";
    private const string ClaveVerificador = "miparte.pkce";
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

    /// <summary>Indica si la sesión actual es la del modo demo local (datos en memoria, sin servidor).</summary>
    public bool EsDemoLocal => CuentaDemo.EsLocal(SesionActual?.UserId);

    /// <summary>Indica si la sesión es de alguna de las dos demos: la local o la cuenta demo real compartida.</summary>
    public bool EsDemo => EsDemoLocal || string.Equals(SesionActual?.Email, CuentaDemo.Correo, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Entra en el modo demo local: una sesión ficticia que no caduca y que <see cref="ManejadorDemo"/> reconoce para
    /// contestar desde memoria. Se guarda como cualquier sesión, así que recargar la página no saca de la demo.
    /// </summary>
    public async Task IniciarDemoAsync()
    {
        await InicializarAsync();
        await EstablecerAsync(new SesionSupabase("demo", "demo", DateTimeOffset.MaxValue, CuentaDemo.IdUsuarioLocal, "Demo"));
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

    /// <summary>
    /// Prepara el login con Google (PKCE): guarda en este navegador un verificador aleatorio y devuelve la URL de Supabase
    /// con su <c>code_challenge</c>. Así el retorno solo se acepta en el navegador que inició el flujo (evita el CSRF de login).
    /// </summary>
    /// <param name="redirectTo">URL absoluta a la que vuelve el usuario.</param>
    public async Task<Uri> UrlGoogleAsync(string redirectTo)
    {
        var verificador = Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await _almacen.EscribirAsync(ClaveVerificador, verificador);
        var desafio = Base64Url(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verificador)));
        return _auth.UrlGoogle(redirectTo, desafio);
    }

    /// <summary>
    /// Completa el login con Google a partir de la URL de retorno (<c>?code=…</c>, o <c>error=…</c>), en forma de query y/o fragmento.
    /// Devuelve false si no trae ni código ni error (visita normal).
    /// </summary>
    /// <param name="parametros">Query y/o fragmento de la URL, con o sin <c>?</c> / <c>#</c>.</param>
    /// <exception cref="AuthException">Google o Supabase devolvieron un error, no hay un login iniciado en este navegador o el código no es válido.</exception>
    public async Task<bool> CompletarGoogleAsync(string? parametros)
    {
        var valores = new Dictionary<string, string>();
        foreach (var par in (parametros ?? "").TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            if (i > 0) valores[Uri.UnescapeDataString(par[..i].TrimStart('?', '#'))] = Uri.UnescapeDataString(par[(i + 1)..].Replace('+', ' '));
        }

        var hayError = valores.TryGetValue("error", out var error);
        if (!hayError && !valores.ContainsKey("code")) return false;

        // El verificador es de un solo uso: se consume antes de seguir, tenga éxito o no el canje.
        var verificador = await _almacen.LeerAsync(ClaveVerificador);
        await _almacen.EliminarAsync(ClaveVerificador);

        if (hayError)
            throw new AuthException(error == "access_denied" ? "Has cancelado el acceso con Google." : "No se pudo iniciar sesión con Google.", 400);
        if (string.IsNullOrEmpty(verificador))
            throw new AuthException("El acceso con Google no se inició en este navegador. Vuelve a intentarlo.", 400);

        await InicializarAsync();
        await EstablecerAsync(await _auth.IntercambiarCodigoAsync(valores["code"], verificador));
        return true;
    }

    private static string Base64Url(byte[] datos) => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Cierra la sesión en Supabase (mejor esfuerzo) y la descarta localmente.</summary>
    public async Task CerrarSesionAsync()
    {
        var sesion = SesionActual;
        await DescartarAsync();
        // La sesión del modo demo local no existe en Supabase.
        if (sesion is not null && !CuentaDemo.EsLocal(sesion.UserId)) await _auth.CerrarSesionAsync(sesion.AccessToken);
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
