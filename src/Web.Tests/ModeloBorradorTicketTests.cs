using MiParte.Contracts;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

public class ModeloBorradorTicketTests
{
    private static readonly Guid Categoria = Guid.NewGuid();
    private static readonly Guid Perfil = Guid.NewGuid();
    private static readonly Guid Pagador = Guid.NewGuid();

    private static ModeloBorradorTicket Modelo(bool comercio = true, bool fecha = true) => new(new LecturaTicketResponse(
        new BorradorGastoDto(23.45m, "Mercadona", new DateOnly(2026, 10, 6), "Supermercado", "Casa", Categoria, Perfil), comercio, fecha));

    [Fact]
    public void Arranca_con_lo_leido_en_formato_espanol_y_sin_avisos()
    {
        var m = Modelo();

        Assert.Equal(("23,45", "Mercadona", new DateOnly(2026, 10, 6)), (m.Importe, m.Concepto, m.Fecha));
        Assert.Empty(m.Avisos);
    }

    [Fact]
    public void Avisa_de_lo_que_no_se_leyo()
    {
        var m = Modelo(comercio: false, fecha: false);

        Assert.Equal(2, m.Avisos.Count);
        Assert.Contains(m.Avisos, a => a.Contains("comercio"));
        Assert.Contains(m.Avisos, a => a.Contains("fecha"));
    }

    [Fact]
    public void Preparar_usa_lo_editado_y_conserva_categoria_y_perfil()
    {
        var m = Modelo();
        m.Importe = "24.5";
        m.Concepto = "  Mercadona Centro ";
        m.Fecha = new DateOnly(2026, 10, 5);

        Assert.Null(m.Preparar(Pagador, out var p));

        Assert.Equal(new ConfirmarGastoRequest(24.5m, "Mercadona Centro", new DateOnly(2026, 10, 5), Pagador, Categoria, Perfil, "Supermercado", "Casa"), p);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("12,345")]
    [InlineData("doce")]
    [InlineData("")]
    [InlineData("99999999999")]
    public void Preparar_rechaza_un_importe_no_valido(string importe)
    {
        var m = Modelo();
        m.Importe = importe;

        Assert.NotNull(m.Preparar(Pagador, out var p));
        Assert.Null(p);
    }

    [Fact]
    public void Preparar_rechaza_un_concepto_vacio_o_demasiado_largo()
    {
        var m = Modelo();
        m.Concepto = "   ";
        Assert.NotNull(m.Preparar(Pagador, out _));

        m.Concepto = new string('a', ModeloAltaGastoChat.MaxConcepto + 1);
        Assert.NotNull(m.Preparar(Pagador, out _));
    }
}
