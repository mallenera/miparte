using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MiParte.Core.Infrastructure.Persistencia;
using MiParte.Seguridad;
using Xunit;

namespace MiParte.Core.Tests;

/// <summary>Comprobación de disponibilidad para el monitor externo y registro de la duración de las peticiones.</summary>
public class SaludYRegistroTests
{
    /// <summary>Guarda las líneas de log de la categoría de peticiones.</summary>
    private sealed class CapturaLog : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Nivel, string Texto)> Lineas { get; } = new();

        public ILogger CreateLogger(string categoria) => new Registrador(categoria == RegistroPeticiones.Categoria ? Lineas : null);

        public void Dispose() { }

        private sealed class Registrador(ConcurrentQueue<(LogLevel, string)>? destino) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formato)
                => destino?.Enqueue((nivel, formato(estado, ex)));
        }
    }

    private static WebApplicationFactory<Program> ConLog(CapturaLog captura, string? umbralMs = null)
        => AutenticacionTests.Crear(AutenticacionTests.Secreto).WithWebHostBuilder(b =>
        {
            if (umbralMs is not null) b.UseSetting("Registro:PeticionLentaMs", umbralMs);
            b.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(captura));
        });

    [Fact]
    public async Task Listo_ConBaseDeDatosDisponible_Responde200Ok()
    {
        await using var f = AutenticacionTests.Crear(AutenticacionTests.Secreto);

        var r = await f.CreateClient().GetAsync("/health/listo");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("core", cuerpo!["service"]);
        Assert.Equal("ok", cuerpo["status"]);
    }

    [Fact]
    public async Task Listo_SinBaseDeDatos_Responde503SinDetalles()
    {
        await using var f = AutenticacionTests.Crear(AutenticacionTests.Secreto).WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<MiParteDbContext>>();
            foreach (var d in s.Where(x => x.ServiceType.IsGenericType
                                           && x.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                                           && x.ServiceType.GenericTypeArguments[0] == typeof(MiParteDbContext)).ToList())
                s.Remove(d);
            // Un puerto donde no escucha nada: la conexión se rechaza al momento.
            s.AddDbContext<MiParteDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Timeout=2;Pooling=false"));
        }));

        var r = await f.CreateClient().GetAsync("/health/listo");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var texto = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"caido\"", texto);
        Assert.DoesNotContain("127.0.0.1", texto);
    }

    [Fact]
    public async Task Registro_AnotaMetodoPlantillaEstadoYMs_SinIdsNiHealth()
    {
        var captura = new CapturaLog();
        await using var f = ConLog(captura);
        var cliente = f.CreateClient();

        await cliente.GetAsync("/health");
        await cliente.GetAsync("/health/listo");
        await cliente.GetAsync($"/api/gastos/{Guid.NewGuid()}");

        var linea = Assert.Single(captura.Lineas, l => l.Texto.StartsWith("Petición "));
        Assert.Equal(LogLevel.Information, linea.Nivel);
        Assert.Matches(@"^Petición GET /api/gastos/\{id:guid\} -> 401 en \d+ ms$", linea.Texto);
        Assert.Contains(captura.Lineas, l => l.Texto.StartsWith("Servicio listo "));
    }

    [Fact]
    public async Task Registro_PeticionQueSuperaElUmbral_SaleComoAviso()
    {
        var captura = new CapturaLog();
        await using var f = ConLog(captura, umbralMs: "0");

        await f.CreateClient().GetAsync("/api/gastos");

        Assert.Contains(captura.Lineas, l => l.Nivel == LogLevel.Warning && l.Texto.StartsWith("Petición GET /api/gastos -> 401"));
    }
}
