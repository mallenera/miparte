using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MiParte.Web;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Configuracion;
using MiParte.Web.Hogares;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var opciones = OpcionesWeb.Leer(builder.Configuration);
builder.Services.AddSingleton(opciones);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<IAlmacenLocal, AlmacenLocalJs>();
builder.Services.AddScoped<ServicioSesion>();
builder.Services.AddScoped<EstadoHogar>();
builder.Services.AddScoped<ServicioArranque>();
builder.Services.AddScoped<ServicioAvisos>();
builder.Services.AddScoped<ManejadorCoreApi>();

// URLs vacías si falta configuración: la app arranca y las pantallas de acceso avisan de ello.
static Uri Base(string url, string sufijo = "") =>
    Uri.TryCreate(url.TrimEnd('/') + sufijo, UriKind.Absolute, out var uri) ? uri : new Uri("http://configuracion.invalida" + sufijo);

builder.Services.AddHttpClient<SupabaseAuthClient>(http =>
{
    http.BaseAddress = Base(opciones.SupabaseUrl, "/auth/v1/");
    http.DefaultRequestHeaders.Add("apikey", opciones.SupabaseAnonKey);
});
builder.Services.AddHttpClient<CoreApiClient>(http => http.BaseAddress = Base(opciones.CoreUrl, "/"))
    .AddHttpMessageHandler<ManejadorCoreApi>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ProveedorEstadoAutenticacion>();

await builder.Build().RunAsync();
