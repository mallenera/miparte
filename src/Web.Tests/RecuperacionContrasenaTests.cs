using System.Net;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Tests;

public class RecuperacionContrasenaTests
{
    private const string SesionJson = """{"access_token":"acceso-1","refresh_token":"refresco-1","expires_in":3600,"user":{"id":"u1","email":"ana@example.com"}}""";

    private static SupabaseAuthClient Cliente(ManejadorFalso manejador) =>
        new(new HttpClient(manejador) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, TimeProvider.System);

    [Fact]
    public async Task SolicitarRecuperacion_PideElCorreoAlEndpointRecover()
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.OK, "{}");

        await Cliente(manejador).SolicitarRecuperacionAsync("ana@example.com");

        var peticion = Assert.Single(manejador.Peticiones);
        Assert.EndsWith("/auth/v1/recover", peticion.RequestUri!.ToString());
        Assert.Contains("ana@example.com", manejador.Cuerpos[0]);
    }

    [Fact]
    public async Task RecuperarContrasena_CanjeaElCodigoYActualizaLaContrasena()
    {
        var manejador = new ManejadorFalso(p => p.RequestUri!.AbsolutePath.EndsWith("/verify")
            ? ManejadorFalso.RespuestaJson(HttpStatusCode.OK, SesionJson)
            : ManejadorFalso.RespuestaJson(HttpStatusCode.OK, """{"id":"u1"}"""));

        var sesion = await Cliente(manejador).RecuperarContrasenaAsync("ana@example.com", "12345678", "NuevaClave1");

        Assert.Equal("acceso-1", sesion.AccessToken);
        Assert.Equal(2, manejador.Peticiones.Count);
        var verificacion = manejador.Peticiones[0];
        Assert.EndsWith("/verify", verificacion.RequestUri!.ToString());
        Assert.Contains("\"type\":\"recovery\"", manejador.Cuerpos[0]);
        var actualizacion = manejador.Peticiones[1];
        Assert.Equal(HttpMethod.Put, actualizacion.Method);
        Assert.EndsWith("/user", actualizacion.RequestUri!.ToString());
        Assert.Equal("Bearer", actualizacion.Headers.Authorization!.Scheme);
        Assert.Equal("acceso-1", actualizacion.Headers.Authorization.Parameter);
        Assert.Contains("NuevaClave1", manejador.Cuerpos[1]);
    }

    [Fact]
    public async Task CodigoCaducado_DaUnMensajeEnEspanolYNoActualiza()
    {
        var manejador = ManejadorFalso.Json(HttpStatusCode.Forbidden, """{"error_code":"otp_expired","msg":"Token has expired"}""");

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            Cliente(manejador).RecuperarContrasenaAsync("ana@example.com", "12345678", "NuevaClave1"));

        Assert.Contains("ha caducado", ex.Message);
        Assert.Single(manejador.Peticiones);
    }
}
