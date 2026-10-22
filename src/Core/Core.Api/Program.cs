using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MiParte.Auth;
using MiParte.Core.Api.Auditoria;
using MiParte.Core.Api.Cierres;
using MiParte.Core.Api.Contacto;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Miembros;
using MiParte.Core.Api.Notificaciones;
using MiParte.Core.Api.Reparto;
using MiParte.Core.Api.Seguridad;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Application.Auditoria;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.Contacto;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Application.Hogares;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Application.Periodos;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Infrastructure;
using MiParte.Core.Infrastructure.Correo;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Seguridad;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSupabaseAuth(builder.Configuration);
// La API no usa cookies ni nada que deba cifrarse entre reinicios (solo valida JWT): claves efímeras en memoria
// evitan los avisos de DataProtection por guardar claves sin cifrar en un contenedor sin disco persistente.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CacheAcceso>(); // miembros activos por usuario: evita una consulta por petición
builder.AddLimitacionPeticiones();
builder.AddCabecerasSeguridad();

// Cadena de conexión: variable de entorno ConnectionStrings__Default (nunca en el repo).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPersistencia(connectionString);
    // Envío de avisos del navegador: solo con base de datos, de donde leen las suscripciones.
    builder.Services.AddHostedService<EnviadorNotificacionesPush>();
}

// Casos de uso y repositorios: fuera del if porque solo necesitan el MiParteDbContext, que los tests sustituyen.
builder.Services.AddRepositorios();
// Avisos: la cola es fuera del if (los casos de uso la llenan); sin claves VAPID el envío queda desactivado.
builder.Services.AddColaNotificacionesPush();
builder.Services.Configure<OpcionesPush>(builder.Configuration.GetSection(OpcionesPush.Seccion));
// Contacto: correo por la API HTTP de Resend (Render gratuito bloquea SMTP). Sin clave ni destino el formulario responde 503.
builder.Services.AddSingleton(builder.Configuration.GetSection(OpcionesCorreo.Seccion).Get<OpcionesCorreo>() ?? new());
builder.Services.AddHttpClient<IEnviadorCorreo, EnviadorCorreoResend>(http =>
{
    http.BaseAddress = EnviadorCorreoResend.UrlBase;
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped(sp => new ServicioContacto(sp.GetRequiredService<IEnviadorCorreo>()));
builder.Services.AddScoped(sp => new ServicioNotificaciones(
    sp.GetRequiredService<IRepositorioNotificaciones>(), sp.GetRequiredService<IRepositorioMiembros>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new ServicioGastos(
    sp.GetRequiredService<IRepositorioGastos>(), sp.GetRequiredService<IPublicadorNotificaciones>()));
builder.Services.AddScoped(sp => new ServicioPerfiles(sp.GetRequiredService<IRepositorioPerfiles>()));
builder.Services.AddScoped(sp => new ServicioHogares(sp.GetRequiredService<IRepositorioHogares>()));
builder.Services.AddScoped(sp => new ServicioAuditoria(sp.GetRequiredService<IRepositorioAuditoria>()));
builder.Services.AddScoped(sp => new ServicioCategorias(
    sp.GetRequiredService<IRepositorioCategorias>(), sp.GetRequiredService<IRepositorioGastos>()));
builder.Services.AddScoped(sp => new ServicioPeriodos(
    sp.GetRequiredService<IRepositorioPeriodos>(), sp.GetRequiredService<IRepositorioLiquidacion>(), sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IPublicadorNotificaciones>()));
builder.Services.AddScoped(sp => new ServicioCierres(
    sp.GetRequiredService<IRepositorioCierres>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new ServicioRecurrentes(
    sp.GetRequiredService<IRepositorioRecurrentes>(), sp.GetRequiredService<IRepositorioGastos>()));
builder.Services.AddScoped(sp => new ServicioMiembros(
    sp.GetRequiredService<IRepositorioMiembros>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new ServicioEnlacesGrupo(
    sp.GetRequiredService<IRepositorioEnlacesGrupo>(), sp.GetRequiredService<IRepositorioMiembros>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new ServicioCuentaComun(
    sp.GetRequiredService<IRepositorioCuentaComun>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new ServicioLiquidacion(
    sp.GetRequiredService<IRepositorioLiquidacion>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IPublicadorNotificaciones>()));

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
// el cubo del limitador. Se activa con ProxyInverso:Proxies / ProxyInverso:Redes (solo esos remitentes pueden
// reenviar cabeceras) o, si el contenedor solo es alcanzable a través del proxy, con ProxyInverso:Confiar.
var reenviadas = ReenvioCabeceras.Crear(app.Configuration);
if (reenviadas is not null) app.UseForwardedHeaders(reenviadas);

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
app.UseErroresInesperados(); // tras el CORS: un 500 conserva Access-Control-Allow-Origin
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
app.MapEnlacesGrupo();
app.MapAuditoria();
app.MapCierresMes();
app.MapCierresPeriodo();
app.MapNotificaciones();
app.MapContacto();

app.Run();

/// <summary>
/// Punto de entrada de Core.Api: configura autenticación Supabase, persistencia, CORS y el middleware
/// de hogar actual, y registra los endpoints. La declaración parcial permite usarla desde los tests
/// de integración (WebApplicationFactory).
/// </summary>
public partial class Program;
