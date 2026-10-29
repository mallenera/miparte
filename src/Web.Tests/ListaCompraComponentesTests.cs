using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Web.Api;
using MiParte.Web.Componentes;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

/// <summary>La lista de la compra renderizada: la del hogar (con sesión y permisos) y la que se abre desde un enlace QR.</summary>
public class ListaCompraComponentesTests : BunitContext
{
    private static readonly Guid LecheId = Guid.NewGuid(), PanId = Guid.NewGuid();
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");

    private static readonly ProductoCompraDto Leche = new(LecheId, "Leche", 2, false);
    private static readonly ProductoCompraDto Pan = new(PanId, "Pan", 1, true);

    private EstadoHogar _hogar = null!;

    private ApiFalsa Registrar(ApiFalsa api, IEnumerable<string>? permisos = null)
    {
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        _hogar = new EstadoHogar(new AlmacenMemoria());
        _hogar.AplicarAsync(new YoResponse(null, [Casa], Casa)).GetAwaiter().GetResult();
        Services.AddSingleton(cliente);
        Services.AddSingleton(_hogar);
        PermisosDePrueba.Registrar(Services, cliente, _hogar, permisos);
        return api;
    }

    private static ApiFalsa ApiConLista(bool activa = true, params ProductoCompraDto[] productos)
        => new ApiFalsa().Responde("GET /api/lista-compra", HttpStatusCode.OK, new ListaCompraResponse(activa, productos));

    [Fact]
    public void Desactivada_SinHogarFunciones_ExplicaQueNoEstaActivadaYNoOfreceActivarla()
    {
        Registrar(ApiConLista(activa: false), [CatalogoPermisos.ListaCompra]);

        var c = Render<VistaListaCompra>();

        Assert.Contains("no está activada", c.Markup);
        Assert.DoesNotContain("Activar la lista de la compra", c.Markup);
        Assert.Empty(c.FindAll("form.addcat"));
    }

    [Fact]
    public void Desactivada_ConHogarFunciones_LaActivaYElHogarLoRefleja()
    {
        var activa = false;
        var api = Registrar(new ApiFalsa()
            .Responde("GET /api/lista-compra", _ => ApiFalsa.Json(HttpStatusCode.OK, new ListaCompraResponse(activa, [])))
            .Responde("PUT /api/lista-compra/activacion", _ =>
            {
                activa = true;
                return ApiFalsa.Json(HttpStatusCode.OK, new { activa = true });
            }));
        var c = Render<VistaListaCompra>();

        c.Find("button.btn").Click();

        Assert.Contains("\"activa\":true", api.Cuerpos["PUT /api/lista-compra/activacion"]);
        Assert.True(_hogar.HogarActual!.ListaCompraActiva);
        Assert.Contains("La lista está vacía.", c.Markup);
    }

    [Fact]
    public void Activada_SeparaLosPendientesDeLosDelCarro_ConSusCantidades()
    {
        Registrar(ApiConLista(true, Leche, Pan));

        var c = Render<VistaListaCompra>();

        Assert.Contains("Leche", c.Markup);
        Assert.Contains("× 2", c.Markup);
        Assert.Contains("En el carro", c.Markup);
        var filas = c.FindAll(".gastorow");
        Assert.Equal(2, filas.Count);
        Assert.Contains("Leche", filas[0].TextContent);
        Assert.Contains("Pan", filas[1].TextContent);
        // Un producto pendiente se pinta sin marcar y uno en el carro, marcado (un atributo checked="false" lo marcaría igualmente).
        var casillas = c.FindAll("input[type=checkbox]");
        Assert.False(casillas[0].HasAttribute("checked"));
        Assert.True(casillas[1].HasAttribute("checked"));
    }

    [Fact]
    public void ListaVacia_LoDice()
    {
        Registrar(ApiConLista());

        var c = Render<VistaListaCompra>();

        Assert.Contains("La lista está vacía.", c.Markup);
        Assert.DoesNotContain("En el carro", c.Markup);
    }

    [Fact]
    public void Anadir_EnviaNombreYCantidad_YElBotonEsperaAUnNombreValido()
    {
        var api = Registrar(ApiConLista().Responde("POST /api/lista-compra/productos", HttpStatusCode.Created, new ProductoCompraDto(Guid.NewGuid(), "Huevos", 6, false)));
        var c = Render<VistaListaCompra>();
        Assert.True(c.Find("form.addcat button[type=submit]").HasAttribute("disabled"));

        c.Find("form.addcat input[maxlength='100']").Input("Huevos");
        c.Find("form.addcat input[inputmode='numeric']").Input("6");
        Assert.False(c.Find("form.addcat button[type=submit]").HasAttribute("disabled"));
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/lista-compra/productos"];
        Assert.Contains("\"nombre\":\"Huevos\"", cuerpo);
        Assert.Contains("\"cantidad\":6", cuerpo);
    }

