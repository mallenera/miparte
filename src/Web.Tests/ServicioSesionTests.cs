using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Tests;

public class ServicioSesionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    private static string Token(string acceso, string refresco, int segundos = 3600) =>
        $$$"""{"access_token":"{{{acceso}}}","refresh_token":"{{{refresco}}}","expires_in":{{{segundos}}},"user":{"id":"u1","email":"a@b.com"}}""";

    private static (ServicioSesion servicio, AlmacenMemoria almacen, FakeTimeProvider reloj) Crear(ManejadorFalso manejador)
    {
        var reloj = new FakeTimeProvider(Ahora);
        var almacen = new AlmacenMemoria();
        var auth = new SupabaseAuthClient(new HttpClient(manejador) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        return (new ServicioSesion(auth, almacen, reloj), almacen, reloj);
    }

    [Fact]
    public async Task Iniciar_sesion_guarda_la_sesion_y_avisa()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, Token("a1", "r1")));
        var avisos = 0;
        servicio.SesionCambiada += () => avisos++;

        await servicio.IniciarSesionAsync("a@b.com", "secreto");

        Assert.Equal("a1", servicio.SesionActual!.AccessToken);
        Assert.True(almacen.Datos.ContainsKey("miparte.sesion"));
        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task Restaura_la_sesion_guardada_si_sigue_vigente()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, Token("nuevo", "r2")));
        almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("viejo", "r1", Ahora.AddHours(1), "u1", "a@b.com"));

        Assert.Equal("viejo", await servicio.ObtenerTokenAsync());
    }

    [Fact]
    public async Task Renueva_el_token_cuando_esta_a_punto_de_caducar()
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.OK, Token("nuevo", "r2"));
        var (servicio, almacen, _) = Crear(manejador);
        almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("viejo", "r1", Ahora.AddSeconds(30), "u1", "a@b.com"));

        Assert.Equal("nuevo", await servicio.ObtenerTokenAsync());
        Assert.Equal("https://x.supabase.co/auth/v1/token?grant_type=refresh_token", manejador.Peticiones[0].RequestUri!.ToString());
        Assert.Contains("nuevo", almacen.Datos["miparte.sesion"]);
    }

    [Fact]
    public async Task Si_el_refresh_falla_descarta_la_sesion()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.BadRequest, """{"error_code":"invalid_grant"}"""));
        almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("viejo", "r1", Ahora.AddSeconds(-5), "u1", null));

        Assert.Null(await servicio.ObtenerTokenAsync());
        Assert.Null(servicio.SesionActual);
        Assert.False(almacen.Datos.ContainsKey("miparte.sesion"));
    }

    [Fact]
    public async Task Sin_red_conserva_la_sesion_mientras_el_token_no_haya_caducado()
    {
        var (servicio, almacen, _) = Crear(new ManejadorFalso(_ => throw new HttpRequestException("sin red")));
        almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("viejo", "r1", Ahora.AddSeconds(30), "u1", null));

        Assert.Equal("viejo", await servicio.ObtenerTokenAsync());
        Assert.NotNull(servicio.SesionActual);
    }

    [Fact]
    public async Task Cerrar_sesion_borra_el_almacen()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, Token("a1", "r1")));
        await servicio.IniciarSesionAsync("a@b.com", "secreto");

        await servicio.CerrarSesionAsync();

        Assert.Null(servicio.SesionActual);
        Assert.Empty(almacen.Datos);
    }

    [Fact]
    public async Task Un_almacen_corrupto_se_ignora()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, Token("a1", "r1")));
        almacen.Datos["miparte.sesion"] = "no es json";

        Assert.Null(await servicio.ObtenerTokenAsync());
        Assert.Empty(almacen.Datos);
    }

    [Fact]
    public async Task Verificar_codigo_inicia_y_guarda_la_sesion()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, Token("a1", "r1")));

        await servicio.VerificarCodigoAsync("a@b.com", "123456");

        Assert.Equal("a1", servicio.SesionActual!.AccessToken);
        Assert.True(almacen.Datos.ContainsKey("miparte.sesion"));
    }

    private static string Jwt(string sub, string email)
    {
        static string B64(string t) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(t)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{}")}.{B64($$"""{"sub":"{{sub}}","email":"{{email}}"}""")}.firma";
    }

    [Fact]
    public async Task Google_con_fragmento_valido_inicia_sesion()
    {
        var (servicio, almacen, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, "{}"));

        var ok = await servicio.CompletarGoogleAsync($"#access_token={Jwt("u9", "g@gmail.com")}&refresh_token=r9&expires_in=3600&token_type=bearer");

        Assert.True(ok);
        Assert.Equal("u9", servicio.SesionActual!.UserId);
        Assert.Equal("g@gmail.com", servicio.SesionActual.Email);
        Assert.True(almacen.Datos.ContainsKey("miparte.sesion"));
    }

    [Fact]
    public async Task Google_sin_tokens_no_hace_nada()
    {
        var (servicio, _, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, "{}"));

        Assert.False(await servicio.CompletarGoogleAsync(""));
        Assert.Null(servicio.SesionActual);
    }

    [Fact]
    public async Task Google_cancelado_lanza_error_en_espanol()
    {
        var (servicio, _, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, "{}"));

        var e = await Assert.ThrowsAsync<AuthException>(() => servicio.CompletarGoogleAsync("#error=access_denied&error_description=x"));

        Assert.Contains("cancelado", e.Message);
    }

    [Fact]
    public async Task Google_con_token_corrupto_se_rechaza()
    {
        var (servicio, _, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<AuthException>(() => servicio.CompletarGoogleAsync("#access_token=basura&refresh_token=r"));
        Assert.Null(servicio.SesionActual);
    }

    [Fact]
    public void Url_de_google_apunta_a_authorize_con_el_retorno_codificado()
    {
        var (servicio, _, _) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, "{}"));

        Assert.Equal("https://x.supabase.co/auth/v1/authorize?provider=google&redirect_to=http%3A%2F%2Flocalhost%3A5211%2Flogin",
            servicio.UrlGoogle("http://localhost:5211/login").ToString());
    }
}
