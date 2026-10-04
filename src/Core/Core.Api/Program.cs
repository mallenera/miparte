using Microsoft.AspNetCore.Mvc;
using MiParte.Auth;
using MiParte.Core.Api.Gastos;
using MiParte.Core.Api.Hogares;
using MiParte.Core.Api.Miembros;
using MiParte.Core.Api.Reparto;
using MiParte.Core.Infrastructure;
using MiParte.Core.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSupabaseAuth(builder.Configuration);

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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<HogarActualMiddleware>();

app.MapGet("/health", () => Results.Ok(new { service = "core", status = "ok" }));

app.MapHogares();
app.MapIngresos();
app.MapGastos();
app.MapGastosRecurrentes();
app.MapLiquidacion();
app.MapCategorias();
app.MapPerfiles();
app.MapMiembros();

app.Run();

public partial class Program;
