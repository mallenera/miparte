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

    private static CuentaComunResponse Cuenta(decimal saldo, params PendienteCuentaDto[] pendientes) =>
        new("2026-10", 1000m, 1000m, 1000m - saldo, saldo, pendientes, saldo + pendientes.Sum(p => p.Importe), [], []);

    [Fact]
    public void Cuenta_comun_muestra_saldo_y_avisa_si_no_cubre_los_gastos()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(-50m));
        Registrar(api);

        var c = RenderComponent<VistaCuentaComun>();

        Assert.Contains("La cuenta no cubre los gastos", c.Markup);
        Assert.Contains("50,00", c.Find("#saldo").TextContent);
    }

    [Fact]
    public void Cuenta_comun_fijar_aportacion_envia_adulto_mes_e_importe()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m))
            .Responde("PUT /api/cuenta-comun/aportaciones", HttpStatusCode.OK,
                new AportacionCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), 600m));
        Registrar(api);
        var c = RenderComponent<VistaCuentaComun>();

        c.Find("#form-aportacion input").Input("600,50");
        c.Find("#form-aportacion").Submit();

        var cuerpo = api.Cuerpos["PUT /api/cuenta-comun/aportaciones"];
        Assert.Contains(AnaId.ToString(), cuerpo);
        Assert.Contains("\"importe\":600.5", cuerpo);
        Assert.Contains($"\"desde\":\"{DateTime.Today:yyyy-MM}-01\"", cuerpo);
    }

    [Fact]
    public void Cuenta_comun_reembolso_solo_aparece_con_pendientes_y_envia_importe()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(100m, new PendienteCuentaDto(AnaId, "Ana", 900m)))
            .Responde("POST /api/cuenta-comun/reembolsos", HttpStatusCode.Created,
                new ReembolsoCuentaDto(Guid.NewGuid(), AnaId, DateOnly.FromDateTime(DateTime.Today), 400m, null));
        Registrar(api);
        var c = RenderComponent<VistaCuentaComun>();

        Assert.Contains("La cuenta le debe", c.Markup);
        c.Find("#form-reembolso input").Input("400");
        c.Find("#form-reembolso").Submit();

        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/reembolsos"];
        Assert.Contains(AnaId.ToString(), cuerpo);
        Assert.Contains("\"importe\":400", cuerpo);
    }

    [Fact]
    public void Cuenta_comun_sin_pendientes_no_ofrece_reembolsar()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m));
        Registrar(api);

        var c = RenderComponent<VistaCuentaComun>();

        Assert.Empty(c.FindAll("#form-reembolso"));
        Assert.Contains("No hay reembolsos pendientes", c.Markup);
    }

    [Fact]
    public void Gasto_con_cuenta_configurada_permite_que_pague_la_cuenta_y_envia_pagador_nulo()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Casa", null, null);
        var aportacion = new AportacionCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(2026, 1, 1), 500m);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK,
                new CuentaComunResponse("2026-10", 500m, 500m, 0m, 500m, [], 500m, [aportacion], []))
            .Responde("POST /api/gastos", HttpStatusCode.Created,
                new GastoResponse(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), 80m, cat.Id, null, perfil.Id, null, null, [], true));
        Registrar(api);
        var c = RenderComponent<VistaGastos>();

        c.FindAll("form.addcat select")[1].Change(""); // Paga: cuenta común
        c.Find("form.addcat input[inputmode=decimal]").Input("80");
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/gastos"];
        Assert.Contains("\"pagadoPor\":null", cuerpo);
        Assert.Contains(perfil.Id.ToString(), cuerpo);
    }

    private static ApiFalsa ApiResumen(ResumenMensualResponse resumen, LiquidacionResponse liquidacion) =>
        new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK,
                new[] { ApiFalsa.Miembro("Ana", esYo: true, id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId) })
            .Responde("GET /api/resumen", HttpStatusCode.OK, resumen)
            .Responde("GET /api/liquidacion", HttpStatusCode.OK, liquidacion);

    private static LiquidacionResponse Liquidacion(decimal deuda, params PagoLiquidacionDto[] pagos) =>
        new("2026-10",
            [new SaldoMiembroDto(AnaId, "Ana", deuda), new SaldoMiembroDto(LuisId, "Luis", -deuda)],
            deuda > 0 ? [new TransferenciaDto(LuisId, AnaId, deuda)] : [],
            pagos);

    [Fact]
    public void Resumen_muestra_totales_por_persona_y_categoria_con_la_transferencia_sugerida()
    {
        var resumen = new ResumenMensualResponse("2026-10", 1200m,
            [new ResumenMiembroDto(AnaId, "Ana", 1000m, 700m), new ResumenMiembroDto(LuisId, "Luis", 200m, 500m)],
            [new ResumenCategoriaDto(Guid.NewGuid(), "Hipoteca", 850m, [new ImporteMiembroDto(AnaId, 500m), new ImporteMiembroDto(LuisId, 350m)])]);
        Registrar(ApiResumen(resumen, Liquidacion(300m)));

        var c = RenderComponent<VistaResumen>();

        Assert.Contains("1200,00", c.Find("#total").TextContent.Replace(".", ""));
        Assert.Contains("Hipoteca", c.Markup);
        Assert.Contains("Luis paga a Ana", c.Markup);
        Assert.Contains("+", c.Find(".saldo.pos").TextContent); // el signo acompaña al color
        Assert.NotEmpty(c.FindAll(".saldo.neg"));
    }

    [Fact]
    public void Resumen_sin_gastos_invita_a_anadir_uno_y_dice_que_no_hay_nada_que_liquidar()
    {
        Registrar(ApiResumen(new ResumenMensualResponse("2026-10", 0m, [], []), new LiquidacionResponse("2026-10", [], [], [])));

        var c = RenderComponent<VistaResumen>();

        Assert.Contains("No hay gastos", c.Markup);
        Assert.Contains("nada que liquidar", c.Markup);
        Assert.DoesNotContain(c.FindAll("button"), b => b.TextContent == "Registrar pago");
    }

    [Fact]
    public void Resumen_registrar_pago_envia_mes_pagador_receptor_e_importe_de_la_transferencia()
    {
        var api = ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), Liquidacion(300m))
            .Responde("POST /api/pagos-liquidacion", HttpStatusCode.Created,
                new PagoLiquidacionDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), LuisId, AnaId, 300m, new DateOnly(2026, 10, 20), null));
        Registrar(api);
        var c = RenderComponent<VistaResumen>();

        c.FindAll("button").First(b => b.TextContent == "Registrar pago").Click();

        var cuerpo = api.Cuerpos["POST /api/pagos-liquidacion"];
        Assert.Contains($"\"deMiembroId\":\"{LuisId}\"", cuerpo);
        Assert.Contains($"\"aMiembroId\":\"{AnaId}\"", cuerpo);
        Assert.Contains("\"importe\":300", cuerpo);
        Assert.Contains($"\"mes\":\"{DateTime.Today:yyyy-MM}-01\"", cuerpo);
    }

    [Fact]
    public void Resumen_muestra_el_error_de_la_api_si_el_pago_supera_la_deuda()
    {
        var api = ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), Liquidacion(300m))
            .Error("POST /api/pagos-liquidacion", HttpStatusCode.Conflict, "El importe supera la deuda pendiente entre ambos miembros (150.00).");
        Registrar(api);
        var c = RenderComponent<VistaResumen>();

        c.FindAll("button").First(b => b.TextContent == "Registrar pago").Click();

        Assert.Contains("supera la deuda pendiente", c.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Resumen_eliminar_un_pago_pide_confirmacion_y_llama_a_la_api()
    {
        var pago = new PagoLiquidacionDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), LuisId, AnaId, 150m, new DateOnly(2026, 10, 20), "Bizum");
        var api = ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), Liquidacion(150m, pago))
            .Responde($"DELETE /api/pagos-liquidacion/{pago.Id}", HttpStatusCode.NoContent);
        Registrar(api);
        var c = RenderComponent<VistaResumen>();

        Assert.Contains("Bizum", c.Markup);
        c.FindAll("button").First(b => b.TextContent == "Eliminar").Click();
        Assert.DoesNotContain($"DELETE /api/pagos-liquidacion/{pago.Id}", api.Recibidas);
        c.FindAll("button").First(b => b.TextContent == "Sí").Click();

        Assert.Contains($"DELETE /api/pagos-liquidacion/{pago.Id}", api.Recibidas);
    }

    [Fact]
    public void Gasto_sin_cuenta_configurada_no_ofrece_a_la_cuenta_como_pagador()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Casa", null, null);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK,
                new CuentaComunResponse("2026-10", 0m, 0m, 0m, 0m, [], 0m, [], []));
        Registrar(api);

        var c = RenderComponent<VistaGastos>();

        Assert.DoesNotContain("la paga directamente", c.Markup);
    }
}
