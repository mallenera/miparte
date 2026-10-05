using System.Net;
using Microsoft.Extensions.Time.Testing;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Tests;

public class SupabaseAuthClientTests
{
    private const string TokenJson =
        """{"access_token":"acc","refresh_token":"ref","expires_in":3600,"user":{"id":"u1","email":"a@b.com"}}""";

    private static SupabaseAuthClient Crear(ManejadorFalso manejador, FakeTimeProvider? reloj = null) =>
        new(new HttpClient(manejador) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj ?? new FakeTimeProvider());

    [Fact]
    public async Task IniciarSesion_devuelve_la_sesion_con_caducidad_calculada()
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
        var manejador = ManejadorFalso.Json(HttpStatusCode.OK, TokenJson);

        var sesion = await Crear(manejador, reloj).IniciarSesionAsync("a@b.com", "secreto");

        Assert.Equal("acc", sesion.AccessToken);
        Assert.Equal("u1", sesion.UserId);
        Assert.Equal("a@b.com", sesion.Email);
        Assert.Equal(reloj.GetUtcNow().AddHours(1), sesion.ExpiraEn);
        Assert.Equal("https://x.supabase.co/auth/v1/token?grant_type=password", manejador.Peticiones[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Credenciales_invalidas_se_traducen_al_espanol()
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.BadRequest, """{"error_code":"invalid_credentials","msg":"Invalid login credentials"}""");

        var e = await Assert.ThrowsAsync<AuthException>(() => Crear(manejador).IniciarSesionAsync("a@b.com", "mal"));

        Assert.Equal("Correo o contraseña incorrectos.", e.Message);
        Assert.Equal(400, e.CodigoHttp);
    }

    [Fact]
    public async Task Registro_sin_sesion_indica_que_hay_que_confirmar_el_correo()
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.OK, """{"id":"u1","email":"a@b.com"}""");

        Assert.Null(await Crear(manejador).RegistrarAsync("a@b.com", "secreto"));
    }

    [Fact]
    public async Task Cerrar_sesion_no_lanza_si_falla_la_red()
    {
        var manejador = new ManejadorFalso(_ => throw new HttpRequestException("sin red"));

        await Crear(manejador).CerrarSesionAsync("acc");
    }
}
