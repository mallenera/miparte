using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MiParte.Web.Api;
using MiParte.Web.Componentes;

namespace MiParte.Web.Tests;

/// <summary>Reintentos ante un servidor caído, aviso de versión nueva y pie de página.</summary>
public class AvisosSistemaTests : BunitContext
{
    private static readonly TimeSpan[] SinEspera = [TimeSpan.Zero, TimeSpan.Zero];

    private static (HttpClient http, ServicioConexion conexion, ManejadorFalso servidor) Cliente(Func<int, HttpResponseMessage> responder)
    {
        var llamadas = 0;
        var servidor = new ManejadorFalso(_ => responder(++llamadas));
        var conexion = new ServicioConexion();
        var manejador = new ManejadorReintentos(conexion, TimeProvider.System) { Esperas = SinEspera, EsperaMaximaPorLimite = TimeSpan.Zero, InnerHandler = servidor };
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

    private static HttpResponseMessage Limitada(int segundos = 60)
    {
        var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(segundos));
        return r;
    }

    /// <summary>Un 429 en una lectura se espera (con tope) y se reintenta; mientras tanto el aviso es el de exceso de peticiones.</summary>
    [Fact]
    public async Task Una_lectura_se_reintenta_tras_un_429_y_avisa_del_exceso_de_peticiones()
    {
        var (http, conexion, servidor) = Cliente(n => n < 3 ? Limitada() : new HttpResponseMessage(HttpStatusCode.OK));
        var estados = new List<(bool Reintentando, bool PorLimite)>();
        conexion.Cambiado += () => estados.Add((conexion.Reintentando, conexion.PorLimite));

        var respuesta = await http.GetAsync("api/miembros");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(3, servidor.Peticiones.Count);
        Assert.Contains((true, true), estados);
        Assert.Equal((false, false), estados[^1]);
        Assert.False(conexion.Reintentando);
        Assert.False(conexion.PorLimite);
    }

    /// <summary>Una fecha de `Retry-After` ya vencida no vale «espera cero»: se usa la espera por defecto, y un delta de cero sí es cero.</summary>
    [Fact]
    public async Task Un_retry_after_con_fecha_vencida_usa_la_espera_por_defecto()
    {
        var reloj = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var llamadas = 0;
        var servidor = new ManejadorFalso(_ =>
        {
            var r = new HttpResponseMessage(++llamadas == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (llamadas == 1) r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(reloj.GetUtcNow().AddMinutes(-5));
            return r;
        });
        var manejador = new ManejadorReintentos(new ServicioConexion(), reloj)
            { Esperas = SinEspera, EsperaMaximaPorLimite = TimeSpan.FromSeconds(30), InnerHandler = servidor };
        var http = new HttpClient(manejador) { BaseAddress = new Uri("http://localhost/") };

        var tarea = http.GetAsync("api/miembros");
        await Task.Delay(50);
        Assert.False(tarea.IsCompleted); // espera (la de por defecto), no reintenta de inmediato
        reloj.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        Assert.False(tarea.IsCompleted);
        reloj.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(HttpStatusCode.OK, (await tarea).StatusCode);
        Assert.Equal(2, llamadas);
    }

    /// <summary>Si sigue limitando tras los reintentos permitidos se devuelve el 429 para que se muestre el mensaje de la API.</summary>
    [Fact]
    public async Task Tras_los_reintentos_por_limite_devuelve_el_429()
    {
        var (http, conexion, servidor) = Cliente(_ => Limitada());

        var respuesta = await http.GetAsync("api/miembros");

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Equal(1 + ManejadorReintentos.MaxReintentosPorLimite, servidor.Peticiones.Count);
        Assert.False(conexion.Reintentando);
    }

    /// <summary>Una escritura que recibe 429 no se repite: no se sabe si llegó a procesarse.</summary>
    [Fact]
    public async Task Una_escritura_con_429_no_se_reintenta()
    {
        var (http, _, servidor) = Cliente(_ => Limitada());

        var respuesta = await http.PostAsync("api/gastos", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Single(servidor.Peticiones);
    }

    /// <summary>El aviso explica el motivo: servidor arrancando o exceso de peticiones.</summary>
    [Fact]
    public void El_aviso_de_conexion_distingue_el_exceso_de_peticiones_del_servidor_caido()
    {
        var conexion = new ServicioConexion();
        Services.AddSingleton(conexion);
        var c = Render<AvisoConexion>();
        Assert.Empty(c.FindAll(".aviso-sistema"));

        conexion.Empezar();
        c.WaitForAssertion(() => Assert.Contains("arrancando", c.Markup));

        conexion.Empezar(porLimite: true);
        c.WaitForAssertion(() => Assert.Contains("muchas peticiones", c.Markup));

        conexion.Terminar(porLimite: true);
        c.WaitForAssertion(() => Assert.Contains("arrancando", c.Markup));
        conexion.Terminar();
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
        var cut = Render<AvisoConexion>();
        Assert.Empty(cut.FindAll(".aviso"));

        conexion.Empezar();
        cut.WaitForAssertion(() => Assert.Contains("se está actualizando", cut.Find(".aviso").TextContent));

        conexion.Terminar();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".aviso")));
    }

    private IRenderedComponent<AvisoActualizacion> RenderizarAvisoActualizacion(out DotNetObjectReference<AvisoActualizacion> referencia)
    {
        JSInterop.SetupVoid("miparteActualizacion.suscribir", _ => true).SetVoidResult();
        var cut = Render<AvisoActualizacion>();
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
        var cut = Render<PiePagina>(p => p.Add(c => c.Fecha, new DateOnly(2026, 10, 8)));

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
