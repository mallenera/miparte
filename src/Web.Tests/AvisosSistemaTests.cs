using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MiParte.Web.Api;
using MiParte.Web.Componentes;

namespace MiParte.Web.Tests;

/// <summary>Reintentos ante un servidor caído, aviso de versión nueva y pie de página.</summary>
public class AvisosSistemaTests : TestContext
{
    private static readonly TimeSpan[] SinEspera = [TimeSpan.Zero, TimeSpan.Zero];

    private static (HttpClient http, ServicioConexion conexion, ManejadorFalso servidor) Cliente(Func<int, HttpResponseMessage> responder)
    {
        var llamadas = 0;
        var servidor = new ManejadorFalso(_ => responder(++llamadas));
        var conexion = new ServicioConexion();
        var manejador = new ManejadorReintentos(conexion, TimeProvider.System) { Esperas = SinEspera, InnerHandler = servidor };
        return (new HttpClient(manejador) { BaseAddress = new Uri("http://localhost/") }, conexion, servidor);
    }

    /// <summary>Un 503 durante el reinicio se reintenta hasta que el servidor contesta, y el aviso se apaga.</summary>
    [Fact]
    public async Task Una_lectura_se_reintenta_mientras_el_servidor_devuelve_503()
    {
        var (http, conexion, servidor) = Cliente(n => n < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK));
        var avisos = new List<bool>();
        conexion.Cambiado += () => avisos.Add(conexion.Reintentando);

        var respuesta = await http.GetAsync("api/yo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(3, servidor.Peticiones.Count);
        Assert.Equal([true, false], avisos);
    }

    /// <summary>Un fallo de red también se reintenta.</summary>
    [Fact]
    public async Task Una_lectura_se_reintenta_tras_un_fallo_de_red()
    {
        var (http, _, servidor) = Cliente(n => n == 1 ? throw new HttpRequestException("sin conexión") : new HttpResponseMessage(HttpStatusCode.OK));

        var respuesta = await http.GetAsync("api/yo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(2, servidor.Peticiones.Count);
    }

    /// <summary>Agotados los reintentos se devuelve la última respuesta y el aviso se apaga.</summary>
    [Fact]
    public async Task Al_agotar_los_reintentos_devuelve_el_ultimo_503()
    {
        var (http, conexion, servidor) = Cliente(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var respuesta = await http.GetAsync("api/yo");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal(1 + SinEspera.Length, servidor.Peticiones.Count);
        Assert.False(conexion.Reintentando);
    }

    /// <summary>Los fallos de red persistentes acaban propagándose.</summary>
    [Fact]
    public async Task Al_agotar_los_reintentos_propaga_el_fallo_de_red()
    {
        var (http, conexion, _) = Cliente(_ => throw new HttpRequestException("sin conexión"));

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("api/yo"));
        Assert.False(conexion.Reintentando);
    }

    /// <summary>Un error de la aplicación (500) no es una caída del servidor: no se reintenta.</summary>
    [Fact]
    public async Task Un_500_no_se_reintenta()
    {
        var (http, _, servidor) = Cliente(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var respuesta = await http.GetAsync("api/yo");

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Single(servidor.Peticiones);
    }

    /// <summary>Las escrituras no se reintentan: podrían duplicarse.</summary>
    [Fact]
    public async Task Una_escritura_no_se_reintenta()
    {
        var (http, _, servidor) = Cliente(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var respuesta = await http.PostAsync("api/gastos", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Single(servidor.Peticiones);
    }

    /// <summary>Los reintentos conservan las cabeceras de la petición original.</summary>
    [Fact]
    public async Task El_reintento_conserva_las_cabeceras()
    {
        var (http, _, servidor) = Cliente(n => new HttpResponseMessage(n == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK));
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "api/yo");
        peticion.Headers.Add("X-Hogar-Id", "abc");

        await http.SendAsync(peticion);

        Assert.All(servidor.Peticiones, p => Assert.Equal("abc", p.Headers.GetValues("X-Hogar-Id").Single()));
    }

    /// <summary>El aviso de conexión aparece solo mientras se reintenta.</summary>
    [Fact]
    public void El_aviso_de_conexion_sigue_al_estado()
    {
        var conexion = new ServicioConexion();
        Services.AddSingleton(conexion);
        var cut = RenderComponent<AvisoConexion>();
        Assert.Empty(cut.FindAll(".aviso"));

        conexion.Empezar();
        cut.WaitForAssertion(() => Assert.Contains("se está actualizando", cut.Find(".aviso").TextContent));

        conexion.Terminar();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".aviso")));
    }

    private IRenderedComponent<AvisoActualizacion> RenderizarAvisoActualizacion(out DotNetObjectReference<AvisoActualizacion> referencia)
    {
        JSInterop.SetupVoid("miparteActualizacion.suscribir", _ => true).SetVoidResult();
        var cut = RenderComponent<AvisoActualizacion>();
        referencia = (DotNetObjectReference<AvisoActualizacion>)JSInterop.Invocations["miparteActualizacion.suscribir"].Single().Arguments[0]!;
        return cut;
    }

    /// <summary>Sin versión nueva no se muestra nada; al avisar el service worker aparece el botón.</summary>
    [Fact]
    public async Task El_aviso_de_version_nueva_aparece_cuando_lo_notifica_el_service_worker()
    {
        var cut = RenderizarAvisoActualizacion(out var referencia);
        Assert.Empty(cut.FindAll(".aviso"));

        await cut.InvokeAsync(() => referencia.Value.HayVersionNueva());

        Assert.Contains("versión nueva", cut.Find(".aviso").TextContent);
    }

    /// <summary>«Actualizar» pide al service worker activar la versión nueva.</summary>
    [Fact]
    public async Task Actualizar_activa_la_version_nueva()
    {
        var cut = RenderizarAvisoActualizacion(out var referencia);
        JSInterop.SetupVoid("miparteActualizacion.aplicar").SetVoidResult();
        await cut.InvokeAsync(() => referencia.Value.HayVersionNueva());

        cut.Find(".aviso .btn:not(.btn-ghost)").Click();

        Assert.Single(JSInterop.Invocations["miparteActualizacion.aplicar"]);
    }

    /// <summary>«Ahora no» oculta el aviso hasta que llegue otra versión.</summary>
    [Fact]
    public async Task Ahora_no_oculta_el_aviso()
    {
        var cut = RenderizarAvisoActualizacion(out var referencia);
        await cut.InvokeAsync(() => referencia.Value.HayVersionNueva());

        cut.Find(".aviso .btn-ghost").Click();

        Assert.Empty(cut.FindAll(".aviso"));
    }

    /// <summary>El pie muestra el año y la fecha de actualización en español.</summary>
    [Fact]
    public void El_pie_muestra_copyright_y_fecha_de_actualizacion()
    {
        var cut = RenderComponent<PiePagina>(p => p.Add(c => c.Fecha, new DateOnly(2026, 10, 8)));

        Assert.Contains("© 2026 Mi parte, tu parte", cut.Markup);
        Assert.Equal("8 de octubre de 2026", cut.Find("time").TextContent);
        Assert.Equal("2026-10-08", cut.Find("time").GetAttribute("datetime"));
    }

    /// <summary>La fecha de compilación se lee del metadato del ensamblado.</summary>
    [Fact]
    public void La_fecha_de_compilacion_esta_en_el_ensamblado_del_front()
    {
        Assert.NotNull(InfoVersion.FechaCompilacion);
    }
}
