using Microsoft.AspNetCore.Mvc;
using MiParte.Auth;
using MiParte.Core.Api.Hogares;
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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<HogarActualMiddleware>();

app.MapGet("/health", () => Results.Ok(new { service = "core", status = "ok" }));

app.MapGet("/api/yo", (HttpContext ctx, [FromServices] IHogarActual hogar) =>
        Results.Ok(new { userId = ctx.User.FindFirst("sub")?.Value, hogarId = hogar.HogarId }))
    .RequireAuthorization();

app.Run();

public partial class Program;
