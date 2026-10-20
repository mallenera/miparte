using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Componentes;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

/// <summary>Tarjeta de avisos del navegador: lo que se ve según el soporte del navegador y la configuración del servidor.</summary>
public class AvisosPushTests : BunitContext
{
    private void Registrar(ApiFalsa api)
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        Services.AddSingleton(new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") }));
        Services.AddSingleton(new ServicioSesion(auth, new AlmacenMemoria(), reloj));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void NavegadorSinSoporte_LoExplicaSinPedirLaClave()
    {
        var api = new ApiFalsa();
        Registrar(api);
        JSInterop.Setup<bool>("miParteAvisos.soportado").SetResult(false);

        var cut = Render<AvisosPush>();

        Assert.Contains("Este navegador no admite avisos", cut.Markup);
        Assert.DoesNotContain("GET /api/notificaciones/clave-publica", api.Recibidas);
    }

    [Fact]
    public void ServidorSinAvisosConfigurados_LoIndica()
    {
        var api = new ApiFalsa().Error("GET /api/notificaciones/clave-publica", HttpStatusCode.NotFound, "no configurado");
        Registrar(api);
        JSInterop.Setup<bool>("miParteAvisos.soportado").SetResult(true);

        var cut = Render<AvisosPush>();

        cut.WaitForAssertion(() => Assert.Contains("todavía no tiene configurados los avisos", cut.Markup));
        Assert.DoesNotContain("Activar avisos", cut.Markup);
    }

    [Fact]
    public void ServidorConClave_OfreceActivarLosAvisos()
    {
        var api = new ApiFalsa().Responde("GET /api/notificaciones/clave-publica", HttpStatusCode.OK, new ClavePublicaPushResponse("BPublica"));
        Registrar(api);
        JSInterop.Setup<bool>("miParteAvisos.soportado").SetResult(true);

        var cut = Render<AvisosPush>();

        cut.WaitForAssertion(() => Assert.Contains("Activar avisos", cut.Markup));
        Assert.Contains("Estado: <strong>desactivados</strong>", cut.Markup);
    }
}
