using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiParte.Auth;
using MiParte.Core.Api.Auditoria;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Miembros;
using MiParte.Core.Api.Reparto;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Infrastructure;
using MiParte.Core.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSupabaseAuth(builder.Configuration);
builder.AddLimitacionPeticiones();
builder.AddCabecerasSeguridad();

// Cadena de conexión: variable de entorno ConnectionStrings__Default (nunca en el repo).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPersistencia(connectionString);
}

// CORS: orígenes en Cors:OrigenesPermitidos (env Cors__OrigenesPermitidos__0, ...).
// Sin configuración no se permite ningún origen cruzado.
var origenes = builder.Configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>()
    ?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().TrimEnd('/')).ToArray() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origenes.Length > 0)
        p.WithOrigins(origenes)
         .WithHeaders("Authorization", "Content-Type", HogarActualMiddleware.Cabecera)
         .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS");
}));

var app = builder.Build();

// Tras un proxy inverso (Render) RemoteIpAddress es la IP del proxy y todas las peticiones anónimas compartirían
// el cubo del limitador. Solo se activa con ProxyInverso:Confiar (env ProxyInverso__Confiar): el contenedor solo
// es alcanzable a través del proxy, por eso no hay lista de redes conocidas. ForwardLimit = 1 toma la última IP
// de X-Forwarded-For, la que añadió el proxy de confianza, y descarta lo que el cliente haya puesto delante.
if (app.Configuration.GetValue<bool>("ProxyInverso:Confiar"))
{
    var reenviadas = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };
    reenviadas.KnownIPNetworks.Clear();
    reenviadas.KnownProxies.Clear();
    app.UseForwardedHeaders(reenviadas);
}

// Deja en el log qué espera la validación del JWT (sin secretos): ayuda a diagnosticar 401.
var supabase = app.Services.GetRequiredService<IOptions<SupabaseAuthOptions>>().Value;
app.Logger.LogInformation(
    "Auth Supabase: emisor esperado = {Emisor}; modo = {Modo}",
    supabase.EmisorEfectivo ?? "(sin configurar: ningún token será válido)",
    string.IsNullOrWhiteSpace(supabase.JwtSecret) ? "JWKS (asimétrico)" : "HS256 (secreto legado)");
if (!string.IsNullOrWhiteSpace(supabase.Url)
    && !(Uri.TryCreate(supabase.Url, UriKind.Absolute, out var urlSupabase)
         && (urlSupabase.Scheme == Uri.UriSchemeHttps || urlSupabase.Scheme == Uri.UriSchemeHttp)))
{
    app.Logger.LogError(
        "Supabase__Url no es una URL absoluta ({Url}): debe ser https://<project-ref>.supabase.co. Ningún token será válido.",
        supabase.Url);
}

app.UseCabecerasSeguridad(); // el primero: también cubre los rechazos del CORS, la autenticación y el limitador
app.UseCors();
app.UseAuthentication();
// Entre autenticar y autorizar: la partición es el usuario ya validado (un sub falsificado no cuenta) y
// los 401 de quien no se autentica también se limitan por IP, porque UseAuthorization corta antes.
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<HogarActualMiddleware>();

app.MapGet("/health", () => Results.Ok(new { service = "core", status = "ok" })).DisableRateLimiting();

app.MapHogares();
app.MapGastos();
app.MapGastosRecurrentes();
app.MapLiquidacion();
app.MapCuentaComun();
app.MapCategorias();
app.MapPerfiles();
app.MapMiembros();
app.MapAuditoria();

app.Run();

/// <summary>
/// Punto de entrada de Core.Api: configura autenticación Supabase, persistencia, CORS y el middleware
/// de hogar actual, y registra los endpoints. La declaración parcial permite usarla desde los tests
/// de integración (WebApplicationFactory).
/// </summary>
public partial class Program;