    [Fact]
    public void Anadir_ConCantidadInvalida_NoSePuedeEnviar()
    {
        Registrar(ApiConLista());
        var c = Render<VistaListaCompra>();

        c.Find("form.addcat input[maxlength='100']").Input("Huevos");
        c.Find("form.addcat input[inputmode='numeric']").Input("0");

        Assert.True(c.Find("form.addcat button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void Marcar_EnviaElCambioDeEstadoDelProducto()
    {
        var api = Registrar(ApiConLista(true, Leche, Pan)
            .Responde($"PUT /api/lista-compra/productos/{LecheId}", HttpStatusCode.OK, Leche with { Comprado = true })
            .Responde($"PUT /api/lista-compra/productos/{PanId}", HttpStatusCode.OK, Pan with { Comprado = false }));
        var c = Render<VistaListaCompra>();

        c.FindAll("input[type=checkbox]")[0].Change(true);
        Assert.Contains("\"comprado\":true", api.Cuerpos[$"PUT /api/lista-compra/productos/{LecheId}"]);

        c.FindAll("input[type=checkbox]")[1].Change(false);
        Assert.Contains("\"comprado\":false", api.Cuerpos[$"PUT /api/lista-compra/productos/{PanId}"]);
    }

    [Fact]
    public void Borrar_SoloLoOfreceQuienTieneElPermiso()
    {
        var api = Registrar(ApiConLista(true, Leche).Responde($"DELETE /api/lista-compra/productos/{LecheId}", HttpStatusCode.NoContent), [CatalogoPermisos.ListaCompra]);
        var c = Render<VistaListaCompra>();

        c.Find("button.linkbtn.danger").Click();

        Assert.Contains($"DELETE /api/lista-compra/productos/{LecheId}", api.Recibidas);
    }

    [Fact]
    public void SinElPermisoListaCompra_VeLaListaPeroNoLaEdita()
    {
        Registrar(ApiConLista(true, Leche), []);

        var c = Render<VistaListaCompra>();

        Assert.Contains("Leche", c.Markup);
        Assert.Empty(c.FindAll("form.addcat"));
        Assert.Empty(c.FindAll("button.linkbtn.danger"));
        Assert.DoesNotContain("Compartir por QR", c.Markup);
    }

    [Fact]
    public void CompartirPorQr_MuestraElCodigoYLaDireccionDelEnlace_YSePuedeRevocar()
    {
        var enlaceId = Guid.NewGuid();
        var api = Registrar(ApiConLista()
            .Responde("POST /api/lista-compra/enlaces", HttpStatusCode.Created, new EnlaceListaCreado(enlaceId, "tok123", DateTimeOffset.UtcNow.AddDays(7)))
            .Responde("POST /api/lista-compra/qr", HttpStatusCode.OK, new QrListaResponse("<svg viewBox=\"0 0 1 1\"><rect/></svg>"))
            .Responde($"DELETE /api/lista-compra/enlaces/{enlaceId}", HttpStatusCode.NoContent));
        var c = Render<VistaListaCompra>();
        Assert.Contains("Compartir por QR", c.Markup);

        c.FindAll("button.btn").Last().Click();

        Assert.Contains("/compra/tok123", api.Cuerpos["POST /api/lista-compra/qr"]);
        Assert.NotNull(c.Find(".qr-lista svg"));
        Assert.Contains("/compra/tok123", c.Find("a[href*='/compra/tok123']").GetAttribute("href"));

        c.Find("button.linkbtn.danger").Click();

        Assert.Contains($"DELETE /api/lista-compra/enlaces/{enlaceId}", api.Recibidas);
        Assert.Empty(c.FindAll(".qr-lista"));
    }

    private static EnlaceListaDto EnlaceDto(Guid id, string estado, DateTimeOffset? caduca) =>
        new(id, AnaId, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), caduca, estado);

    private static readonly Guid AnaId = Guid.NewGuid();

    [Fact]
    public void Lista_los_enlaces_creados_con_su_estado_y_solo_deja_revocar_los_vigentes()
    {
        Guid vigente = Guid.NewGuid(), revocado = Guid.NewGuid(), caducado = Guid.NewGuid();
        var api = Registrar(ApiConLista()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", esYo: true, id: AnaId) })
            .Responde("GET /api/lista-compra/enlaces", HttpStatusCode.OK, new[]
            {
                EnlaceDto(vigente, "vigente", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)),
                EnlaceDto(revocado, "revocado", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)),
                EnlaceDto(caducado, "caducado", new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero)),
            }));

        var c = Render<VistaListaCompra>();

