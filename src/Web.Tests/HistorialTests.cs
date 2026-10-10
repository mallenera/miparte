using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Auditoria;
using MiParte.Web.Componentes;

namespace MiParte.Web.Tests;

public class HistorialTests : BunitContext
{
    private static readonly Guid AnaId = Guid.NewGuid(), LuisId = Guid.NewGuid();
    private static readonly List<MiembroDto> Miembros =
    [
        ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId),
        ApiFalsa.Miembro("Luis", id: LuisId),
    ];

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static EventoAuditoriaDto Evento(string accion, string entidad, string? antes, string? despues, string autor = "Ana",
        DateTimeOffset? cuando = null) =>
        new(Guid.NewGuid(), cuando ?? new DateTimeOffset(2026, 10, 8, 10, 30, 0, TimeSpan.Zero), Guid.NewGuid(), autor, accion, entidad,
            Guid.NewGuid(), antes is null ? null : Json(antes), despues is null ? null : Json(despues));

    private ApiFalsa Registrar(ApiFalsa api)
    {
        api.Responde("GET /api/miembros", HttpStatusCode.OK, Miembros);
        Services.AddSingleton(new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") }));
        return api;
    }

    private IRenderedComponent<PanelHistorial> Panel() => Render<PanelHistorial>();

    [Fact]
    public void Frase_describe_la_accion_y_el_nombre()
    {
        Assert.Equal("creó un gasto «Compra»", TextoAuditoria.Frase(Evento("crear", "gasto", null, """{"concepto":"Compra"}""")));
        Assert.Equal("borró una categoría «Ocio»", TextoAuditoria.Frase(Evento("borrar", "categoria", """{"nombre":"Ocio"}""", null)));
        Assert.Equal("editó un perfil de reparto", TextoAuditoria.Frase(Evento("editar", "perfil_reparto", """{"detalle":[]}""", """{"detalle":[{}]}""")));
        Assert.Equal("aceptó una invitación", TextoAuditoria.Frase(Evento("usar", "invitacion", null, null)));
    }


    [Fact]
    public void Cambios_de_permisos_se_muestran_con_el_texto_del_catalogo()
    {
        var e = Evento("editar", "miembro", """{"permisos":["gastos.crear"]}""", """{"permisos":["gastos.crear","historial.ver"]}""");

        var cambios = TextoAuditoria.Cambios(e, _ => null);

        Assert.Contains(new CambioAuditoria("Permisos", "Crear gastos", "Crear gastos, Ver el historial de cambios"), cambios);
    }
    [Fact]
    public void Cambios_muestra_antes_y_despues_oculta_ids_internos_y_resuelve_miembros()
    {
        var e = Evento("editar", "gasto",
            $$"""{"importe":100,"categoriaId":"{{Guid.NewGuid()}}","pagadoPor":"{{AnaId}}","aCargoCuentaComun":false}""",
            $$"""{"importe":120.5,"categoriaId":"{{Guid.NewGuid()}}","pagadoPor":"{{LuisId}}","aCargoCuentaComun":true}""");

        var cambios = TextoAuditoria.Cambios(e, id => Miembros.FirstOrDefault(m => m.Id == id)?.Nombre);

        Assert.DoesNotContain(cambios, c => c.Campo.Contains("ategor"));
        Assert.Contains(new CambioAuditoria("Importe", "100", "120,5"), cambios);
        Assert.Contains(new CambioAuditoria("Pagado por", "Ana", "Luis"), cambios);
        Assert.Contains(new CambioAuditoria("A cargo de la cuenta común", "No", "Sí"), cambios);
    }

    [Fact]
    public void Cambios_omite_un_miembro_que_no_se_puede_resolver_y_resume_las_listas()
    {
        var e = Evento("crear", "gasto", null, $$"""{"pagadoPor":"{{Guid.NewGuid()}}","repartos":[{},{}]}""");

        var cambios = TextoAuditoria.Cambios(e, _ => null);

        Assert.Equal([new CambioAuditoria("Reparto", null, "2 líneas")], cambios);
    }

    [Fact]
    public void Un_reparto_o_detalle_muestra_miembro_y_valor_de_cada_linea()
    {
        var e = Evento("editar", "perfil_reparto",
            $$"""{"detalle":[{"miembroId":"{{AnaId}}","valor":60},{"miembroId":"{{LuisId}}","valor":40}]}""",
            $$"""{"detalle":[{"miembroId":"{{AnaId}}","valor":50},{"miembroId":"{{LuisId}}","valor":50.5}]}""");

        var cambios = TextoAuditoria.Cambios(e, id => Miembros.FirstOrDefault(m => m.Id == id)?.Nombre);

        Assert.Equal([new CambioAuditoria("Detalle", "Ana 60, Luis 40", "Ana 50, Luis 50,5")], cambios);
    }

    [Fact]
    public void Un_reparto_de_gasto_usa_el_importe_asumido_y_un_miembro_desconocido_no_se_oculta()
    {
        var e = Evento("crear", "gasto", null, $$"""{"repartos":[{"miembroId":"{{AnaId}}","importeAsumido":21},{"miembroId":"{{Guid.NewGuid()}}","importeAsumido":9}]}""");

        var cambios = TextoAuditoria.Cambios(e, id => Miembros.FirstOrDefault(m => m.Id == id)?.Nombre);

        Assert.Equal([new CambioAuditoria("Reparto", null, "Ana 21, otro miembro 9")], cambios);
    }

    [Fact]
    public void El_panel_pide_tambien_los_miembros_desactivados_para_poner_nombre()
    {
        var api = Registrar(new ApiFalsa().Responde("GET /api/auditoria", HttpStatusCode.OK, new List<EventoAuditoriaDto>()));

        Panel();

        Assert.Contains("/api/miembros?incluirInactivos=true", api.Consultas);
    }

    [Fact]
    public void El_panel_lista_los_eventos_con_autor_frase_y_detalle()
    {
        Registrar(new ApiFalsa().Responde("GET /api/auditoria", HttpStatusCode.OK, new[]
        {
            Evento("crear", "gasto", null, """{"concepto":"Compra","importe":42}"""),
            Evento("borrar", "categoria", """{"nombre":"Ocio"}""", null, autor: "Luis"),
        }));

        var c = Panel();

        var filas = c.FindAll("li.hist-item");
        Assert.Equal(2, filas.Count);
        Assert.Contains("Ana", filas[0].TextContent);
        Assert.Contains("creó un gasto «Compra»", filas[0].TextContent);
        Assert.Contains("Importe", filas[0].QuerySelector("dl")!.TextContent);
        Assert.Contains("Luis", filas[1].TextContent);
        Assert.DoesNotContain(c.FindAll("button"), b => b.TextContent.Contains("Cargar más"));
    }

    [Fact]
    public void Sin_eventos_muestra_el_estado_vacio()
    {
        Registrar(new ApiFalsa().Responde("GET /api/auditoria", HttpStatusCode.OK, new List<EventoAuditoriaDto>()));

        var c = Panel();

        Assert.Contains("Todavía no hay cambios", c.Find(".empty").TextContent);
    }

    [Fact]
    public void Un_error_de_la_api_se_muestra_y_permite_reintentar()
    {
        var api = Registrar(new ApiFalsa().Error("GET /api/auditoria", HttpStatusCode.Forbidden, "Solo un administrador puede ver el historial."));

        var c = Panel();

        Assert.Contains("Solo un administrador", c.Find("[role=alert]").TextContent);
        c.Find("[role=alert] button").Click();
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/auditoria"));
    }

    [Fact]
    public void Cargar_mas_pide_la_pagina_siguiente_desde_el_ultimo_evento()
    {
        var primera = Enumerable.Range(0, 50)
            .Select(i => Evento("crear", "gasto", null, """{"concepto":"x"}""", cuando: new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero).AddMinutes(-i)))
            .ToList();
        var api = Registrar(new ApiFalsa().Responde("GET /api/auditoria", HttpStatusCode.OK, primera));
        var c = Panel();

        Assert.Equal(50, c.FindAll("li.hist-item").Count);
        c.FindAll("button").First(b => b.TextContent.Contains("Cargar más")).Click();

        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/auditoria"));
        Assert.Contains(primera[^1].Id.ToString(), api.Consultas.Last());
        Assert.Contains("despuesDeId=", api.Consultas.Last());
    }

    [Fact]
    public void Filtrar_por_tipo_vuelve_a_pedir_el_historial_con_la_entidad()
    {
        var api = Registrar(new ApiFalsa().Responde("GET /api/auditoria", HttpStatusCode.OK, new List<EventoAuditoriaDto>()));
        var c = Panel();

        c.Find("select").Change("categoria");

        Assert.Contains("entidad=categoria", api.Consultas.Last());
    }

    [Fact]
    public void Si_falla_cargar_mas_se_conserva_la_lista_y_el_reintento_pide_solo_esa_pagina()
    {
        var primera = Enumerable.Range(0, 50)
            .Select(i => Evento("crear", "gasto", null, """{"concepto":"x"}""", cuando: new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero).AddMinutes(-i)))
            .ToList();
        var llamadas = 0;
        var api = Registrar(new ApiFalsa().Responde("GET /api/auditoria", HttpStatusCode.OK, primera));
        api.Responde("GET /api/auditoria", r => ++llamadas == 1
            ? ApiFalsa.Json(HttpStatusCode.OK, primera)
            : ApiFalsa.Json(HttpStatusCode.InternalServerError, new { error = "Fallo de prueba." }));
        var c = Panel();

        c.FindAll("button").First(b => b.TextContent.Contains("Cargar más")).Click();

        Assert.Equal(50, c.FindAll("li.hist-item").Count);
        Assert.Contains("Fallo de prueba.", c.Find("[role=alert]").TextContent);
        var reintentar = c.FindAll("button").First(b => b.TextContent.Contains("Reintentar"));
        reintentar.Click();
        Assert.Contains("despuesDeId=", api.Consultas.Last());
        Assert.Equal(50, c.FindAll("li.hist-item").Count);
    }
}
