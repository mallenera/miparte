using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Hogares;
using MiParte.Web.Pages;

namespace MiParte.Web.Tests;

public class SinHogarTests : BunitContext
{
    private static readonly HogarResumen Nuevo = new(Guid.NewGuid(), "Piso del centro");

    private ApiFalsa _api = null!;

    /// <summary>Registra el cliente contra el servidor falso; el alta responde con el hogar nuevo y /api/yo lo incluye.</summary>
    private void Registrar()
    {
        _api = new ApiFalsa()
            .Responde("POST /api/hogares", HttpStatusCode.Created, Nuevo)
            .Responde("GET /api/yo", HttpStatusCode.OK, new YoResponse(null, [Nuevo], Nuevo));
        var cliente = new CoreApiClient(new HttpClient(_api) { BaseAddress = new Uri("http://localhost:5001/") });
        Services.AddSingleton(cliente);
        Services.AddSingleton(new EstadoHogar(new AlmacenMemoria()));
        Services.AddScoped<ServicioArranque>();
    }

    /// <summary>Rellena nombres y envía el formulario de creación.</summary>
    private static void CrearHogar(IRenderedComponent<SinHogar> c)
    {
        c.Find("#hogar").Change("Piso del centro");
        c.Find("#yo").Change("Ana");
        c.Find("form").Submit();
    }

    private string TipoEnviado()
    {
        using var doc = JsonDocument.Parse(_api.Cuerpos["POST /api/hogares"]);
        return doc.RootElement.GetProperty("tipo").GetString()!;
    }

    [Fact]
    public void El_selector_ofrece_los_cinco_tipos_con_nombres_en_espanol()
    {
        Registrar();

        var c = Render<SinHogar>();

        var nombres = c.FindAll("#tipo option").Select(o => o.TextContent.Trim()).ToList();
        Assert.Equal(["Hogar", "Piso compartido", "Viaje", "Evento", "Otro"], nombres);
    }

    [Fact]
    public void Sin_elegir_tipo_se_marca_hogar_por_defecto_y_se_envia_hogar()
    {
        Registrar();

        var c = Render<SinHogar>();
        Assert.Equal("Familia o personas que conviven y comparten los gastos del día a día.", c.Find("#tipo-ayuda").TextContent);
        CrearHogar(c);

        c.WaitForAssertion(() => Assert.Equal("hogar", TipoEnviado()));
    }

    [Theory]
    [InlineData("piso_compartido")]
    [InlineData("viaje")]
    [InlineData("evento")]
    [InlineData("otro")]
    public void El_tipo_elegido_viaja_en_la_peticion(string tipo)
    {
        Registrar();
        var c = Render<SinHogar>();

        c.Find("#tipo").Change(tipo);
        CrearHogar(c);

        c.WaitForAssertion(() => Assert.Equal(tipo, TipoEnviado()));
    }

    [Fact]
    public void La_explicacion_cambia_con_la_opcion_elegida()
    {
        Registrar();
        var c = Render<SinHogar>();

        c.Find("#tipo").Change(TiposGrupo.Evento);

        Assert.Equal(NombresTiposGrupo.Descripcion(TiposGrupo.Evento), c.Find("#tipo-ayuda").TextContent);
    }

    [Theory]
    [InlineData("hogar", "Hogar")]
    [InlineData("piso_compartido", "Piso compartido")]
    [InlineData("viaje", "Viaje")]
    [InlineData("evento", "Evento")]
    [InlineData("otro", "Otro")]
    [InlineData("condominio", "Hogar")]
    public void Los_nombres_de_tipo_se_resuelven_en_un_solo_sitio(string tipo, string nombre) =>
        Assert.Equal(nombre, NombresTiposGrupo.Nombre(tipo));
}
