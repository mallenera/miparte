using System.Net;
using MiParte.Assistant.Api.Chat;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Contracts;

namespace MiParte.Assistant.Tests;

/// <summary>
/// Alta de gastos desde el chat: la herramienta solo propone un borrador, la confirmación es la única que guarda, y el
/// streaming entrega el texto por trozos. Todo con el modelo y los datos falsos.
/// </summary>
public class AltaGastoTests
{
    private static readonly List<MensajeChatDto> Conversacion = [new("user", "Apunta 12 € de café el 14 de julio")];
    private static readonly DateOnly Fecha = new(2026, 7, 14);

    private static ServicioChat Chat(IClienteModelo modelo) => new(modelo, Ayuda.Opciones(), Ayuda.Reloj());

    [Fact]
    public async Task Proponer_conPredeterminados_devuelveElBorradorSinGuardarNada()
    {
        var datos = new DatosFalsos();
        var modelo = new ModeloFalso(
            ModeloFalso.Llama("proponer_gasto", "{\"importe\":12,\"concepto\":\"Café\",\"fecha\":\"2026-07-14\"}"),
            ModeloFalso.Texto("Lo tienes preparado; confírmalo si está bien."));

        var r = await Chat(modelo).ResponderAsync(Conversacion, datos, default);

        Assert.Equal(new BorradorGastoDto(12m, "Café", Fecha, "Supermercado", "Casa", DatosFalsos.Super, DatosFalsos.PerfilCasa), r.Borrador);
        Assert.Contains("proponer_gasto", r.Herramientas);
        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task Proponer_sinCategoriaPredeterminada_daUnErrorQueElModeloPuedeExplicar()
    {
        var datos = new DatosFalsos { SuperPredeterminada = false };
        var modelo = new ModeloFalso(
            ModeloFalso.Llama("proponer_gasto", "{\"importe\":12,\"concepto\":\"Café\",\"fecha\":\"2026-07-14\"}"),
            ModeloFalso.Texto("Antes hay que marcar una categoría."));

        var r = await Chat(modelo).ResponderAsync(Conversacion, datos, default);

        Assert.Null(r.Borrador);
        var resultado = modelo.Solicitudes[1].Mensajes[^1].Bloques.OfType<BloqueResultadoHerramienta>().Single();
        Assert.True(resultado.EsError);
        Assert.Contains("predeterminada para gastos", resultado.Contenido);
        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task Proponer_sinFecha_esErrorYNoHayBorrador()
    {
        var modelo = new ModeloFalso(
            ModeloFalso.Llama("proponer_gasto", "{\"importe\":12,\"concepto\":\"Café\"}"),
            ModeloFalso.Texto("¿Qué día fue?"));

        var r = await Chat(modelo).ResponderAsync(Conversacion, new DatosFalsos(), default);

        Assert.Null(r.Borrador);
        var resultado = modelo.Solicitudes[1].Mensajes[^1].Bloques.OfType<BloqueResultadoHerramienta>().Single();
        Assert.True(resultado.EsError);
        Assert.Contains("AAAA-MM-DD", resultado.Contenido);
    }

    [Fact]
    public async Task Proponer_conMasDeDosDecimales_esError()
    {
        var modelo = new ModeloFalso(
            ModeloFalso.Llama("proponer_gasto", "{\"importe\":12.345,\"concepto\":\"Café\",\"fecha\":\"2026-07-14\"}"),
            ModeloFalso.Texto("El importe tiene que tener céntimos."));

        var r = await Chat(modelo).ResponderAsync(Conversacion, new DatosFalsos(), default);

        Assert.Null(r.Borrador);
        var resultado = modelo.Solicitudes[1].Mensajes[^1].Bloques.OfType<BloqueResultadoHerramienta>().Single();
        Assert.Contains("2 decimales", resultado.Contenido);
    }

    [Fact]
    public async Task Confirmar_usaLosPredeterminadosYElPagadorElegido()
    {
        var datos = new DatosFalsos();

        var gasto = await new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12m, "  Café  ", Fecha, DatosFalsos.Luis, DatosFalsos.Super, DatosFalsos.PerfilCasa), datos, default);

        var creado = Assert.Single(datos.GastosCreados);
        Assert.Equal(12m, creado.Importe);
        Assert.Equal("Café", creado.Concepto);
        Assert.Equal(Fecha, creado.Fecha);
        Assert.Equal(DatosFalsos.Super, creado.CategoriaId);
        Assert.Equal(DatosFalsos.PerfilCasa, creado.PerfilRepartoId);
        Assert.Equal(DatosFalsos.Luis, creado.PagadoPor);
        Assert.Equal(12m, gasto.Importe);
    }

