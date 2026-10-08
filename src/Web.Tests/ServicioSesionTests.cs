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

    private static (ServicioSesion servicio, AlmacenMemoria almacen, ManejadorFalso manejador) CrearGoogle(string respuesta = null!)
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.OK, respuesta ?? Token("g1", "gr1"));
        var (servicio, almacen, _) = Crear(manejador);
        return (servicio, almacen, manejador);
    }

    [Fact]
    public async Task Url_de_google_lleva_el_code_challenge_del_verificador_guardado()
    {
        var (servicio, almacen, _) = CrearGoogle();

        var url = await servicio.UrlGoogleAsync("http://localhost:5211/login");

        var verificador = almacen.Datos["miparte.pkce"];
        var desafio = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verificador)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.StartsWith("https://x.supabase.co/auth/v1/authorize?provider=google&redirect_to=http%3A%2F%2Flocalhost%3A5211%2Flogin", url.ToString());
        Assert.EndsWith($"&code_challenge={desafio}&code_challenge_method=s256", url.ToString());
        Assert.True(verificador.Length >= 43);
    }

    [Fact]
    public async Task Google_con_codigo_canjea_con_el_verificador_y_lo_consume()
    {
        var (servicio, almacen, manejador) = CrearGoogle();
        await servicio.UrlGoogleAsync("http://localhost:5211/login");
        var verificador = almacen.Datos["miparte.pkce"];

        var ok = await servicio.CompletarGoogleAsync("?code=abc123&otro=1");

        Assert.True(ok);
        Assert.Equal("g1", servicio.SesionActual!.AccessToken);
        Assert.False(almacen.Datos.ContainsKey("miparte.pkce"));
        Assert.True(almacen.Datos.ContainsKey("miparte.sesion"));
        Assert.EndsWith("/auth/v1/token?grant_type=pkce", manejador.Peticiones[0].RequestUri!.ToString());
        Assert.NotEmpty(verificador);
    }

    [Fact]
    public async Task Google_con_codigo_sin_verificador_se_rechaza_sin_llamar_a_supabase()
    {
        var (servicio, _, manejador) = CrearGoogle();

        await Assert.ThrowsAsync<AuthException>(() => servicio.CompletarGoogleAsync("?code=ajeno"));

        Assert.Null(servicio.SesionActual);
        Assert.Empty(manejador.Peticiones);
    }

    [Fact]
    public async Task Google_no_admite_reutilizar_el_mismo_retorno()
    {
        var (servicio, _, _) = CrearGoogle();
        await servicio.UrlGoogleAsync("http://localhost:5211/login");
        await servicio.CompletarGoogleAsync("?code=abc");
        await servicio.DescartarAsync();

        await Assert.ThrowsAsync<AuthException>(() => servicio.CompletarGoogleAsync("?code=abc"));
        Assert.Null(servicio.SesionActual);
    }

    [Fact]
    public async Task Google_sin_parametros_no_hace_nada()
    {
        var (servicio, _, manejador) = CrearGoogle();

        Assert.False(await servicio.CompletarGoogleAsync("?x=1&"));
        Assert.Null(servicio.SesionActual);
        Assert.Empty(manejador.Peticiones);
    }

    [Fact]
    public async Task Google_cancelado_lanza_error_en_espanol_y_consume_el_verificador()
    {
        var (servicio, almacen, _) = CrearGoogle();
        await servicio.UrlGoogleAsync("http://localhost:5211/login");

        var e = await Assert.ThrowsAsync<AuthException>(() => servicio.CompletarGoogleAsync("?error=access_denied&error_description=x"));

        Assert.Contains("cancelado", e.Message);
        Assert.False(almacen.Datos.ContainsKey("miparte.pkce"));
    }

    [Fact]
    public async Task La_demo_local_crea_una_sesion_que_no_caduca_y_sobrevive_a_recargar()
    {
        var (servicio, almacen, reloj) = Crear(ManejadorFalso.Json(HttpStatusCode.OK, Token("a1", "r1")));

        await servicio.IniciarDemoAsync();

        Assert.True(servicio.EsDemoLocal);
        Assert.True(servicio.EsDemo);
        reloj.Advance(TimeSpan.FromDays(3650));
        Assert.Equal("demo", await servicio.ObtenerTokenAsync());

        // Una «recarga»: otro servicio con el mismo almacén recupera la demo sin llamar a Supabase.
        var auth = new SupabaseAuthClient(new HttpClient(ManejadorFalso.Json(HttpStatusCode.InternalServerError, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        var otro = new ServicioSesion(auth, almacen, reloj);
        await otro.InicializarAsync();
        Assert.True(otro.EsDemoLocal);
    }

    [Fact]
    public async Task Cerrar_la_demo_local_no_llama_a_Supabase()
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.OK, Token("a1", "r1"));
        var (servicio, _, _) = Crear(manejador);
        await servicio.IniciarDemoAsync();

        await servicio.CerrarSesionAsync();

        Assert.Null(servicio.SesionActual);
        Assert.Empty(manejador.Peticiones);
    }
}
