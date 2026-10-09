using Microsoft.Extensions.Options;
using MiParte.Assistant.Api;
using MiParte.Assistant.Api.Chat;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Assistant.Api.Seguridad;
using MiParte.Auth;

var builder = WebApplication.CreateBuilder(args);

var opciones = builder.Configuration.GetSection(OpcionesAsistente.Seccion).Get<OpcionesAsistente>() ?? new();
builder.Services.Configure<OpcionesAsistente>(builder.Configuration.GetSection(OpcionesAsistente.Seccion));
builder.Services.AddSupabaseAuth(builder.Configuration);
builder.AddSeguridadAsistente(opciones);

// Datos del hogar: siempre vía Core.Api con el JWT de la persona (no hay acceso directo a la base de datos).
builder.Services.AddHttpClient(FabricaDatosHogarCore.NombreCliente, c =>
{
    c.BaseAddress = new Uri(opciones.CoreUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IFabricaDatosHogar, FabricaDatosHogarCore>();

// Modelo: la clave ANTHROPIC_API_KEY se lee solo del entorno.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClienteModelo, ClienteAnthropic>();
builder.Services.AddScoped<ServicioChat>();

// CORS: orígenes en Cors:OrigenesPermitidos (env Cors__OrigenesPermitidos__0, ...). Sin configuración, ninguno.
var origenes = builder.Configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>()
    ?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().TrimEnd('/')).ToArray() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origenes.Length > 0)
        p.WithOrigins(origenes)
         .WithHeaders("Authorization", "Content-Type", EndpointChat.CabeceraHogar)
         .WithMethods("GET", "POST", "OPTIONS");
}));

var app = builder.Build();

var supabase = app.Services.GetRequiredService<IOptions<SupabaseAuthOptions>>().Value;
app.Logger.LogInformation(
    "Auth Supabase: emisor esperado = {Emisor}; modelo = {Modelo}; clave de Anthropic {Clave}",
    supabase.EmisorEfectivo ?? "(sin configurar: ningún token será válido)",
    opciones.Modelo,
    string.IsNullOrWhiteSpace(app.Configuration["ANTHROPIC_API_KEY"]) ? "NO configurada" : "configurada");

app.UseCabecerasSeguridad();
app.UseCors();
app.UseErroresInesperados();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "assistant", status = "ok" })).DisableRateLimiting();
app.MapChat();

app.Run();

/// <summary>
/// Punto de entrada de Assistant.Api: chatbot con tool calling que consulta los datos del hogar vía Core.Api. La
/// declaración parcial permite usarla desde los tests de integración (WebApplicationFactory).
/// </summary>
public partial class Program;
