using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Configuracion;
using MiParte.Web.Hogares;
using MiParte.Web.Pages;

namespace MiParte.Web.Tests;

public class InvitacionEnlaceTests : BunitContext
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");

    private AlmacenMemoria _almacen = null!;

    /// <summary>Almacén que se comporta como el del navegador sin almacenamiento: las escrituras no se guardan.</summary>
    private sealed class AlmacenBloqueado : AlmacenMemoria
    {
        public override Task EscribirAsync(string clave, string valor) => Task.CompletedTask;
    }

    private void Registrar(ApiFalsa api, bool conSesion, bool almacenamientoBloqueado = false)
    {
        var reloj = new FakeTimeProvider(Ahora);
        _almacen = almacenamientoBloqueado ? new AlmacenBloqueado() : new AlmacenMemoria();
        if (conSesion)
            _almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("a", "r", Ahora.AddHours(1), "u1", "a@b.com"));
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        Services.AddSingleton<IAlmacenLocal>(_almacen);
        Services.AddSingleton(new OpcionesWeb());
        Services.AddSingleton(new ServicioSesion(auth, _almacen, reloj));
        Services.AddSingleton(new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") }));
        Services.AddSingleton(new EstadoHogar(new AlmacenMemoria()));
        Services.AddScoped<ServicioArranque>();
    }

    [Fact]
    public void El_enlace_lleva_el_codigo_en_el_fragmento_y_se_extrae_de_vuelta()
    {
        var enlace = InvitacionPendiente.ConstruirEnlace("https://app.test/unirse", "Qm9n+/=x");

        Assert.StartsWith("https://app.test/unirse#token=", enlace);
        Assert.DoesNotContain("?", enlace);
        Assert.Equal("Qm9n+/=x", InvitacionPendiente.ExtraerToken(enlace));
    }

    [Theory]
    [InlineData("https://app.test/unirse")]
    [InlineData("https://app.test/unirse#otro=1")]
    [InlineData("https://app.test/unirse#token=")]
    public void Sin_codigo_en_el_fragmento_no_hay_token(string url) =>
        Assert.Null(InvitacionPendiente.ExtraerToken(url));

    [Fact]
    public void Con_sesion_guarda_el_codigo_quita_el_fragmento_y_ofrece_aceptar()
    {
        Registrar(new ApiFalsa(), conSesion: true);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/unirse#token=abc123");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));
        Assert.Equal("abc123", _almacen.Datos["miparte.invitacion"]);
        Assert.DoesNotContain("#", nav.Uri);
    }

    [Fact]
    public void Sin_sesion_ofrece_entrar_o_crear_cuenta_y_conserva_el_codigo()
    {
        Registrar(new ApiFalsa(), conSesion: false);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/unirse#token=abc123");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("a[href=login]")));
        Assert.NotEmpty(c.FindAll("a[href=registro]"));
        Assert.Equal("abc123", _almacen.Datos["miparte.invitacion"]);
    }

    [Fact]
    public void Aceptar_envia_el_codigo_guardado_y_lo_descarta()
    {
        var api = new ApiFalsa()
            .Responde("POST /api/invitaciones/aceptar", HttpStatusCode.OK, Casa)
            .Responde("GET /api/yo", HttpStatusCode.OK, new YoResponse("u1", [Casa], Casa));
        Registrar(api, conSesion: true);
        _almacen.Datos["miparte.invitacion"] = "abc123";
        Services.GetRequiredService<NavigationManager>().NavigateTo("/unirse");
        var c = Render<Unirse>();
        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));

        c.Find("#nombre").Change("Luis");
        c.Find("form").Submit();

        c.WaitForAssertion(() => Assert.False(_almacen.Datos.ContainsKey("miparte.invitacion")));
        var cuerpo = JsonSerializer.Deserialize<AceptarInvitacionRequest>(
            api.Cuerpos["POST /api/invitaciones/aceptar"], new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("abc123", cuerpo.Token);
        Assert.Equal("Luis", cuerpo.Nombre);
    }

    [Fact]
    public void Si_falla_la_carga_tras_aceptar_se_reintenta_sin_reenviar_el_codigo()
    {
        var api = new ApiFalsa()
            .Responde("POST /api/invitaciones/aceptar", HttpStatusCode.OK, Casa)
            .Error("GET /api/yo", HttpStatusCode.ServiceUnavailable, "Sin servicio.");
        Registrar(api, conSesion: true);
        _almacen.Datos["miparte.invitacion"] = "abc123";
        Services.GetRequiredService<NavigationManager>().NavigateTo("/unirse");
        var c = Render<Unirse>();
        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));

        c.Find("form").Submit();

        c.WaitForAssertion(() => Assert.Contains("Ya te has unido", c.Find("[role=alert]").TextContent));
        Assert.Empty(c.FindAll("form"));
        Assert.False(_almacen.Datos.ContainsKey("miparte.invitacion"));
        Assert.Contains(c.FindAll("button"), b => b.TextContent.Trim() == "Reintentar");
    }

    [Fact]
    public void Una_invitacion_usada_muestra_el_error_y_no_se_vuelve_a_ofrecer()
    {
        var api = new ApiFalsa().Error("POST /api/invitaciones/aceptar", HttpStatusCode.Conflict, "La invitación ya se ha usado.");
        Registrar(api, conSesion: true);
        _almacen.Datos["miparte.invitacion"] = "abc123";
        Services.GetRequiredService<NavigationManager>().NavigateTo("/unirse");
        var c = Render<Unirse>();
        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));

        c.Find("form").Submit();

        c.WaitForAssertion(() => Assert.Contains("ya se ha usado", c.Find("[role=alert]").TextContent));
        Assert.False(_almacen.Datos.ContainsKey("miparte.invitacion"));
    }

    [Fact]
    public async Task Tras_acceder_con_invitacion_pendiente_el_destino_es_unirse()
    {
        var almacen = new AlmacenMemoria();
        Assert.Equal("", await InvitacionPendiente.DestinoTrasAccesoAsync(almacen));

        await InvitacionPendiente.GuardarAsync(almacen, "abc123");

        Assert.Equal("unirse", await InvitacionPendiente.DestinoTrasAccesoAsync(almacen));
    }

    [Fact]
    public void Sin_almacenamiento_los_enlaces_de_acceso_conservan_el_codigo_en_el_fragmento()
    {
        Registrar(new ApiFalsa(), conSesion: false, almacenamientoBloqueado: true);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/unirse#token=abc123");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("a[href='login#token=abc123']")));
        Assert.NotEmpty(c.FindAll("a[href='registro#token=abc123']"));
        Assert.Contains("#token=abc123", nav.Uri);
    }

    [Fact]
    public void Login_sin_almacenamiento_reenvia_a_unirse_con_el_codigo_de_la_url()
    {
        Registrar(new ApiFalsa(), conSesion: true, almacenamientoBloqueado: true);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/login#token=abc123");

        Render<Login>();

        Assert.EndsWith("/unirse#token=abc123", nav.Uri);
    }

    [Fact]
    public void Login_y_registro_sin_almacenamiento_se_enlazan_conservando_el_codigo()
    {
        Registrar(new ApiFalsa(), conSesion: false, almacenamientoBloqueado: true);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/login#token=abc123");
        Assert.NotEmpty(Render<Login>().FindAll("a[href='registro#token=abc123']"));

        nav.NavigateTo("/registro#token=abc123");
        Assert.NotEmpty(Render<Registro>().FindAll("a[href='login#token=abc123']"));
    }

    [Fact]
    public async Task El_codigo_de_la_url_tiene_prioridad_para_decidir_el_destino()
    {
        var almacen = new AlmacenMemoria();

        Assert.Equal("unirse#token=a%2Bb", await InvitacionPendiente.DestinoTrasAccesoAsync(almacen, "a+b"));
    }
}
