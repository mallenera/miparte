using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Ajustes del proceso de pruebas. Cada prueba HTTP levanta su propia aplicación (<c>WebApplicationFactory</c>) y, por defecto, cada una
/// vigila sus ficheros <c>appsettings*.json</c> con un <c>FileSystemWatcher</c>: en Linux cada uno gasta una instancia de inotify, y el
/// límite por usuario (128 en CI) se agota con unas cientos de pruebas («The configured user limit (128) on the number of inotify
/// instances has been reached»). Con la recarga desactivada no se crea ningún vigilante.
/// </summary>
public static class ConfiguracionDePruebas
{
    /// <summary>Se ejecuta al cargar el ensamblado de pruebas, antes de crear ninguna aplicación.</summary>
    [ModuleInitializer]
    public static void DesactivarLaRecargaDeConfiguracion()
        => Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}

/// <summary>Comprueba que los ajustes de pruebas surten efecto en las aplicaciones que levantan las pruebas HTTP.</summary>
public class ConfiguracionDePruebasTests
{
    [Fact]
    public void LasAplicacionesDePruebaNoVigilanSusFicherosDeConfiguracion()
    {
        using var fabrica = Crear(Secreto);
        _ = fabrica.Server; // arranca la aplicación

        var raiz = (IConfigurationRoot)fabrica.Services.GetRequiredService<IConfiguration>();
        var ficheros = raiz.Providers.OfType<JsonConfigurationProvider>().ToList();

        Assert.NotEmpty(ficheros);
        Assert.All(ficheros, f => Assert.False(f.Source.ReloadOnChange, $"{f.Source.Path} se vigila con un FileSystemWatcher"));
    }
}
