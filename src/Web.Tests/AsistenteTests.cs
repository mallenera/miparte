using System.Net;
using System.Text;
using System.Text.Json;
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

public class AsistenteTests : BunitContext
{
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();
    private static readonly Guid CategoriaSuper = Guid.NewGuid();
    private static readonly Guid PerfilCasa = Guid.NewGuid();
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string RutaChat = "POST /api/chat/stream";

    private void Registrar(ApiFalsa api, string urlAsistente = "http://localhost:5002", ApiFalsa? core = null)
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        Services.AddSingleton(new AsistenteApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5002/") }));
        Services.AddSingleton(new CoreApiClient(new HttpClient(core ?? new ApiFalsa()) { BaseAddress = new Uri("http://localhost:5001/") }));
        Services.AddSingleton(new EstadoHogar(new AlmacenMemoria()));
        Services.AddSingleton(new ServicioSesion(auth, new AlmacenMemoria(), reloj));
        Services.AddSingleton(new OpcionesWeb { AssistantUrl = urlAsistente });
    }

    /// <summary>Respuesta de streaming: un evento de texto con toda la respuesta y el evento final.</summary>
    private static HttpResponseMessage Sse(ChatResponse respuesta)
    {
        var cuerpo = $"event: texto\ndata: {JsonSerializer.Serialize(new { texto = respuesta.Respuesta }, Web)}\n\n"
            + $"event: fin\ndata: {JsonSerializer.Serialize(respuesta, Web)}\n\n";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "text/event-stream") };
    }

    private static ApiFalsa Chat(ChatResponse respuesta) => new ApiFalsa().Responde(RutaChat, _ => Sse(respuesta));

    // El cuerpo viaja con los acentos escapados: se deserializa en vez de buscar texto.
    private static ChatRequest Enviado(ApiFalsa api) =>
        JsonSerializer.Deserialize<ChatRequest>(api.Cuerpos[RutaChat], Web)!;

    [Fact]
    public void Sin_AssistantUrl_avisa_de_que_no_esta_configurado_y_no_muestra_el_formulario()
    {
        Registrar(new ApiFalsa(), urlAsistente: "");

        var c = Render<VistaAsistente>();

        Assert.Contains("no está configurado", c.Markup);
        Assert.Empty(c.FindAll("form.chatform"));
    }

    [Fact]
    public void Pregunta_envia_el_historial_y_pinta_la_respuesta_con_las_funciones_usadas()
    {
        var api = Chat(new ChatResponse("En junio gastasteis 80 €.", ["gasto_total"]));
        Registrar(api);
        var c = Render<VistaAsistente>();

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
        var api = Chat(new ChatResponse("Respuesta uno.", []));
        Registrar(api);
        var c = Render<VistaAsistente>();
        c.Find("#pregunta").Input("Primera");
        c.Find("form.chatform").Submit();

        c.Find("#pregunta").Input("Segunda");
        c.Find("form.chatform").Submit();

        var cuerpo = api.Cuerpos[RutaChat];
        Assert.Contains("Primera", cuerpo);
        Assert.Contains("Respuesta uno.", cuerpo);
        Assert.Contains("\"rol\":\"assistant\"", cuerpo);
        Assert.Contains("Segunda", cuerpo);
    }

    [Fact]
    public void Error_del_servidor_se_muestra_y_devuelve_la_pregunta_al_cuadro()
    {
        var api = new ApiFalsa().Error(RutaChat, HttpStatusCode.ServiceUnavailable, "El asistente no está disponible ahora mismo.");
        Registrar(api);
        var c = Render<VistaAsistente>();

        c.Find("#pregunta").Input("Hola");
        c.Find("form.chatform").Submit();

        Assert.Contains("El asistente no está disponible ahora mismo.", c.Find(".aviso.err").TextContent);
        Assert.Equal("Hola", c.Find("#pregunta").GetAttribute("value"));
        Assert.Empty(c.FindAll(".burbuja.mia"));
        Assert.Empty(c.FindAll(".burbuja:not(.mia)"));
    }

    [Fact]
    public void La_respuesta_se_pinta_como_texto_y_nunca_como_html()
    {
        var api = Chat(new ChatResponse("<img src=x onerror=alert(1)> <script>alert(2)</script>", []));
        Registrar(api);
        var c = Render<VistaAsistente>();

        c.Find("#pregunta").Input("xss");
        c.Find("form.chatform").Submit();

        Assert.Empty(c.FindAll(".burbuja img"));
        Assert.Empty(c.FindAll(".burbuja script"));
        Assert.Contains("&lt;img", c.Markup);
    }

    [Fact]
    public void El_panel_flotante_empieza_cerrado_y_el_boton_lo_abre()
    {
        Registrar(new ApiFalsa());
        JSInterop.Mode = JSRuntimeMode.Loose;
        var c = Render<PanelAsistente>();

        Assert.True(c.Find("aside.panel-asist").HasAttribute("hidden"));
        c.Find("button.fab-asistente").Click();

        Assert.False(c.Find("aside.panel-asist").HasAttribute("hidden"));
        Assert.Empty(c.FindAll("button.fab-asistente"));
    }

    [Fact]
    public void El_panel_se_cierra_con_el_boton_y_con_escape_y_conserva_la_conversacion()
    {
        var api = Chat(new ChatResponse("Respuesta guardada.", []));
        Registrar(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var c = Render<PanelAsistente>();
        c.Find("button.fab-asistente").Click();
        c.Find("#pregunta").Input("Hola");
        c.Find("form.chatform").Submit();

        c.Find("button[aria-label='Cerrar el asistente']").Click();
        Assert.True(c.Find("aside.panel-asist").HasAttribute("hidden"));

        c.Find("button.fab-asistente").Click();
        c.Find("aside.panel-asist").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.True(c.Find("aside.panel-asist").HasAttribute("hidden"));
        Assert.Contains("Respuesta guardada.", c.Markup);
    }

    [Fact]
    public void Una_sugerencia_envia_la_pregunta_sin_escribirla()
    {
        var api = Chat(new ChatResponse("Ok.", []));
        Registrar(api);
        var c = Render<VistaAsistente>();

        c.FindAll(".sugerencias button")[0].Click();

        Assert.Equal("¿Cuánto hemos gastado este mes?", Enviado(api).Mensajes[^1].Texto);
    }

    [Fact]
    public void Nueva_conversacion_vacia_el_historial()
    {
        var api = Chat(new ChatResponse("Ok.", []));
        Registrar(api);
        var c = Render<VistaAsistente>();
        c.Find("#pregunta").Input("Hola");
        c.Find("form.chatform").Submit();

        c.Find("button.linkbtn").Click();

        Assert.Empty(c.FindAll(".burbuja"));
    }

    [Fact]
    public void El_borrador_se_muestra_y_no_se_puede_confirmar_sin_elegir_quien_paga()
    {
        var api = Chat(new ChatResponse("Te lo dejo preparado.", ["proponer_gasto"],
            new BorradorGastoDto(12m, "Café", new DateOnly(2026, 10, 6), "Alimentación", "Casa", CategoriaSuper, PerfilCasa)));
        var core = new ApiFalsa().Responde("GET /api/miembros", HttpStatusCode.OK,
            new[] { new MiembroDto(Ana, "Ana", "adulto", null, true, "admin", true, true), new MiembroDto(Luis, "Luis", "adulto", null, true, "miembro", true) });
        Registrar(api, core: core);
        var c = Render<VistaAsistente>();

        c.Find("#pregunta").Input("Apunta 12 € de café hoy");
        c.Find("form.chatform").Submit();

        Assert.Contains("Gasto propuesto (sin guardar)", c.Markup);
        Assert.Contains("Café", c.Markup);
        Assert.True(c.Find("div.borrador button.btn").HasAttribute("disabled"));
    }

    [Fact]
    public void Confirmar_envia_el_gasto_con_quien_paga_y_lo_marca_como_guardado()
    {
        var borrador = new BorradorGastoDto(12m, "Café", new DateOnly(2026, 10, 6), "Alimentación", "Casa", CategoriaSuper, PerfilCasa);
        var api = Chat(new ChatResponse("Te lo dejo preparado.", ["proponer_gasto"], borrador))
            .Responde("POST /api/chat/confirmar-gasto", HttpStatusCode.Created,
                new GastoResponse(Guid.NewGuid(), borrador.Fecha, 12m, Guid.NewGuid(), Luis, Guid.NewGuid(), "Café", null, []));
        var core = new ApiFalsa().Responde("GET /api/miembros", HttpStatusCode.OK,
            new[] { new MiembroDto(Ana, "Ana", "adulto", null, true, "admin", true, true), new MiembroDto(Luis, "Luis", "adulto", null, true, "miembro", true) });
        Registrar(api, core: core);
        var c = Render<VistaAsistente>();
        c.Find("#pregunta").Input("Apunta 12 € de café hoy");
        c.Find("form.chatform").Submit();

        c.Find("div.borrador select").Change(Luis.ToString());
        c.Find("div.borrador button.btn").Click();

        Assert.Contains("Gasto guardado.", c.Markup);
        var enviado = JsonSerializer.Deserialize<ConfirmarGastoRequest>(api.Cuerpos["POST /api/chat/confirmar-gasto"], Web)!;
        Assert.Equal((12m, "Café", Luis), (enviado.Importe, enviado.Concepto, enviado.PagadoPor));
        Assert.Equal(borrador.Fecha, enviado.Fecha);
        Assert.Equal((CategoriaSuper, PerfilCasa), (enviado.CategoriaId, enviado.PerfilRepartoId));
    }

    [Fact]
    public void Si_los_predeterminados_cambiaron_el_borrador_se_descarta_y_avisa()
    {
        var api = Chat(new ChatResponse("Te lo dejo preparado.", ["proponer_gasto"],
                new BorradorGastoDto(12m, "Café", new DateOnly(2026, 10, 6), "Alimentación", "Casa", CategoriaSuper, PerfilCasa)))
            .Error("POST /api/chat/confirmar-gasto", HttpStatusCode.Conflict,
                "Los predeterminados para gastos han cambiado desde que se propuso el gasto, así que no lo he guardado.");
        var core = new ApiFalsa().Responde("GET /api/miembros", HttpStatusCode.OK,
            new[] { new MiembroDto(Ana, "Ana", "adulto", null, true, "admin", true, true), new MiembroDto(Luis, "Luis", "adulto", null, true, "miembro", true) });
        Registrar(api, core: core);
        var c = Render<VistaAsistente>();
        c.Find("#pregunta").Input("Apunta 12 € de café hoy");
        c.Find("form.chatform").Submit();
        c.Find("div.borrador select").Change(Luis.ToString());

        c.Find("div.borrador button.btn").Click();

        Assert.Contains("Descartado", c.Markup);
        Assert.Contains("los predeterminados para gastos han cambiado", c.Markup.ToLowerInvariant());
        Assert.Empty(c.FindAll("div.borrador button.btn"));
    }

    [Fact]
    public void Descartar_el_borrador_no_guarda_nada()
    {
        var api = Chat(new ChatResponse("Te lo dejo preparado.", ["proponer_gasto"],
            new BorradorGastoDto(12m, "Café", new DateOnly(2026, 10, 6), "Alimentación", "Casa", CategoriaSuper, PerfilCasa)));
        var core = new ApiFalsa().Responde("GET /api/miembros", HttpStatusCode.OK, new[] { new MiembroDto(Ana, "Ana", "adulto", null, true, "admin", true, true) });
        Registrar(api, core: core);
        var c = Render<VistaAsistente>();
        c.Find("#pregunta").Input("Apunta 12 € de café");
        c.Find("form.chatform").Submit();

        c.Find("div.borrador button.linkbtn").Click();

        Assert.Contains("Descartado", c.Markup);
        Assert.False(api.Recibidas.Any(r => r.Contains("confirmar-gasto")));
    }

    [Fact]
    public void Añadir_gasto_pregunta_uno_a_uno_y_envia_el_gasto_completo_al_asistente()
    {
        var api = Chat(new ChatResponse("Te lo dejo preparado.", ["proponer_gasto"]));
        Registrar(api);
        var c = Render<VistaAsistente>();

        c.Find("button.alta-gasto").Click();
        Assert.Contains("¿Cuánto ha sido?", c.Markup);
        c.Find("#pregunta").Input("12,5");
        c.Find("form.chatform").Submit();
        Assert.Contains("¿Qué se ha pagado?", c.Markup);
        c.Find("#pregunta").Input("Café");
        c.Find("form.chatform").Submit();
        Assert.Contains("¿De qué fecha es?", c.Markup);
        c.Find("#pregunta").Input("23/10/26");
        c.Find("form.chatform").Submit();

        var mensaje = Enviado(api).Mensajes[^1].Texto;
        Assert.Contains("importe 12.5 €", mensaje);
        Assert.Contains("concepto «Café»", mensaje);
        Assert.Contains("fecha 2026-10-23", mensaje);
    }

    [Fact]
    public void Una_fecha_mal_escrita_vuelve_a_preguntar_sin_enviar_nada_al_asistente()
    {
        var api = Chat(new ChatResponse("Ok.", []));
        Registrar(api);
        var c = Render<VistaAsistente>();
        c.Find("button.alta-gasto").Click();
        c.Find("#pregunta").Input("12");
        c.Find("form.chatform").Submit();
        c.Find("#pregunta").Input("Café");
        c.Find("form.chatform").Submit();

        c.Find("#pregunta").Input("2026-10-23");
        c.Find("form.chatform").Submit();

        Assert.Contains("La fecha no es válida", c.Markup);
        Assert.DoesNotContain(api.Recibidas, r => r.Contains("chat/stream"));
    }

    [Fact]
    public async Task LectorSse_entregaLosEventosConSusDatosEnOrden()
    {
        const string cuerpo = "event: texto\ndata: {\"texto\":\"Hola\"}\n\nevent: texto\ndata: {\"texto\":\"qué tal\"}\n\nevent: fin\ndata: {}\n\n";
        using var flujo = new MemoryStream(Encoding.UTF8.GetBytes(cuerpo));

        var eventos = new List<(string Evento, string Datos)>();
        await foreach (var e in LectorSse.LeerAsync(flujo)) eventos.Add(e);

        Assert.Equal(
            [("texto", "{\"texto\":\"Hola\"}"), ("texto", "{\"texto\":\"qué tal\"}"), ("fin", "{}")],
            eventos);
    }
}
