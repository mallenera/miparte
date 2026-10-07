using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Configuracion;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

/// <summary>
/// Pruebas con el contenedor de inyección real (<see cref="ServiciosWeb.AddMiParteWeb"/>): el manejador HTTP lo construye
/// <c>IHttpClientFactory</c> en su propio ámbito y debe compartir sesión y hogar con las páginas.
/// </summary>
public class RegistroServiciosTests
{
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    private static (ServiceProvider proveedor, ManejadorFalso servidor, AlmacenMemoria almacen) Crear()
    {
        var servidor = ManejadorFalso.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new YoResponse("u1", [], null)));
        var almacen = new AlmacenMemoria();
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddMiParteWeb(new OpcionesWeb { SupabaseUrl = "https://x.supabase.co", SupabaseAnonKey = "anon", CoreUrl = "http://localhost:5098" });
        servicios.AddSingleton<IAlmacenLocal>(almacen); // sustituye al localStorage real
        servicios.AddHttpClient<CoreApiClient>().ConfigurePrimaryHttpMessageHandler(() => servidor);
        return (servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), servidor, almacen);
    }

    [Fact]
    public async Task El_hogar_elegido_llega_como_X_Hogar_Id_aunque_el_manejador_viva_en_otro_ambito()
    {
        var (proveedor, servidor, almacen) = Crear();
        await using var _ = proveedor;
        almacen.Datos["miparte.sesion"] = JsonSerializer.Serialize(new SesionSupabase("jwt", "r1", Ahora.AddHours(1), "u1", "a@b.com"));
        var casa = new HogarResumen(Guid.NewGuid(), "Casa");
        var piso = new HogarResumen(Guid.NewGuid(), "Piso");

        // Las páginas eligen el hogar desde su ámbito…
        using (var ambitoPaginas = proveedor.CreateScope())
        {
            var hogar = ambitoPaginas.ServiceProvider.GetRequiredService<EstadoHogar>();
            await hogar.AplicarAsync(new YoResponse("u1", [casa, piso], null));
            await hogar.SeleccionarAsync(piso.Id);
        }

        // …y la petición sale desde otro ámbito distinto (como el que crea IHttpClientFactory).
        using var otroAmbito = proveedor.CreateScope();
        await otroAmbito.ServiceProvider.GetRequiredService<CoreApiClient>().YoAsync();

        var peticion = servidor.Peticiones.Single();
        Assert.Equal(piso.Id.ToString(), peticion.Headers.GetValues("X-Hogar-Id").Single());
        Assert.Equal("Bearer jwt", peticion.Headers.Authorization!.ToString());
    }

    [Fact]
    public void Sesion_hogar_y_avisos_son_la_misma_instancia_en_todos_los_ambitos()
    {
        var (proveedor, _, _) = Crear();
        using var _ = proveedor;
        using var a = proveedor.CreateScope();
        using var b = proveedor.CreateScope();

        Assert.Same(a.ServiceProvider.GetRequiredService<ServicioSesion>(), b.ServiceProvider.GetRequiredService<ServicioSesion>());
        Assert.Same(a.ServiceProvider.GetRequiredService<EstadoHogar>(), b.ServiceProvider.GetRequiredService<EstadoHogar>());
        Assert.Same(a.ServiceProvider.GetRequiredService<ServicioAvisos>(), b.ServiceProvider.GetRequiredService<ServicioAvisos>());
    }
}
