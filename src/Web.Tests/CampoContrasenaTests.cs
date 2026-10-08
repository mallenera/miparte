using Bunit;
using MiParte.Web.Componentes;

namespace MiParte.Web.Tests;

public class CampoContrasenaTests : TestContext
{
    [Fact]
    public void Empieza_oculta_y_el_icono_alterna_la_visibilidad()
    {
        var valor = "secreto1";
        var cut = RenderComponent<CampoContrasena>(p => p.Add(c => c.Id, "pwd").Add(c => c.Value, "secreto1").Add(c => c.ValueExpression, () => valor));
        Assert.Equal("password", cut.Find("input").GetAttribute("type"));
        Assert.Equal("Mostrar contraseña", cut.Find("button").GetAttribute("aria-label"));

        cut.Find("button").Click();

        Assert.Equal("text", cut.Find("input").GetAttribute("type"));
        Assert.Equal("Ocultar contraseña", cut.Find("button").GetAttribute("aria-label"));
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-pressed"));

        cut.Find("button").Click();
        Assert.Equal("password", cut.Find("input").GetAttribute("type"));
    }
}
