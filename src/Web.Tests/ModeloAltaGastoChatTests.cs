using MiParte.Web.Modelos;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

public class ModeloAltaGastoChatTests
{
    [Fact]
    public void El_importe_con_coma_es_valido_y_pasa_a_preguntar_el_concepto()
    {
        var alta = new ModeloAltaGastoChat();

        var error = alta.Responder("12,50");

        Assert.Null(error);
        Assert.Equal(ModeloAltaGastoChat.Paso.Concepto, alta.PasoActual);
        Assert.Equal(12.5m, alta.Importe);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cero")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1,234")]
    public void Un_importe_no_valido_no_avanza(string texto)
    {
        var alta = new ModeloAltaGastoChat();

        var error = alta.Responder(texto);

        Assert.Equal(EntradaImporte.MensajeInvalido, error);
        Assert.Equal(ModeloAltaGastoChat.Paso.Importe, alta.PasoActual);
    }

    [Fact]
    public void Un_importe_mayor_que_el_maximo_del_asistente_no_avanza()
    {
        var alta = new ModeloAltaGastoChat();

        var error = alta.Responder("10000000000");

        Assert.NotNull(error);
        Assert.Equal(ModeloAltaGastoChat.Paso.Importe, alta.PasoActual);
    }

    [Fact]
    public void Un_concepto_vacio_o_demasiado_largo_no_avanza()
    {
        var alta = new ModeloAltaGastoChat();
        alta.Responder("5");

        Assert.NotNull(alta.Responder("   "));
        Assert.NotNull(alta.Responder(new string('a', ModeloAltaGastoChat.MaxConcepto + 1)));
        Assert.Equal(ModeloAltaGastoChat.Paso.Concepto, alta.PasoActual);
    }

    [Fact]
    public void La_fecha_en_formato_DDMMYY_se_interpreta_y_completa_el_alta()
    {
        var alta = new ModeloAltaGastoChat();
        alta.Responder("12");
        alta.Responder("Café");

        var error = alta.Responder("23/10/26");

        Assert.Null(error);
        Assert.Equal(ModeloAltaGastoChat.Paso.Completa, alta.PasoActual);
        Assert.Equal(new DateOnly(2026, 10, 23), alta.Fecha);
    }

    [Theory]
    [InlineData("2026-10-23")]
    [InlineData("23-10-26")]
    [InlineData("31/02/26")]
    [InlineData("")]
    public void Una_fecha_en_otro_formato_no_avanza(string texto)
    {
        var alta = new ModeloAltaGastoChat();
        alta.Responder("12");
        alta.Responder("Café");

        var error = alta.Responder(texto);

        Assert.NotNull(error);
        Assert.Equal(ModeloAltaGastoChat.Paso.Fecha, alta.PasoActual);
    }

    [Fact]
    public void El_mensaje_para_el_asistente_lleva_importe_concepto_y_fecha_en_formato_tecnico()
    {
        var alta = new ModeloAltaGastoChat();
        alta.Responder("12,5");
        alta.Responder("Café");
        alta.Responder("23/10/26");

        var mensaje = alta.MensajeParaAsistente();

        Assert.Contains("importe 12.5 €", mensaje);
        Assert.Contains("concepto «Café»", mensaje);
        Assert.Contains("fecha 2026-10-23", mensaje);
        Assert.Contains("proponer_gasto", mensaje);
    }
}