        Assert.Contains("Enlaces creados", c.Markup);
        var filas = c.FindAll(".gastorow").Where(f => f.TextContent.Contains("Creado el")).ToList();
        Assert.Equal(3, filas.Count);
        Assert.Contains("por Ana", filas[0].TextContent);
        Assert.Contains("Vigente hasta el 03/11/2026", filas[0].TextContent);
        Assert.Contains("Revocado", filas[1].TextContent);
        Assert.Contains("Caducó el 27/10/2026", filas[2].TextContent);
        // Solo el vigente tiene botón de revocar.
        Assert.Single(c.FindAll("button.linkbtn.danger").Where(b => b.TextContent == "Revocar"));
        Assert.Contains("El código de un enlace solo se ve al crearlo", c.Markup);
    }

    [Fact]
    public void Revoca_un_enlace_antiguo_desde_la_lista()
    {
        var vigente = Guid.NewGuid();
        var api = Registrar(ApiConLista()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", esYo: true, id: AnaId) })
            .Responde("GET /api/lista-compra/enlaces", HttpStatusCode.OK, new[] { EnlaceDto(vigente, "vigente", null) })
            .Responde($"DELETE /api/lista-compra/enlaces/{vigente}", HttpStatusCode.NoContent));
        var c = Render<VistaListaCompra>();

        Assert.Contains("Vigente", c.Find(".gastorow").TextContent);
        c.Find("button.linkbtn.danger").Click();

        Assert.Contains($"DELETE /api/lista-compra/enlaces/{vigente}", api.Recibidas);
    }

    [Fact]
    public void Sin_el_permiso_de_la_lista_no_se_ven_los_enlaces()
    {
        var api = Registrar(ApiConLista()
            .Responde("GET /api/lista-compra/enlaces", HttpStatusCode.OK, new[] { EnlaceDto(Guid.NewGuid(), "vigente", null) }), []);

        var c = Render<VistaListaCompra>();

        Assert.DoesNotContain("Enlaces creados", c.Markup);
    }

    [Fact]
    public void Sin_enlaces_creados_no_aparece_la_seccion()
    {
        Registrar(ApiConLista());

        var c = Render<VistaListaCompra>();

        Assert.DoesNotContain("Enlaces creados", c.Markup);
        Assert.Contains("Crear código QR", c.Markup);
    }

    [Fact]
    public void EnlaceQr_AbreLaListaSinSesion_ConLaCabeceraDelToken_YSinGestion()
    {
        var api = Registrar(new ApiFalsa()
            .Responde("GET /api/lista-compra/publica", HttpStatusCode.OK, new ListaCompraResponse(true, [Leche, Pan]))
            .Responde($"PUT /api/lista-compra/publica/productos/{LecheId}", HttpStatusCode.OK, Leche with { Comprado = true })
            .Responde("POST /api/lista-compra/publica/productos", HttpStatusCode.Created, new ProductoCompraDto(Guid.NewGuid(), "Agua", 1, false)));

        var c = Render<VistaListaCompra>(p => p.Add(x => x.Token, "tok123"));

        Assert.Equal("tok123", api.Enlaces["GET /api/lista-compra/publica"]);
        Assert.Contains("Leche", c.Markup);
        // Sin sesión no se borra, ni se comparte ni se activa nada.
        Assert.Empty(c.FindAll("button.linkbtn.danger"));
        Assert.DoesNotContain("Compartir por QR", c.Markup);

        c.FindAll("input[type=checkbox]")[0].Change(true);
        Assert.Equal("tok123", api.Enlaces[$"PUT /api/lista-compra/publica/productos/{LecheId}"]);

        c.Find("form.addcat input[maxlength='100']").Input("Agua");
        c.Find("form.addcat").Submit();
        Assert.Equal("tok123", api.Enlaces["POST /api/lista-compra/publica/productos"]);
        Assert.DoesNotContain("GET /api/lista-compra", api.Recibidas);
    }

    [Fact]
    public void EnlaceQr_ConUnTokenInvalido_MuestraElError()
    {
        Registrar(new ApiFalsa().Error("GET /api/lista-compra/publica", HttpStatusCode.NotFound, "El enlace no existe."));

        var c = Render<VistaListaCompra>(p => p.Add(x => x.Token, "malo"));

        Assert.Contains("El enlace no existe.", c.Find(".aviso.err").TextContent);
        Assert.Empty(c.FindAll("form.addcat"));
    }

    [Fact]
    public void EnlaceQr_CaducadoOLaListaDesactivada_MuestraElMotivo()
    {
        Registrar(new ApiFalsa().Error("GET /api/lista-compra/publica", HttpStatusCode.Conflict, "El enlace de la lista ha caducado o se ha revocado."));

        var c = Render<VistaListaCompra>(p => p.Add(x => x.Token, "viejo"));

        Assert.Contains("caducado", c.Find(".aviso.err").TextContent);
    }

    [Fact]
    public void ErrorAlAnadir_QuedaVisible_YLaListaNoCambia()
    {
        Registrar(ApiConLista(true, Leche).Error("POST /api/lista-compra/productos", HttpStatusCode.Conflict, "La lista de la compra no está activada en este hogar."));
        var c = Render<VistaListaCompra>();

        c.Find("form.addcat input[maxlength='100']").Input("Pan");
        c.Find("form.addcat").Submit();

        Assert.Contains("no está activada", c.Find(".aviso.err").TextContent);
        Assert.Contains("Leche", c.Markup);
    }
}