    [Fact]
    public async Task Confirmar_sinPerfilPredeterminado_esConflictoYNoGuarda()
    {
        var datos = new DatosFalsos { PerfilPredeterminado = false };

        var e = await Assert.ThrowsAsync<GastoRechazadoException>(() => new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12m, "Café", Fecha, DatosFalsos.Luis, DatosFalsos.Super, DatosFalsos.PerfilCasa), datos, default));

        Assert.Equal(HttpStatusCode.Conflict, e.Codigo);
        Assert.Contains("perfil de reparto", e.Message);
        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task Confirmar_conMasDeDosDecimales_esInvalidoAntesDeLlamarACore()
    {
        var datos = new DatosFalsos();

        await Assert.ThrowsAsync<SolicitudInvalidaException>(() => new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12.345m, "Café", Fecha, DatosFalsos.Luis, DatosFalsos.Super, DatosFalsos.PerfilCasa), datos, default));

        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task Confirmar_siLaCategoriaPredeterminadaHaCambiado_rechazaSinGuardar()
    {
        // La propuesta mostró «Supermercado», pero la predeterminada actual es otra categoría (en el hogar, la única marcada).
        var datos = new DatosFalsos();

        var e = await Assert.ThrowsAsync<GastoRechazadoException>(() => new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12m, "Café", Fecha, DatosFalsos.Luis, DatosFalsos.Ocio, DatosFalsos.PerfilCasa), datos, default));

        Assert.Equal(HttpStatusCode.Conflict, e.Codigo);
        Assert.Equal(AltaGasto.PredeterminadosCambiados, e.Message);
        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task Confirmar_siElPerfilPredeterminadoHaCambiado_rechazaSinGuardar()
    {
        var datos = new DatosFalsos();

        var e = await Assert.ThrowsAsync<GastoRechazadoException>(() => new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12m, "Café", Fecha, DatosFalsos.Luis, DatosFalsos.Super, Guid.NewGuid()), datos, default));

        Assert.Equal(HttpStatusCode.Conflict, e.Codigo);
        Assert.Empty(datos.GastosCreados);
    }

    [Fact]
    public async Task Confirmar_siCoincideConLoMostrado_guarda()
    {
        var datos = new DatosFalsos();

        await new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12m, "Café", Fecha, DatosFalsos.Luis, DatosFalsos.Super, DatosFalsos.PerfilCasa), datos, default);

        Assert.Single(datos.GastosCreados);
    }

    [Fact]
    public async Task Confirmar_queCoreRechaza_llegaConSuMensaje()
    {
        var datos = new DatosFalsos
        {
            FalloAlGuardar = new GastoRechazadoException(HttpStatusCode.Forbidden, "No tienes permiso para registrar gastos."),
        };

        var e = await Assert.ThrowsAsync<GastoRechazadoException>(() => new ServicioAltaGasto().ConfirmarAsync(
            new ConfirmarGastoRequest(12m, "Café", Fecha, DatosFalsos.Luis, DatosFalsos.Super, DatosFalsos.PerfilCasa), datos, default));

        Assert.Equal(HttpStatusCode.Forbidden, e.Codigo);
        Assert.Equal("No tienes permiso para registrar gastos.", e.Message);
    }

    [Fact]
    public async Task Streaming_entregaElTextoDeLaRespuestaYLaMismaRespuestaFinal()
    {
        var trozos = new List<string>();
        var modelo = new ModeloFalso(ModeloFalso.Texto("Hola, ¿qué tal?"));

        var r = await Chat(modelo).ResponderAsync(Conversacion, new DatosFalsos(), t =>
        {
            trozos.Add(t);
            return Task.CompletedTask;
        }, default);

        Assert.Equal(["Hola, ¿qué tal?"], trozos);
        Assert.Equal("Hola, ¿qué tal?", r.Respuesta);
    }
}
