using MiParte.Core.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Cadena de conexión: variable de entorno ConnectionStrings__Default (nunca en el repo).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPersistencia(connectionString);
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "core", status = "ok" }));

app.Run();

public partial class Program;
