using Bunit;
using Microsoft.JSInterop;
using MiParte.Contracts;
using MiParte.Web.Componentes;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>Tarjeta «Compartir el resumen»: botones según el navegador y acciones contra el JS de compartir.</summary>
public class TarjetaCompartirTests : BunitContext
{
    private static readonly DateOnly Octubre = new(2026, 10, 15);
    private readonly Guid _ana = Guid.NewGuid(), _luis = Guid.NewGuid();

    private LiquidacionResponse ConTransferencia() => new("2026-10",
        [new SaldoMiembroDto(_ana, "Ana", 42.5m), new SaldoMiembroDto(_luis, "Luis", -42.5m)],
        [new TransferenciaDto(_luis, _ana, 42.5m)],
        []);

    private IRenderedComponent<TarjetaCompartir> Renderizar(
        bool puedeCompartir, bool copiadoOk = true, bool compartidoOk = true, JSException? errorCompartir = null)
    {
        JSInterop.Setup<bool>("compartirResumen.puedeCompartir", _ => true).SetResult(puedeCompartir);
        JSInterop.Setup<bool>("compartirResumen.copiar", _ => true).SetResult(copiadoOk);
        var compartir = JSInterop.Setup<bool>("compartirResumen.compartir", _ => true);
        if (errorCompartir is null) compartir.SetResult(compartidoOk);
        else compartir.SetException(errorCompartir);
        return Render<TarjetaCompartir>(p => p
            .Add(c => c.NombreHogar, "Piso")
            .Add(c => c.Mes, Octubre)
            .Add(c => c.Liquidacion, ConTransferencia())
            .Add(c => c.Miembros, new List<MiembroDto> { ApiFalsa.Miembro("Ana", id: _ana), ApiFalsa.Miembro("Luis", id: _luis) }));
    }

    private static AngleSharp.Dom.IElement Boton(IRenderedComponent<TarjetaCompartir> cut, string texto) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == texto);

    [Fact]
    public void Muestra_el_boton_compartir_solo_si_el_navegador_puede_compartir()
    {
        var con = Renderizar(puedeCompartir: true);
        Assert.Contains("Compartir", con.FindAll("button").Select(b => b.TextContent.Trim()));

        var sin = Renderizar(puedeCompartir: false);
        Assert.DoesNotContain("Compartir", sin.FindAll("button").Select(b => b.TextContent.Trim()));
        Assert.Contains("Copiar texto", sin.Markup);
    }

    [Fact]
    public void El_texto_del_resumen_se_muestra_en_el_area_seleccionable()
    {
        var cut = Renderizar(puedeCompartir: false);

        var area = cut.Find("textarea");
        Assert.Equal("Resumen de Piso · octubre 2026\n\nLuis debe a Ana 42,50 €", area.GetAttribute("value") ?? area.TextContent);
        Assert.NotNull(area.GetAttribute("readonly"));
    }

    [Fact]
    public void Copiar_envia_el_texto_al_js_y_confirma_si_funciona()
    {
        var cut = Renderizar(puedeCompartir: false, copiadoOk: true);

        Boton(cut, "Copiar texto").Click();

        var llamada = JSInterop.Invocations["compartirResumen.copiar"].Single();
        Assert.Equal("Resumen de Piso · octubre 2026\n\nLuis debe a Ana 42,50 €", llamada.Arguments[1]);
        cut.WaitForAssertion(() => Assert.Contains("Texto copiado", cut.Find("[role=status]").TextContent));
    }

    [Fact]
    public void Si_el_portapapeles_falla_avisa_de_copiar_a_mano()
    {
        var cut = Renderizar(puedeCompartir: false, copiadoOk: false);

        Boton(cut, "Copiar texto").Click();

        cut.WaitForAssertion(() => Assert.Contains("No se pudo copiar", cut.Find("[role=status]").TextContent));
    }

    [Fact]
    public void Si_la_persona_cancela_compartir_no_cambia_el_aviso_previo()
    {
        var cut = Renderizar(puedeCompartir: true, copiadoOk: true, compartidoOk: false);
        Boton(cut, "Copiar texto").Click();
        cut.WaitForAssertion(() => Assert.Contains("Texto copiado", cut.Find("[role=status]").TextContent));

        Boton(cut, "Compartir").Click();

        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["compartirResumen.compartir"]));
        var aviso = cut.Find("[role=status]").TextContent;
        Assert.Contains("Texto copiado", aviso);
        Assert.DoesNotContain("Resumen enviado", aviso);
        Assert.DoesNotContain("No se pudo compartir", aviso);
    }

    [Fact]
    public void Si_el_navegador_falla_al_compartir_avisa_de_copiar_el_texto_sin_romper_la_pagina()
    {
        var cut = Renderizar(puedeCompartir: true, errorCompartir: new JSException("NotAllowedError"));

        Boton(cut, "Compartir").Click();

        cut.WaitForAssertion(() => Assert.Contains("No se pudo compartir. Copia el texto.", cut.Find("[role=status]").TextContent));
    }

    [Fact]
    public void Los_enlaces_de_whatsapp_y_correo_llevan_el_mensaje_generico_y_se_ven_junto_a_el()
    {
        var cut = Renderizar(puedeCompartir: false);

        var whatsapp = cut.Find("a[href^='https://wa.me/']").GetAttribute("href");
        Assert.Contains(Uri.EscapeDataString(ResumenCompartible.MensajeEnlace), whatsapp);
        Assert.StartsWith("mailto:?subject=", cut.Find("a[href^='mailto:']").GetAttribute("href"));
        Assert.NotNull(cut.Find("textarea"));
    }

    [Fact]
    public void Los_enlaces_no_llevan_importes_ni_nombres_de_miembros_ni_del_hogar()
    {
        var cut = Renderizar(puedeCompartir: false);

        var enlaces = cut.FindAll("a[href^='https://wa.me/'], a[href^='mailto:']")
            .Select(a => a.GetAttribute("href")!)
            .ToList();
        Assert.Equal(2, enlaces.Count);

        foreach (var href in enlaces)
        {
            // Se comprueba también la URL decodificada, por si un dato se hubiera escapado.
            var claro = Uri.UnescapeDataString(href);
            Assert.DoesNotMatch(@"\d", claro);
            Assert.DoesNotContain("€", claro);
            Assert.DoesNotContain("Luis", claro);
            Assert.DoesNotContain("Ana", claro);
            Assert.DoesNotContain("Piso", claro);
        }
    }
}
