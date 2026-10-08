using Microsoft.AspNetCore.Components.Authorization;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Configuracion;
using MiParte.Web.Demo;
using MiParte.Web.Hogares;
using MiParte.Web.Temas;

namespace MiParte.Web;

/// <summary>Registro de servicios del front, separado de <c>Program.cs</c> para poder probarlo con la inyección real.</summary>
public static class ServiciosWeb
{
    /// <summary>
    /// Registra sesión, hogar, clientes HTTP y autenticación. <b>Sesión, hogar y avisos son singleton</b>: el
    /// <see cref="ManejadorCoreApi"/> lo crea <c>IHttpClientFactory</c> en su propio ámbito, y con servicios scoped recibiría
    /// instancias distintas de las de las páginas (sin el hogar elegido, por lo que no enviaría <c>X-Hogar-Id</c>).
    /// En WebAssembly el singleton dura lo que la aplicación, que es lo que se necesita.
    /// </summary>
    /// <param name="servicios">Colección de servicios.</param>
    /// <param name="opciones">Configuración del front.</param>
    public static IServiceCollection AddMiParteWeb(this IServiceCollection servicios, OpcionesWeb opciones)
    {
        servicios.AddSingleton(opciones);
        servicios.AddSingleton(TimeProvider.System);

        servicios.AddSingleton<IAlmacenLocal, AlmacenLocalJs>();
        servicios.AddSingleton<ServicioSesion>();
        servicios.AddSingleton<EstadoHogar>();
        servicios.AddSingleton<ServicioAvisos>();
        servicios.AddSingleton<ServicioTema>();
        servicios.AddScoped<ServicioArranque>();
        servicios.AddSingleton<ServicioConexion>();
        servicios.AddTransient<ManejadorReintentos>();
        servicios.AddTransient<ManejadorCoreApi>();
        servicios.AddSingleton<ServidorDemo>();
        servicios.AddTransient<ManejadorDemo>();

        servicios.AddHttpClient<SupabaseAuthClient>(http =>
        {
            http.BaseAddress = Base(opciones.SupabaseUrl, "/auth/v1/");
            http.DefaultRequestHeaders.Add("apikey", opciones.SupabaseAnonKey);
        });
        servicios.AddHttpClient<CoreApiClient>(http => http.BaseAddress = Base(opciones.CoreUrl, "/"))
            .AddHttpMessageHandler<ManejadorReintentos>()
            .AddHttpMessageHandler<ManejadorCoreApi>()
            .AddHttpMessageHandler<ManejadorDemo>();

        servicios.AddAuthorizationCore();
        servicios.AddCascadingAuthenticationState();
        servicios.AddScoped<AuthenticationStateProvider, ProveedorEstadoAutenticacion>();
        return servicios;
    }

    // URL vacía si falta configuración: la app arranca y las pantallas de acceso avisan de ello.
    private static Uri Base(string url, string sufijo) =>
        Uri.TryCreate(url.TrimEnd('/') + sufijo, UriKind.Absolute, out var uri) ? uri : new Uri("http://configuracion.invalida" + sufijo);
}
