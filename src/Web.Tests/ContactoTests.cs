using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Componentes;
using MiParte.Web.Modelos;
using MiParte.Web.Pages;

namespace MiParte.Web.Tests;

public class ContactoTests : BunitContext
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    private static CoreApiClient Cliente(ApiFalsa api) => new(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });

    private ApiFalsa Registrar(string? emailSesion = null)
    {
        var api = new ApiFalsa();
        var reloj = new FakeTimeProvider(Ahora);
        var almacen = new AlmacenMemoria();
        if (emailSesion is not null)
            almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("a", "r", Ahora.AddHours(1), "u1", emailSesion));
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        var sesion = new ServicioSesion(auth, almacen, reloj);
        sesion.InicializarAsync().GetAwaiter().GetResult();
        Services.AddSingleton(sesion);
        Services.AddSingleton(Cliente(api));
        return api;
    }

    private static void Rellenar(IRenderedComponent<Contacto> cut, string email = "ana@ejemplo.es", string mensaje = "Una duda sobre el reparto.")
    {
        cut.Find("#email").Change(email);
        cut.Find("#mensaje").Change(mensaje);
    }

    [Fact]
    public void EnviarMandaElMensajeALaApiYConfirma()
    {
        var api = Registrar().Responde("POST /api/contacto", HttpStatusCode.NoContent);
        var cut = Render<Contacto>();

        cut.Find("#motivo").Change("mejora");
        cut.Find("#nombre").Change("Ana");
        Rellenar(cut);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Mensaje enviado", cut.Find("[role=status]").TextContent));
        using var cuerpo = JsonDocument.Parse(api.Cuerpos["POST /api/contacto"]);
        Assert.Equal("mejora", cuerpo.RootElement.GetProperty("motivo").GetString());
        Assert.Equal("ana@ejemplo.es", cuerpo.RootElement.GetProperty("email").GetString());
        Assert.Equal("Una duda sobre el reparto.", cuerpo.RootElement.GetProperty("mensaje").GetString());
        Assert.Equal("Ana", cuerpo.RootElement.GetProperty("nombre").GetString());
    }

    [Fact]
    public void ConCorreoNoValido_AvisaSinLlamarALaApi()
    {
        var api = Registrar();
        var cut = Render<Contacto>();

        Rellenar(cut, email: "ana");
        cut.Find("form").Submit();

        Assert.Contains("correo válido", cut.Find("[role=alert]").TextContent);
        Assert.Empty(api.Recibidas);
    }

    [Fact]
    public void ConSesion_ProponeSuCorreo()
    {
        Registrar(emailSesion: "marcos@ejemplo.es");

        var cut = Render<Contacto>();

        Assert.Equal("marcos@ejemplo.es", cut.Find("#email").GetAttribute("value"));
    }

    [Fact]
    public void SiElEnvioNoEstaDisponible_OfreceEscribirPorCorreoConElAsuntoDelMotivo()
    {
        Registrar().Error("POST /api/contacto", HttpStatusCode.ServiceUnavailable, "No disponible");
        var cut = Render<Contacto>();

        Rellenar(cut);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(
            "mailto:mipartetuparte@gmail.com?subject=Mi%20parte%2C%20tu%20parte%3A%20duda",
            cut.Find("[role=alert] a").GetAttribute("href")));
        // Lo escrito no se pierde.
        Assert.Equal("Una duda sobre el reparto.", cut.Find("#mensaje").GetAttribute("value"));
    }

    [Fact]
    public void UnErrorDeValidacionDeLaApi_MuestraSuMensajeSinOfrecerCorreo()
    {
        Registrar().Error("POST /api/contacto", HttpStatusCode.BadRequest, "El motivo no es válido.");
        var cut = Render<Contacto>();

        Rellenar(cut);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("El motivo no es válido.", cut.Find("[role=alert]").TextContent));
        Assert.Empty(cut.FindAll("[role=alert] a"));
    }

    [Fact]
    public async Task ElModeloEnviaLaTrampaSoloSiEstaRellena()
    {
        var api = new ApiFalsa().Responde("POST /api/contacto", HttpStatusCode.NoContent);
        var modelo = new ModeloContacto(Cliente(api)) { Email = "ana@ejemplo.es", Mensaje = "Una duda sobre el reparto." };

        await modelo.EnviarAsync();
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(api.Cuerpos["POST /api/contacto"]).RootElement.GetProperty("web").ValueKind);

        modelo.Nuevo();
        modelo.Mensaje = "Otra duda sobre el reparto.";
        modelo.Web = "https://spam.example";
        await modelo.EnviarAsync();
        Assert.Equal("https://spam.example", JsonDocument.Parse(api.Cuerpos["POST /api/contacto"]).RootElement.GetProperty("web").GetString());
    }

    [Fact]
    public void ElPieDeLaAplicacionEnlazaAContacto()
    {
        var cut = Render<PiePagina>();

        Assert.Equal("contacto", cut.Find("footer a").GetAttribute("href"));
    }
}
