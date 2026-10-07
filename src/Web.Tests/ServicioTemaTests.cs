using Bunit;
using MiParte.Web.Temas;

namespace MiParte.Web.Tests;

public class ServicioTemaTests
{
    /// <summary>Crea el servicio con un almacén en memoria y un JS simulado.</summary>
    private static (ServicioTema servicio, AlmacenMemoria almacen, BunitJSInterop js) Crear()
    {
        var contexto = new TestContext();
        var almacen = new AlmacenMemoria();
        return (new ServicioTema(almacen, contexto.JSInterop.JSRuntime), almacen, contexto.JSInterop);
    }

    /// <summary>Sin nada guardado se sigue el tema del sistema.</summary>
    [Fact]
    public async Task Sin_tema_guardado_sigue_al_sistema()
    {
        var (servicio, _, _) = Crear();
        await servicio.IniciarAsync();
        Assert.Equal(ServicioTema.Sistema, servicio.Actual);
    }

    /// <summary>Un valor guardado que no es un tema conocido no se aplica.</summary>
    [Fact]
    public async Task Un_tema_desconocido_guardado_se_trata_como_sistema()
    {
        var (servicio, almacen, _) = Crear();
        almacen.Datos[ServicioTema.Clave] = "inventado";
        await servicio.IniciarAsync();
        Assert.Equal(ServicioTema.Sistema, servicio.Actual);
    }

    /// <summary>Elegir un tema lo aplica en la página, lo guarda y avisa a los suscriptores.</summary>
    [Fact]
    public async Task Elegir_un_tema_lo_aplica_lo_guarda_y_avisa()
    {
        var (servicio, almacen, js) = Crear();
        js.SetupVoid("miparteTema.aplicar", "oceano").SetVoidResult();
        var avisos = 0;
        servicio.Cambiado += () => avisos++;

        await servicio.ElegirAsync("oceano");

        Assert.Equal("oceano", servicio.Actual);
        Assert.Equal("oceano", almacen.Datos[ServicioTema.Clave]);
        Assert.Equal(1, avisos);
        js.VerifyInvoke("miparteTema.aplicar");
    }

    /// <summary>Un id que no es de ningún tema no cambia nada ni se guarda.</summary>
    [Fact]
    public async Task Un_tema_inexistente_se_ignora()
    {
        var (servicio, almacen, _) = Crear();
        await servicio.ElegirAsync("no-existe");
        Assert.Equal(ServicioTema.Sistema, servicio.Actual);
        Assert.Empty(almacen.Datos);
    }

    /// <summary>Al iniciar se recupera el tema guardado en la sesión anterior.</summary>
    [Fact]
    public async Task El_tema_guardado_se_recupera_al_iniciar()
    {
        var (servicio, almacen, _) = Crear();
        almacen.Datos[ServicioTema.Clave] = "medianoche";
        await servicio.IniciarAsync();
        Assert.Equal("medianoche", servicio.Actual);
    }
}
