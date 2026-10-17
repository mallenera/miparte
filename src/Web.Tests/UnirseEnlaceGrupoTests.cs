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
using MiParte.Web.Modelos;
using MiParte.Web.Pages;

namespace MiParte.Web.Tests;

public class UnirseEnlaceGrupoTests : BunitContext
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa Pérez");
    private static readonly HogarResumen Otra = new(Guid.NewGuid(), "Piso");
    private const string Token = "Qm9n-grupo_token";

    private AlmacenMemoria _almacen = null!;
    private AlmacenMemoria _almacenHogar = null!;

    private void Registrar(ApiFalsa api, bool conSesion = true)
    {
        var reloj = new FakeTimeProvider(Ahora);
        _almacen = new AlmacenMemoria();
        if (conSesion)
            _almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("a", "r", Ahora.AddHours(1), "u1", "a@b.com"));
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        Services.AddSingleton<IAlmacenLocal>(_almacen);
        Services.AddSingleton(new OpcionesWeb());
        Services.AddSingleton(new ServicioSesion(auth, _almacen, reloj));
        Services.AddSingleton(new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") }));
        _almacenHogar = new AlmacenMemoria();
        Services.AddSingleton(new EstadoHogar(_almacenHogar));
        Services.AddScoped<ServicioArranque>();
    }

    private void IrA(string ruta) => Services.GetRequiredService<NavigationManager>().NavigateTo(ruta);

    [Fact]
    public void Sin_codigo_pide_un_enlace_nuevo_y_no_llama_a_la_api()
    {
        var api = new ApiFalsa();
        Registrar(api);
        IrA("/unirse");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.Contains("no trae el código", c.Find("[role=alert]").TextContent));
        Assert.Empty(api.Recibidas);
    }

    [Fact]
    public void Sin_sesion_ofrece_entrar_o_crear_cuenta_sin_llamar_a_la_api()
    {
        var api = new ApiFalsa();
        Registrar(api, conSesion: false);
        IrA($"/unirse#token={Token}");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("a[href=login]")));
        Assert.Empty(api.Recibidas);
        Assert.Equal(Token, _almacen.Datos["miparte.invitacion"]);
    }

    [Fact]
    public void Un_enlace_revocado_muestra_el_motivo_sin_formulario_ni_el_codigo()
    {
        var api = new ApiFalsa().Error("POST /api/enlaces-grupo/unirse", HttpStatusCode.Conflict, "El enlace de grupo ha sido revocado.");
        Registrar(api);
        IrA($"/unirse#token={Token}");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.Contains("ya no funciona", c.Find("[role=alert]").TextContent));
        Assert.Empty(c.FindAll("form"));
        Assert.DoesNotContain(Token, c.Markup);
        Assert.False(_almacen.Datos.ContainsKey("miparte.invitacion"));
    }

    [Fact]
    public void Un_enlace_caducado_pide_uno_nuevo()
    {
        var api = new ApiFalsa().Error("POST /api/enlaces-grupo/unirse", HttpStatusCode.Conflict, "El enlace de grupo ha caducado.");
        Registrar(api);
        IrA($"/unirse#token={Token}");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.Contains("ha caducado", c.Find("[role=alert]").TextContent));
        Assert.Empty(c.FindAll("form"));
    }

    [Fact]
    public void Con_limite_de_peticiones_avisa_y_ofrece_reintentar()
    {
        var api = new ApiFalsa().Error("POST /api/enlaces-grupo/unirse", (HttpStatusCode)429, "Demasiadas peticiones.");
        Registrar(api);
        IrA($"/unirse#token={Token}");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.Contains("demasiados intentos", c.Find("[role=alert]").TextContent));
        Assert.Contains(c.FindAll("button"), b => b.TextContent.Trim() == "Reintentar");
        Assert.DoesNotContain("Demasiadas peticiones", c.Markup);
    }

    [Fact]
    public void Unirse_con_nombre_une_al_hogar_y_va_a_inicio_si_no_tenia_hogares()
    {
        var llamadas = 0;
        var api = new ApiFalsa()
            .Responde("POST /api/enlaces-grupo/unirse", _ => ++llamadas == 1
                ? ApiFalsa.Json(HttpStatusCode.BadRequest, new { error = "Hay que indicar un nombre para unirse al hogar." })
                : ApiFalsa.Json(HttpStatusCode.OK, Casa))
            .Responde("GET /api/yo", HttpStatusCode.OK, new YoResponse("u1", [Casa], Casa));
        Registrar(api);
        IrA($"/unirse#token={Token}");
        var c = Render<Unirse>();
        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));

        c.Find("#nombre-grupo").Change("Marta");
        c.Find("form").Submit();

        var nav = Services.GetRequiredService<NavigationManager>();
        c.WaitForAssertion(() => Assert.Equal(nav.BaseUri, nav.Uri));
        var cuerpo = JsonSerializer.Deserialize<UnirseEnlaceGrupoRequest>(
            api.Cuerpos["POST /api/enlaces-grupo/unirse"], new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(Token, cuerpo.Token);
        Assert.Equal("Marta", cuerpo.Nombre);
        Assert.False(_almacen.Datos.ContainsKey("miparte.invitacion"));
    }

    [Fact]
    public void Si_ya_tenia_hogares_no_cambia_el_activo_y_va_a_elegir()
    {
        var llamadas = 0;
        var api = new ApiFalsa()
            .Responde("POST /api/enlaces-grupo/unirse", _ => ++llamadas == 1
                ? ApiFalsa.Json(HttpStatusCode.BadRequest, new { error = "Hay que indicar un nombre para unirse al hogar." })
                : ApiFalsa.Json(HttpStatusCode.OK, Casa))
            .Responde("GET /api/yo", HttpStatusCode.OK, new YoResponse("u1", [Otra, Casa], Otra));
        Registrar(api);
        // Ya tenía «Piso» como hogar activo, recordado en el navegador.
        _almacenHogar.Datos["miparte.hogar"] = Otra.Id.ToString();
        IrA($"/unirse#token={Token}");
        var c = Render<Unirse>();
        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));

        c.Find("#nombre-grupo").Change("Marta");
        c.Find("form").Submit();

        var nav = Services.GetRequiredService<NavigationManager>();
        c.WaitForAssertion(() => Assert.EndsWith("elegir-hogar", nav.Uri));
        Assert.Equal(Otra.Id, Services.GetRequiredService<EstadoHogar>().HogarActual?.Id);
    }

    [Fact]
    public void Una_invitacion_nominal_sigue_el_flujo_de_invitacion()
    {
        // Sin ruta de enlace de grupo la API devuelve 404: el código se trata como invitación y se ofrece el formulario de siempre.
        var api = new ApiFalsa();
        Registrar(api);
        IrA($"/unirse#token={Token}");

        var c = Render<Unirse>();

        c.WaitForAssertion(() => Assert.NotEmpty(c.FindAll("form")));
        Assert.Contains(c.FindAll("button"), b => b.TextContent.Trim() == "Aceptar invitación");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "El enlace de grupo ha sido revocado.", EstadoUnirseEnlace.Revocado, false)]
    [InlineData(HttpStatusCode.Conflict, "El enlace de grupo ha caducado.", EstadoUnirseEnlace.Caducado, false)]
    [InlineData(HttpStatusCode.Conflict, "Ya perteneces a este hogar.", EstadoUnirseEnlace.YaEsMiembro, false)]
    [InlineData(HttpStatusCode.NotFound, "Enlace no válido.", EstadoUnirseEnlace.Error, false)]
    [InlineData(HttpStatusCode.TooManyRequests, "Demasiadas peticiones.", EstadoUnirseEnlace.Limite, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "No se pudo conectar.", EstadoUnirseEnlace.Error, true)]
    [InlineData(HttpStatusCode.InternalServerError, "Ruta técnica /api/x", EstadoUnirseEnlace.Error, true)]
    public void Clasificar_traduce_cada_error_sin_texto_tecnico(HttpStatusCode codigo, string mensaje, EstadoUnirseEnlace estado, bool reintentable)
    {
        var (e, texto, puede) = ModeloUnirseEnlace.Clasificar(new ApiException(codigo, mensaje));

        Assert.Equal(estado, e);
        Assert.Equal(reintentable, puede);
        Assert.DoesNotContain("Ruta técnica", texto);
        Assert.DoesNotContain("Demasiadas peticiones", texto);
        Assert.False(string.IsNullOrWhiteSpace(texto));
    }
}
