using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Componentes;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class ComponentesTests : TestContext
{
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");
    private static readonly Guid AnaId = Guid.NewGuid(), LuisId = Guid.NewGuid();

    private void Registrar(ApiFalsa api)
    {
        Services.AddSingleton(new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") }));
        Services.AddSingleton(new EstadoHogar(new AlmacenMemoria()));
        Services.AddSingleton<ServicioAvisos>();
    }

    private static ApiFalsa ApiConHogar(bool soyAdmin)
    {
        var miembros = new List<MiembroDto>
        {
            ApiFalsa.Miembro("Ana", rol: soyAdmin ? "admin" : "miembro", esYo: true, id: AnaId),
            ApiFalsa.Miembro("Luis", rol: soyAdmin ? "miembro" : "admin", id: LuisId),
            ApiFalsa.Miembro("Leo", tipo: "a_cargo", vinculado: false, responsable: LuisId),
        };
        var perfiles = new List<PerfilRepartoDto>
        {
            new(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []),
            new(Guid.NewGuid(), "60/40", "porcentaje", [new(AnaId, 60m), new(LuisId, 40m)]),
        };
        return new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, miembros)
            .Responde("GET /api/perfiles", HttpStatusCode.OK, perfiles);
    }

    [Fact]
    public void Hogar_como_admin_muestra_miembros_y_acciones_de_gestion()
    {
        Registrar(ApiConHogar(soyAdmin: true));

        var c = RenderComponent<VistaHogar>();

        Assert.Contains("Ana", c.Markup);
        Assert.Contains("Tú", c.Markup);
        Assert.Contains("A cargo", c.Markup);
        Assert.Contains("Responsable: Luis", c.Markup);
        Assert.NotEmpty(c.FindAll("form.addm"));
        Assert.Null(c.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label") == "Desactivar a Ana")); // no a sí mismo
        Assert.NotNull(c.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label") == "Desactivar a Luis"));
        Assert.Null(c.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label") == "Invitar a Leo")); // a cargo no se invita
    }

    [Fact]
    public void Hogar_como_miembro_normal_oculta_la_gestion()
    {
        Registrar(ApiConHogar(soyAdmin: false));

        var c = RenderComponent<VistaHogar>();

        Assert.Empty(c.FindAll("form.addm"));
        Assert.Contains("Solo un administrador", c.Markup);
        Assert.DoesNotContain(c.FindAll("button"), b => b.GetAttribute("aria-label")?.StartsWith("Desactivar") == true);
        Assert.NotNull(c.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label") == "Renombrar a Ana")); // su propio nombre sí
        Assert.Null(c.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label") == "Renombrar a Luis"));
        Assert.True(c.Find("#card-invite button.btn-orange").HasAttribute("disabled"));
    }

    [Fact]
    public void Invitar_muestra_el_codigo_una_sola_vez_con_su_caducidad()
    {
        var api = ApiConHogar(soyAdmin: true)
            .Responde("POST /api/invitaciones", HttpStatusCode.Created,
                new InvitacionCreada(Guid.NewGuid(), "token-secreto-123", DateTimeOffset.UtcNow.AddDays(7)));
        Registrar(api);
        var c = RenderComponent<VistaHogar>();

        c.Find("#card-invite button.btn-orange").Click();

        Assert.Equal("token-secreto-123", c.Find("#token-invitacion").TextContent);
        Assert.Contains("no se vuelve a mostrar", c.Markup);
        c.Find(".invite button.linkbtn").Click(); // Cerrar aviso
        Assert.DoesNotContain("token-secreto-123", c.Markup);
    }

    [Fact]
    public void Un_409_al_desactivar_se_muestra_con_el_mensaje_de_la_api()
    {
        var api = ApiConHogar(soyAdmin: true)
            .Error($"PUT /api/miembros/{LuisId}", HttpStatusCode.Conflict, "El hogar necesita al menos un administrador.");
        Registrar(api);
        var c = RenderComponent<VistaHogar>();

        c.FindAll("button").First(b => b.GetAttribute("aria-label") == "Desactivar a Luis").Click();
        c.FindAll("button").First(b => b.TextContent.Contains("Sí, desactivar")).Click();

        Assert.Contains("al menos un administrador", c.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Anadir_un_a_cargo_exige_responsable_y_envia_la_peticion()
    {
        var api = ApiConHogar(soyAdmin: true)
            .Responde("POST /api/miembros", HttpStatusCode.Created, ApiFalsa.Miembro("Nico", tipo: "a_cargo", responsable: AnaId));
        Registrar(api);
        var c = RenderComponent<VistaHogar>();

        c.Find("form.addm input").Input("Nico");
        c.Find("form.addm select").Change("a_cargo");
        Assert.True(c.Find("form.addm button.btn").HasAttribute("disabled")); // sin responsable

        c.FindAll("form.addm select")[1].Change(AnaId.ToString());
        c.Find("form.addm").Submit();

        Assert.Contains("POST /api/miembros", api.Recibidas);
        Assert.Contains("\"tipo\":\"a_cargo\"", api.Cuerpos["POST /api/miembros"]);
        Assert.Contains(AnaId.ToString(), api.Cuerpos["POST /api/miembros"]);
    }

    [Fact]
    public void El_editor_de_perfil_bloquea_guardar_si_los_porcentajes_no_suman_100()
    {
        Registrar(ApiConHogar(soyAdmin: true));
        var miembros = new List<MiembroDto> { ApiFalsa.Miembro("Ana", id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId) };
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "60/40", "porcentaje", [new(AnaId, 60m), new(LuisId, 40m)]);
        var c = RenderComponent<EditorPerfil>(p => p.Add(x => x.Perfil, perfil).Add(x => x.Miembros, miembros));

        Assert.False(c.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Contains("✓", c.Find(".sumline").TextContent);

        c.FindAll("input[type=number]")[1].Input("30");

        Assert.True(c.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Contains("debe ser 100", c.Find(".sumline").TextContent);
    }

    [Fact]
    public void Categorias_se_muestran_en_arbol_y_el_409_de_eliminar_sale_en_pantalla()
    {
        var comida = new CategoriaDto(Guid.NewGuid(), "Comida", null, null);
        var super_ = new CategoriaDto(Guid.NewGuid(), "Supermercado", comida.Id, null);
        var api = new ApiFalsa()
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { super_, comida })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, Array.Empty<PerfilRepartoDto>())
            .Error($"DELETE /api/categorias/{comida.Id}", HttpStatusCode.Conflict, "La categoría tiene subcategorías.");
        Registrar(api);

        var c = RenderComponent<VistaCategorias>();

        var nombres = c.FindAll(".c-name").Select(n => n.TextContent).ToList();
        Assert.Equal(["Comida", "Supermercado"], nombres);
        Assert.Single(c.FindAll(".c-name.sub"));

        c.FindAll("button").First(b => b.GetAttribute("aria-label") == "Eliminar Comida").Click();
        c.FindAll("button").First(b => b.TextContent == "Sí").Click();

        Assert.Contains("subcategorías", c.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Anadir_categoria_envia_nombre_padre_y_perfil()
    {
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var api = new ApiFalsa()
            .Responde("GET /api/categorias", HttpStatusCode.OK, Array.Empty<CategoriaDto>())
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("POST /api/categorias", HttpStatusCode.Created, new CategoriaDto(Guid.NewGuid(), "Ocio", null, perfil.Id));
        Registrar(api);
        var c = RenderComponent<VistaCategorias>();

        c.Find("form.addcat input").Input("Ocio");
        c.FindAll("form.addcat select")[1].Change(perfil.Id.ToString());
        c.Find("form.addcat").Submit();

        Assert.Contains("\"nombre\":\"Ocio\"", api.Cuerpos["POST /api/categorias"]);
        Assert.Contains(perfil.Id.ToString(), api.Cuerpos["POST /api/categorias"]);
    }

    [Fact]
    public void Anadir_gasto_envia_importe_categoria_pagador_y_perfil_por_defecto()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Comida", null, perfil.Id);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("POST /api/gastos", HttpStatusCode.Created,
                new GastoResponse(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), 12.5m, cat.Id, ana.Id, perfil.Id, null, null, []));
        Registrar(api);
        var c = RenderComponent<VistaGastos>();

        c.Find("form.addcat input[inputmode=decimal]").Input("12,50");
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/gastos"];
        Assert.Contains("\"importe\":12.5", cuerpo);
        Assert.Contains(cat.Id.ToString(), cuerpo);
        Assert.Contains(ana.Id.ToString(), cuerpo);
        Assert.Contains(perfil.Id.ToString(), cuerpo);
    }
}
