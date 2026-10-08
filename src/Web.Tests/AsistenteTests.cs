using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Componentes;
using MiParte.Web.Configuracion;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class AsistenteTests : TestContext
{
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");

    private void Registrar(ApiFalsa api, string urlAsistente = "http://localhost:5002")
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        Services.AddSingleton(new AsistenteApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5002/") }));
        Services.AddSingleton(new EstadoHogar(new AlmacenMemoria()));
        Services.AddSingleton(new ServicioSesion(auth, new AlmacenMemoria(), reloj));
        Services.AddSingleton(new OpcionesWeb { AssistantUrl = urlAsistente });
    }

    // El cuerpo viaja con los acentos escapados: se deserializa en vez de buscar texto.
    private static ChatRequest Enviado(ApiFalsa api) =>
        System.Text.Json.JsonSerializer.Deserialize<ChatRequest>(
            api.Cuerpos["POST /api/chat"], new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    [Fact]
    public void Sin_AssistantUrl_avisa_de_que_no_esta_configurado_y_no_muestra_el_formulario()
    {
        Registrar(new ApiFalsa(), urlAsistente: "");

        var c = RenderComponent<VistaAsistente>();

        Assert.Contains("no está configurado", c.Markup);
        Assert.Empty(c.FindAll("form.chatform"));
    }

    [Fact]
    public void Pregunta_envia_el_historial_y_pinta_la_respuesta_con_las_funciones_usadas()
    {
        var api = new ApiFalsa().Responde("POST /api/chat", HttpStatusCode.OK,
            new ChatResponse("En junio gastasteis 80 €.", ["gasto_total"]));
        Registrar(api);
        var c = RenderComponent<VistaAsistente>();

        c.Find("#pregunta").Input("¿Cuánto gasté en junio?");
        c.Find("form.chatform").Submit();

        Assert.Contains("En junio gastasteis 80 €.", c.Markup);
        Assert.Contains("¿Cuánto gasté en junio?", c.Markup);
        Assert.Contains("Consultado: gasto total", c.Markup);
        var mensaje = Assert.Single(Enviado(api).Mensajes);
        Assert.Equal("user", mensaje.Rol);
        Assert.Equal("¿Cuánto gasté en junio?", mensaje.Texto);
    }

    [Fact]
    public void La_segunda_pregunta_lleva_la_conversacion_anterior()
    {
        var api = new ApiFalsa().Responde("POST /api/chat", HttpStatusCode.OK, new ChatResponse("Respuesta uno.", []));
        Registrar(api);
        var c = RenderComponent<VistaAsistente>();
        c.Find("#pregunta").Input("Primera");
        c.Find("form.chatform").Submit();

        c.Find("#pregunta").Input("Segunda");
        c.Find("form.chatform").Submit();

        var cuerpo = api.Cuerpos["POST /api/chat"];
        Assert.Contains("Primera", cuerpo);
        Assert.Contains("Respuesta uno.", cuerpo);
        Assert.Contains("\"rol\":\"assistant\"", cuerpo);
        Assert.Contains("Segunda", cuerpo);
    }

    [Fact]
    public void Error_del_servidor_se_muestra_y_devuelve_la_pregunta_al_cuadro()
    {
        var api = new ApiFalsa().Error("POST /api/chat", HttpStatusCode.ServiceUnavailable, "El asistente no está disponible ahora mismo.");
        Registrar(api);
        var c = RenderComponent<VistaAsistente>();

        c.Find("#pregunta").Input("Hola");
        c.Find("form.chatform").Submit();

        Assert.Contains("El asistente no está disponible ahora mismo.", c.Find(".aviso.err").TextContent);
        Assert.Equal("Hola", c.Find("#pregunta").GetAttribute("value"));
        Assert.Empty(c.FindAll(".burbuja.mia"));
    }

    [Fact]
    public void La_respuesta_se_pinta_como_texto_y_nunca_como_html()
    {
        var api = new ApiFalsa().Responde("POST /api/chat", HttpStatusCode.OK,
            new ChatResponse("<img src=x onerror=alert(1)> <script>alert(2)</script>", []));
        Registrar(api);
        var c = RenderComponent<VistaAsistente>();

        c.Find("#pregunta").Input("xss");
        c.Find("form.chatform").Submit();

        Assert.Empty(c.FindAll(".burbuja img"));
        Assert.Empty(c.FindAll(".burbuja script"));
        Assert.Contains("&lt;img", c.Markup);
    }

    [Fact]
    public void Una_sugerencia_envia_la_pregunta_sin_escribirla()
    {
        var api = new ApiFalsa().Responde("POST /api/chat", HttpStatusCode.OK, new ChatResponse("Ok.", []));
        Registrar(api);
        var c = RenderComponent<VistaAsistente>();

        c.FindAll(".sugerencias button")[0].Click();

        Assert.Equal("¿Cuánto hemos gastado este mes?", Enviado(api).Mensajes[^1].Texto);
    }

    [Fact]
    public void Nueva_conversacion_vacia_el_historial()
    {
        var api = new ApiFalsa().Responde("POST /api/chat", HttpStatusCode.OK, new ChatResponse("Ok.", []));
        Registrar(api);
        var c = RenderComponent<VistaAsistente>();
        c.Find("#pregunta").Input("Hola");
        c.Find("form.chatform").Submit();

        c.Find("button.linkbtn").Click();

        Assert.Empty(c.FindAll(".burbuja"));
    }
}
