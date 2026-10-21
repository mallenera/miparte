using Bunit;
using MiParte.Web.Componentes;
using MiParte.Web.Pages;

namespace MiParte.Web.Tests;

public class ContactoTests : BunitContext
{
    [Fact]
    public void EnlaceAbreCorreoAMipartetuparteConElAsuntoDeLaDuda()
    {
        var cut = Render<Contacto>();

        var enlace = cut.Find("a.btn").GetAttribute("href");

        Assert.Equal("mailto:mipartetuparte@gmail.com?subject=Mi%20parte%2C%20tu%20parte%3A%20duda", enlace);
    }

    [Fact]
    public void ElegirOtroMotivoCambiaElAsuntoDelEnlace()
    {
        var cut = Render<Contacto>();

        cut.Find("select").Change("mejora");

        var enlace = cut.Find("a.btn").GetAttribute("href");
        Assert.Contains("subject=Mi%20parte%2C%20tu%20parte%3A%20propuesta%20de%20mejora", enlace);
        Assert.DoesNotContain("?body=", enlace);
    }

    [Fact]
    public void ElPieDeLaAplicacionEnlazaAContacto()
    {
        var cut = Render<PiePagina>();

        Assert.Equal("contacto", cut.Find("footer a").GetAttribute("href"));
    }
}
