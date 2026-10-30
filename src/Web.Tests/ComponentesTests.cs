using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;
using MiParte.Web.Componentes;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

public class ComponentesTests : BunitContext
{
    private static readonly HogarResumen Casa = new(Guid.NewGuid(), "Casa");
    private static readonly Guid AnaId = Guid.NewGuid(), LuisId = Guid.NewGuid();

    private ServicioPermisos _permisos = null!;

    public ComponentesTests()
    {
        // La tarjeta de compartir pregunta al navegador por Web Share: en estas pruebas basta con que no haya JS configurado.
        JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
    }

    private void Registrar(ApiFalsa api, HogarResumen? hogarActual = null)
    {
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        var hogar = new EstadoHogar(new AlmacenMemoria());
        // La pestaña Gastos sabe si la cuenta común está activa por el hogar ya cargado, no por /api/cuenta-comun.
        if (hogarActual is not null) hogar.AplicarAsync(new YoResponse(null, [hogarActual], hogarActual)).GetAwaiter().GetResult();
        Services.AddSingleton(cliente);
        Services.AddSingleton(hogar);
        _permisos = PermisosDePrueba.Registrar(Services, cliente, hogar);
        Services.AddScoped<ServicioArranque>();
        Services.AddSingleton<ServicioAvisos>();
        var reloj = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(reloj);
        // La tarjeta de avisos del navegador consulta la sesión para ocultarse en la cuenta demo.
        var auth = new SupabaseAuthClient(
            new HttpClient(ManejadorFalso.Json(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://x.supabase.co/auth/v1/") }, reloj);
        Services.AddSingleton(new ServicioSesion(auth, new AlmacenMemoria(), reloj));
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
            .Responde("GET /api/perfiles", HttpStatusCode.OK, perfiles)
            .Responde("GET /api/auditoria", HttpStatusCode.OK, new List<EventoAuditoriaDto>());
    }

    [Fact]
    public void Hogar_como_admin_muestra_miembros_y_acciones_de_gestion()
    {
        Registrar(ApiConHogar(soyAdmin: true));

        var c = Render<VistaConfiguracionHogar>();

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

        var c = Render<VistaConfiguracionHogar>();

        Assert.Empty(c.FindAll("form.addm"));
        Assert.Contains("No tienes permiso para añadir o desactivar miembros", c.Markup);
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
        var c = Render<VistaConfiguracionHogar>();

        c.Find("#card-invite button.btn-orange").Click();

        Assert.Equal("token-secreto-123", c.Find("#token-invitacion").TextContent);
        Assert.Contains("no se vuelve a mostrar", c.Markup);
        c.Find(".invite button.linkbtn").Click(); // Cerrar aviso
        Assert.DoesNotContain("token-secreto-123", c.Markup);
    }

    [Fact]
    public void Invitar_muestra_un_enlace_con_el_codigo_en_el_fragmento()
    {
        var api = ApiConHogar(soyAdmin: true)
            .Responde("POST /api/invitaciones", HttpStatusCode.Created,
                new InvitacionCreada(Guid.NewGuid(), "token-secreto-123", DateTimeOffset.UtcNow.AddDays(7)));
        Registrar(api);
        var c = Render<VistaConfiguracionHogar>();

        c.Find("#card-invite button.btn-orange").Click();

        var enlace = c.Find("#enlace-invitacion").TextContent;
        Assert.EndsWith("/unirse#token=token-secreto-123", enlace);
        Assert.DoesNotContain("?", enlace);
    }

    [Fact]
    public void Un_409_al_desactivar_se_muestra_con_el_mensaje_de_la_api()
    {
        var api = ApiConHogar(soyAdmin: true)
            .Error($"PUT /api/miembros/{LuisId}", HttpStatusCode.Conflict, "El hogar necesita al menos un administrador.");
        Registrar(api);
        var c = Render<VistaConfiguracionHogar>();

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
        var c = Render<VistaConfiguracionHogar>();

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
        var c = Render<EditorPerfil>(p => p.Add(x => x.Perfil, perfil).Add(x => x.Miembros, miembros));

        Assert.False(c.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Contains("✓", c.Find(".sumline").TextContent);

        c.FindAll("input[type=number]")[1].Input("30");

        Assert.True(c.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Contains("debe ser 100", c.Find(".sumline").TextContent);
    }

    [Fact]
    public void Configuracion_abre_en_miembros_y_no_muestra_las_funciones_hasta_elegir_hogar()
    {
        Registrar(ApiConHogar(soyAdmin: true));

        var c = Render<VistaConfiguracionHogar>();

        Assert.Equal("panel-miembros", c.Find("[role=tab][aria-selected=true]").GetAttribute("aria-controls"));
        Assert.Contains("Miembros de", c.Markup);
        Assert.DoesNotContain("Funciones del hogar", c.Markup);
    }

    [Fact]
    public void La_pestana_de_la_url_muestra_su_seccion()
    {
        Registrar(ApiConHogar(soyAdmin: true));

        var c = Render<VistaConfiguracionHogar>(p => p.Add(x => x.Pestana, "hogar"));

        Assert.Equal("Hogar", c.Find("[role=tab][aria-selected=true]").TextContent);
        Assert.Contains("Funciones del hogar", c.Markup);
        Assert.DoesNotContain("Miembros de", c.Markup);
    }

    [Fact]
    public void Una_pestana_desconocida_en_la_url_cae_en_miembros()
    {
        Registrar(ApiConHogar(soyAdmin: true));

        var c = Render<VistaConfiguracionHogar>(p => p.Add(x => x.Pestana, "inventada"));

        Assert.Equal("Miembros", c.Find("[role=tab][aria-selected=true]").TextContent);
        Assert.Contains("Miembros de", c.Markup);
    }

    [Fact]
    public void Elegir_una_pestana_la_deja_en_la_url_y_muestra_sus_permisos()
    {
        Registrar(ApiConHogar(soyAdmin: true));
        var c = Render<VistaConfiguracionHogar>();

        c.FindAll("[role=tab]").First(b => b.TextContent == "Permisos").Click();

        Assert.EndsWith("?pestana=permisos", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Contains("Cada adulto con cuenta tiene los permisos", c.Markup);
        Assert.DoesNotContain("Miembros de", c.Markup);
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

        var c = Render<VistaCategorias>();

        var nombres = c.FindAll(".c-name").Select(n => n.TextContent).ToList();
        Assert.Equal(["Comida", "Supermercado"], nombres);
        Assert.Single(c.FindAll(".c-name.sub"));

        c.FindAll("button").First(b => b.GetAttribute("aria-label") == "Eliminar Comida").Click();
        c.FindAll("button").First(b => b.TextContent == "Sí").Click();

        Assert.Contains("subcategorías", c.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Subcategoria_del_boton_envia_el_padre_y_hereda_su_perfil()
    {
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "50/50", "porcentaje", []);
        var comida = new CategoriaDto(Guid.NewGuid(), "Comida", null, perfil.Id);
        var api = new ApiFalsa()
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { comida })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("POST /api/categorias", HttpStatusCode.Created, new CategoriaDto(Guid.NewGuid(), "Bares", comida.Id, perfil.Id));
        Registrar(api);
        var c = Render<VistaCategorias>();

        c.FindAll("button").First(b => b.GetAttribute("aria-label") == "Añadir subcategoría a Comida").Click();
        Assert.Contains("Nueva subcategoría", c.Find("form.addcat label").TextContent);
        c.Find("form.addcat input").Input("Bares");
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/categorias"];
        Assert.Contains("\"nombre\":\"Bares\"", cuerpo);
        Assert.Contains(comida.Id.ToString(), cuerpo);
        Assert.Contains(perfil.Id.ToString(), cuerpo);
    }

    [Fact]
    public void Resumen_cuelga_la_subcategoria_de_su_madre_sin_gasto_propio()
    {
        var comida = new CategoriaDto(Guid.NewGuid(), "Comida", null, null);
        var super_ = new CategoriaDto(Guid.NewGuid(), "Supermercado", comida.Id, null);
        var resumen = new ResumenMensualResponse("2026-10", 80m,
            [new ResumenMiembroDto(AnaId, "Ana", 80m, 40m), new ResumenMiembroDto(LuisId, "Luis", 0m, 40m)],
            [new ResumenCategoriaDto(super_.Id, "Supermercado", 80m, [])]);
        Registrar(ApiResumen(resumen, Liquidacion(40m)).Responde("GET /api/categorias", HttpStatusCode.OK, new[] { comida, super_ }));

        var c = Render<VistaResumen>();

        // La hija solo con gasto propio cuelga de su madre, que sale sin gasto directo y desplegable.
        var madre = Assert.Single(c.FindAll(".resrow"));
        Assert.Contains("Comida", madre.TextContent);
        c.Find("button[aria-label='Desplegar Comida']").Click();
        Assert.Equal(2, c.FindAll(".resrow").Count);
        Assert.Contains("Supermercado", c.FindAll(".resrow")[1].TextContent);
    }

    [Fact]
    public void Resumen_acumula_las_hijas_en_la_madre_sin_duplicar_y_se_despliega()
    {
        var comida = new CategoriaDto(Guid.NewGuid(), "Comida", null, null);
        var super_ = new CategoriaDto(Guid.NewGuid(), "Supermercado", comida.Id, null);
        var bares = new CategoriaDto(Guid.NewGuid(), "Bares", comida.Id, null);
        var resumen = new ResumenMensualResponse("2026-10", 200m,
            [new ResumenMiembroDto(AnaId, "Ana", 200m, 100m), new ResumenMiembroDto(LuisId, "Luis", 0m, 100m)],
            [
                new ResumenCategoriaDto(comida.Id, "Comida", 20m, [new ImporteMiembroDto(AnaId, 20m)]),
                new ResumenCategoriaDto(super_.Id, "Supermercado", 100m, [new ImporteMiembroDto(AnaId, 100m)]),
                new ResumenCategoriaDto(bares.Id, "Bares", 80m, [new ImporteMiembroDto(LuisId, 80m)]),
            ]);
        Registrar(ApiResumen(resumen, Liquidacion(0m)).Responde("GET /api/categorias", HttpStatusCode.OK, new[] { comida, super_, bares }));

        var c = Render<VistaResumen>();

        // Contraída: solo la madre, con el acumulado (20 + 100 + 80) y el 100 % del mes.
        var filas = c.FindAll(".resrow");
        Assert.Single(filas);
        Assert.Contains("Comida", filas[0].TextContent);
        Assert.Contains("200", filas[0].TextContent);
        Assert.Contains("100 %", filas[0].TextContent);

        c.Find("button[aria-label='Desplegar Comida']").Click();

        filas = c.FindAll(".resrow");
        Assert.Equal(4, filas.Count);
        Assert.Contains("Sin subcategoría", filas[1].TextContent);
        Assert.Contains("20", filas[1].TextContent);
        Assert.Contains("Supermercado", filas[2].TextContent);
        Assert.Contains("50 %", filas[2].TextContent);
        Assert.Contains("Bares", filas[3].TextContent);
        Assert.Contains("40 %", filas[3].TextContent);

        c.Find("button[aria-label='Contraer Comida']").Click();
        Assert.Single(c.FindAll(".resrow"));
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
        var c = Render<VistaCategorias>();

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
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m))
            .Responde("POST /api/gastos", HttpStatusCode.Created,
                new GastoResponse(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), 12.5m, cat.Id, ana.Id, perfil.Id, null, null, []));
        Registrar(api, new HogarResumen(Casa.Id, "Casa", true, false));
        var c = Render<VistaGastos>();

        c.Find("form.addcat input[inputmode=decimal]").Input("12,50");
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/gastos"];
        Assert.Contains("\"importe\":12.5", cuerpo);
        Assert.Contains(cat.Id.ToString(), cuerpo);
        Assert.Contains(ana.Id.ToString(), cuerpo);
        Assert.Contains(perfil.Id.ToString(), cuerpo);
    }

    /// <summary>Filas de la lista de la cuenta sin las aportaciones (que salen siempre, una por adulto).</summary>
    private static List<AngleSharp.Dom.IElement> FilasSinAportacion(IRenderedComponent<VistaCuentaComun> c) =>
        c.FindAll("section[aria-labelledby=t-operaciones] .gastorow").Where(f => !f.TextContent.Contains("Aportación mensual")).ToList();

    private static CuentaComunResponse Cuenta(decimal saldo, params PendienteCuentaDto[] pendientes) =>
        new("2026-10", 1000m, 1000m, 1000m - saldo, saldo, pendientes, saldo + pendientes.Sum(p => p.Importe), [], []);

    private static CuentaComunResponse CuentaConDeudas(params DeudaCuentaDto[] deudas) =>
        new("2026-10", 1000m, 1000m, 0m, 1000m, [], 1000m, [], [], Deudas: deudas);

    [Fact]
    public void Cuenta_comun_sin_deudas_lo_dice_y_ofrece_registrar_un_movimiento()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas());
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        Assert.Empty(FilasSinAportacion(c));
        Assert.NotEmpty(c.FindAll("#form-operacion"));
    }

    [Fact]
    public void Cuenta_comun_lista_lo_que_debe_cada_persona_y_a_que_fondo()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var luis = ApiFalsa.Miembro("Luis", id: LuisId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana, luis })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas(
                new DeudaCuentaDto(AnaId, "Ana", 40m), new DeudaCuentaDto(LuisId, "Luis", 15m, DesdeAhorro: true)));
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        var filas = FilasSinAportacion(c);
        Assert.Equal(2, filas.Count);
        Assert.Contains("Ana", filas[0].TextContent);
        Assert.Contains("Debe a Hogar", filas[0].TextContent);
        Assert.Contains("40,00", filas[0].TextContent);
        Assert.Contains("Luis", filas[1].TextContent);
        Assert.Contains("Debe al ahorro", filas[1].TextContent);
        Assert.DoesNotContain("Nadie debe nada", c.Markup);
    }

    [Fact]
    public void Cuenta_comun_registra_un_adelanto_con_la_persona_elegida_por_defecto_yo()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var luis = ApiFalsa.Miembro("Luis", id: LuisId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { luis, ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas())
            .Responde("POST /api/cuenta-comun/deudas", HttpStatusCode.Created,
                new MovimientoDeudaCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(2026, 10, 7), 40m, "adelanto", false, null));
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("#form-operacion select").Change("adelanto");
        c.Find("#form-operacion input[placeholder='0,00']").Input("40");
        c.Find("#form-operacion").Submit();

        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/deudas"];
        Assert.Contains(AnaId.ToString(), cuerpo); // la persona que usa la app, aunque no sea la primera de la lista
        Assert.Contains("\"tipo\":\"adelanto\"", cuerpo);
        Assert.Contains("\"importe\":40", cuerpo);
        Assert.Contains("\"desdeAhorro\":false", cuerpo);
    }

    [Fact]
    public void Cuenta_comun_registra_una_compensacion_del_ahorro_de_otra_persona()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var luis = ApiFalsa.Miembro("Luis", id: LuisId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana, luis })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas(new DeudaCuentaDto(LuisId, "Luis", 15m, DesdeAhorro: true)))
            .Responde("POST /api/cuenta-comun/deudas", HttpStatusCode.Created,
                new MovimientoDeudaCuentaDto(Guid.NewGuid(), LuisId, new DateOnly(2026, 10, 7), 10m, "compensacion", true, null));
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("#form-operacion select").Change("adelanto");
        c.FindAll("#form-operacion select")[1].Change(LuisId.ToString());
        c.Find("#form-operacion select").Change("compensacion");
        c.Find("#form-operacion input[type=checkbox]").Change(true);
        c.Find("#form-operacion input[placeholder='0,00']").Input("10");
        c.Find("#form-operacion").Submit();

        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/deudas"];
        Assert.Contains(LuisId.ToString(), cuerpo);
        Assert.Contains("\"tipo\":\"compensacion\"", cuerpo);
        Assert.Contains("\"desdeAhorro\":true", cuerpo);
    }

    [Fact]
    public void Cuenta_comun_refresca_los_movimientos_tras_registrar_una_deuda()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, Array.Empty<CategoriaDto>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas())
            .Responde("GET /api/cuenta-comun/movimientos", HttpStatusCode.OK,
                new MovimientosCuentaComunResponse(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), 0m, 0m, 0m, 0m, []))
            .Responde("POST /api/cuenta-comun/deudas", HttpStatusCode.Created,
                new MovimientoDeudaCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(2026, 10, 7), 40m, "adelanto", false, null));
        Registrar(api);
        var c = Render<VistaCuentaComun>();
        c.Find("#form-operacion select").Change("adelanto");
        Assert.Equal(1, api.Recibidas.Count(r => r == "GET /api/cuenta-comun/movimientos"));

        c.Find("#form-operacion input[placeholder='0,00']").Input("40");
        c.Find("#form-operacion").Submit();

        // El extracto de Movimientos se vuelve a pedir para que la línea nueva aparezca sin recargar la página.
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/cuenta-comun/movimientos"));
    }

    private static CuentaComunResponse CuentaConMovimientosDeuda(params MovimientoDeudaCuentaDto[] movimientos) =>
        new("2026-10", 1000m, 1000m, 0m, 1000m, [], 1000m, [], [], Deudas: [], MovimientosDeuda: movimientos);

    private static readonly Guid DeudaId = Guid.NewGuid();

    [Fact]
    public void Cuenta_comun_lista_los_movimientos_de_deuda_del_mes_con_su_persona_fondo_y_concepto()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConMovimientosDeuda(
                new MovimientoDeudaCuentaDto(DeudaId, AnaId, new DateOnly(2026, 10, 5), 40m, "adelanto", false, "Fontanero"),
                new MovimientoDeudaCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(2026, 10, 7), 15m, "devolucion", true, null)));
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        var filas = FilasSinAportacion(c);
        Assert.Equal(2, filas.Count);
        Assert.Contains("Adelanto · Ana", filas[0].TextContent);
        Assert.Contains("De Hogar · Fontanero", filas[0].TextContent);
        Assert.Contains("05/10/2026", filas[0].TextContent);
        Assert.Contains("40,00", filas[0].TextContent);
        Assert.Contains("Devolución · Ana", filas[1].TextContent);
        Assert.Contains("Del ahorro", filas[1].TextContent);
        Assert.Contains("Lista de la cuenta de", c.Find("section[aria-labelledby=t-operaciones]").TextContent);
    }

    [Fact]
    public void Cuenta_comun_sin_movimientos_de_deuda_no_muestra_la_lista()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConMovimientosDeuda());
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        Assert.Empty(FilasSinAportacion(c));
    }

    [Fact]
    public void Cuenta_comun_borra_un_movimiento_de_deuda_tras_confirmar()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConMovimientosDeuda(
                new MovimientoDeudaCuentaDto(DeudaId, AnaId, new DateOnly(2026, 10, 5), 40m, "adelanto", false, null)))
            .Responde($"DELETE /api/cuenta-comun/deudas/{DeudaId}", HttpStatusCode.NoContent);
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("section[aria-labelledby=t-operaciones] button.linkbtn.danger").Click();
        Assert.DoesNotContain($"DELETE /api/cuenta-comun/deudas/{DeudaId}", api.Recibidas); // pide confirmación antes
        Assert.Contains("¿Eliminar?", c.Find("section[aria-labelledby=t-operaciones]").TextContent);

        c.FindAll("section[aria-labelledby=t-operaciones] button.linkbtn.danger").First(b => b.TextContent == "Sí").Click();

        Assert.Contains($"DELETE /api/cuenta-comun/deudas/{DeudaId}", api.Recibidas);
    }

    [Fact]
    public void Cuenta_comun_permite_cancelar_el_borrado_de_una_deuda()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConMovimientosDeuda(
                new MovimientoDeudaCuentaDto(DeudaId, AnaId, new DateOnly(2026, 10, 5), 40m, "adelanto", false, null)));
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("section[aria-labelledby=t-operaciones] button.linkbtn.danger").Click();
        c.FindAll("section[aria-labelledby=t-operaciones] button.linkbtn").First(b => b.TextContent == "No").Click();

        Assert.DoesNotContain("¿Eliminar?", c.Find("section[aria-labelledby=t-operaciones]").TextContent);
        Assert.DoesNotContain(api.Recibidas, r => r.StartsWith("DELETE"));
    }

    [Fact]
    public void Cuenta_comun_sin_el_permiso_de_movimientos_ve_los_movimientos_de_deuda_pero_no_puede_borrarlos()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConMovimientosDeuda(
                new MovimientoDeudaCuentaDto(DeudaId, AnaId, new DateOnly(2026, 10, 5), 40m, "adelanto", false, null)));
        Registrar(api);
        _permisos.Fijar([]);

        var c = Render<VistaCuentaComun>();

        Assert.Single(FilasSinAportacion(c));
        Assert.Empty(c.FindAll("section[aria-labelledby=t-operaciones] button.linkbtn.danger"));
    }

    [Fact]
    public void Cuenta_comun_muestra_por_que_no_se_puede_borrar_un_adelanto()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConMovimientosDeuda(
                new MovimientoDeudaCuentaDto(DeudaId, AnaId, new DateOnly(2026, 10, 5), 40m, "adelanto", false, null)))
            .Error($"DELETE /api/cuenta-comun/deudas/{DeudaId}", HttpStatusCode.Conflict,
                "No se puede borrar este adelanto: dejaría devoluciones o compensaciones sin deuda (20.00).");
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("section[aria-labelledby=t-operaciones] button.linkbtn.danger").Click();
        c.FindAll("section[aria-labelledby=t-operaciones] button.linkbtn.danger").First(b => b.TextContent == "Sí").Click();

        Assert.Contains("dejaría devoluciones o compensaciones sin deuda", c.Markup);
        // Además del aviso de la primera tarjeta, el motivo sale como aviso visible donde se actuó.
        Assert.Contains("dejaría devoluciones", Services.GetRequiredService<ServicioAvisos>().Texto);
    }

    [Fact]
    public void Cuenta_comun_no_permite_enviar_un_importe_invalido_de_deuda()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas());
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("#form-operacion select").Change("adelanto");
        c.Find("#form-operacion input[placeholder='0,00']").Input("abc");

        Assert.True(c.Find("#form-operacion button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void Cuenta_comun_sin_el_permiso_de_movimientos_no_ofrece_registrar_deudas()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas(new DeudaCuentaDto(AnaId, "Ana", 40m)));
        Registrar(api);
        _permisos.Fijar([]);

        var c = Render<VistaCuentaComun>();

        Assert.Contains("Ana", c.Find("section[aria-labelledby=t-operaciones]").TextContent);
        Assert.Empty(c.FindAll("#form-operacion"));
    }

    [Fact]
    public void Un_error_al_registrar_una_deuda_se_muestra_y_no_borra_lo_escrito()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, CuentaConDeudas())
            .Error("POST /api/cuenta-comun/deudas", HttpStatusCode.Conflict, "El importe supera lo que debe la persona (0,00).");
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("#form-operacion select").Change("adelanto");
        c.Find("#form-operacion select").Change("devolucion");
        c.Find("#form-operacion input[placeholder='0,00']").Input("100");
        c.Find("#form-operacion").Submit();

        Assert.Contains("supera lo que debe", c.Markup);
        Assert.Equal("100", c.Find("#form-operacion input[placeholder='0,00']").GetAttribute("value"));
    }

    [Fact]
    public void Cuenta_comun_avisa_si_el_ahorro_no_cubre_lo_que_debe_y_dice_cuanto_falta()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        // El ahorro debe 112,95 a las personas y tiene 80,94 disponibles: faltan 32,01.
        var estado = new CuentaComunResponse(
            "2026-10", 1000m, 1000m, 0m, 0m, [new PendienteCuentaDto(AnaId, "Ana", 112.95m, true)], 1000m, [], [], AhorroDisponible: 80.94m);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, estado);
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        Assert.Contains("El ahorro no cubre lo que debe a las personas: faltan 32,01", c.Find(".aviso.warn").TextContent);
        Assert.Contains("falta reembolsar 112,95", c.Find("#reembolsos-por-persona").TextContent);
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
        var c = Render<VistaCuentaComun>();

        c.Find("#form-operacion input[inputmode=decimal]").Input("600,50");
        c.Find("#form-operacion").Submit();

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
        var c = Render<VistaCuentaComun>();

        Assert.Contains("La cuenta le debe", c.Markup);
        c.Find("#form-operacion select").Change("reembolso");
        c.Find("#form-operacion input[inputmode=decimal]").Input("400");
        c.Find("#form-operacion").Submit();

        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/reembolsos"];
        Assert.Contains(AnaId.ToString(), cuerpo);
        Assert.Contains("\"importe\":400", cuerpo);
    }

    [Fact]
    public void Cuenta_comun_fijar_aportacion_envia_la_parte_de_ahorro_y_rechaza_si_supera_el_importe()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m))
            .Responde("PUT /api/cuenta-comun/aportaciones", HttpStatusCode.OK,
                new AportacionCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), 600m, 100m));
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.Find("#form-operacion input[inputmode=decimal]").Input("600");
        c.Find("#ahorro-aportacion").Input("700");
        Assert.True(c.Find("#form-operacion button").HasAttribute("disabled")); // ahorro > importe

        c.Find("#ahorro-aportacion").Input("100,50");
        c.Find("#form-operacion").Submit();

        var cuerpo = api.Cuerpos["PUT /api/cuenta-comun/aportaciones"];
        Assert.Contains("\"importe\":600", cuerpo);
        Assert.Contains("\"ahorro\":100.5", cuerpo);
    }

    [Fact]
    public void Cuenta_comun_ya_no_muestra_retiradas_y_remite_a_gastos_para_gastar_del_ahorro()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var conAhorro = Cuenta(0m) with
        {
            AhorroAcumulado = 300m, AhorroDisponible = 180m, AhorroGastado = 120m, AhorroActivo = true,
            RetiradasAhorro = [new RetiradaAhorroDto(Guid.NewGuid(), AnaId, DateOnly.FromDateTime(DateTime.Today), 50m, "Antigua")],
        };
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, conAhorro);
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        Assert.Empty(c.FindAll("#form-retirada"));
        Assert.DoesNotContain("Retirado", c.Markup);
        Assert.DoesNotContain("Retirada de", c.Markup);
        Assert.Contains("Anotar un gasto del ahorro", c.Markup);
    }

    [Fact]
    public void Cuenta_comun_sin_pendientes_no_ofrece_reembolsar()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m));
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        Assert.True(c.Find("#form-operacion option[value=reembolso]").HasAttribute("disabled"));
        Assert.DoesNotContain("Reembolso pendiente", c.Markup);
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
        var c = Render<VistaGastos>();

        c.FindAll("form.addcat select")[1].Change(""); // Paga: cuenta común
        c.Find("form.addcat input[inputmode=decimal]").Input("80");
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/gastos"];
        Assert.Contains("\"pagadoPor\":null", cuerpo);
        Assert.Contains(perfil.Id.ToString(), cuerpo);
    }

    [Fact]
    public void Gasto_ofrece_el_perfil_de_ahorro_y_lo_envia_sin_pagador_si_lo_paga_el_ahorro()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común (ahorro)", "cuenta_comun_ahorro", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Casa", null, null);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK,
                new CuentaComunResponse("2026-10", 0m, 0m, 0m, 0m, [], 0m, [], [], 0m, 300m, 0m, 300m, [], 300m, []))
            .Responde("POST /api/gastos", HttpStatusCode.Created,
                new GastoResponse(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), 80m, cat.Id, null, perfil.Id, null, null, [], true, true));
        Registrar(api, new HogarResumen(Casa.Id, "Casa", true, true));
        var c = Render<VistaGastos>();

        // Solo hay ahorro (sin aportaciones): aun así se ofrece su perfil, y con él, que lo pague el propio ahorro.
        Assert.Contains("Cuenta común (ahorro)", c.Markup);
        Assert.Contains("El ahorro (lo paga directamente)", c.Markup);
        Assert.Contains("Se descuenta del ahorro", c.Markup);
        c.FindAll("form.addcat select")[1].Change("");
        c.Find("form.addcat input[inputmode=decimal]").Input("80");
        c.Find("form.addcat").Submit();

        var cuerpo = api.Cuerpos["POST /api/gastos"];
        Assert.Contains("\"pagadoPor\":null", cuerpo);
        Assert.Contains("\"pagadoDesdeAhorro\":true", cuerpo);
        Assert.Contains(perfil.Id.ToString(), cuerpo);
    }

    [Fact]
    public void Gasto_personal_se_envia_marcado_y_se_distingue_en_la_lista()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Individual", "individual", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Ocio", null, perfil.Id);
        var personal = new GastoResponse(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), 30m, cat.Id, AnaId, perfil.Id, "Cine", null,
            [new RepartoGastoDto(AnaId, 30m)], false, false, true);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, new[] { personal })
            .Responde("POST /api/gastos", HttpStatusCode.Created, personal);
        Registrar(api);
        var c = Render<VistaGastos>();

        Assert.NotEmpty(c.FindAll(".gastorow.personal"));
        Assert.Contains("no entra en la liquidación", c.Markup);

        c.FindAll("form.addcat select")[2].Change("personal"); // Tipo de gasto: personal
        c.Find("form.addcat input[inputmode=decimal]").Input("30");
        c.Find("form.addcat").Submit();

        Assert.Contains("\"personal\":true", api.Cuerpos["POST /api/gastos"]);
    }

    [Fact]
    public void Cuenta_comun_ingreso_al_ahorro_envia_importe_y_concepto()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m))
            .Responde("POST /api/cuenta-comun/depositos-ahorro", HttpStatusCode.Created,
                new DepositoAhorroDto(Guid.NewGuid(), AnaId, DateOnly.FromDateTime(DateTime.Today), 1000m, "Ahorro inicial"));
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        c.FindAll("#form-deposito input")[0].Input("1000");
        c.FindAll("#form-deposito input")[1].Input("Ahorro inicial");
        c.Find("#form-deposito").Submit();

        var cuerpo = api.Cuerpos["POST /api/cuenta-comun/depositos-ahorro"];
        Assert.Contains(AnaId.ToString(), cuerpo);
        Assert.Contains("\"importe\":1000", cuerpo);
        Assert.Contains("Ahorro inicial", cuerpo);
    }

    private static ApiFalsa ApiResumen(ResumenMensualResponse resumen, LiquidacionResponse liquidacion) =>
        new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK,
                new[] { ApiFalsa.Miembro("Ana", esYo: true, id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId) })
            .Responde("GET /api/resumen-liquidacion", HttpStatusCode.OK, new ResumenYLiquidacionResponse(resumen, liquidacion));

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

        var c = Render<VistaResumen>();

        Assert.Contains("1200,00", c.Find("#total").TextContent.Replace(".", ""));
        Assert.Contains("Hipoteca", c.Markup);
        Assert.Contains("Luis paga a Ana", c.Markup);
        Assert.Contains("+", c.Find(".saldo.pos").TextContent); // el signo acompaña al color
        Assert.NotEmpty(c.FindAll(".saldo.neg"));
    }

    [Fact]
    public void Resumen_indica_lo_que_la_cuenta_comun_debe_a_quien_adelanto_gastos()
    {
        var resumen = new ResumenMensualResponse("2026-10", 100m,
            [new ResumenMiembroDto(AnaId, "Ana", 100m, 0m, 100m), new ResumenMiembroDto(LuisId, "Luis", 0m, 0m)], []);
        Registrar(ApiResumen(resumen, Liquidacion(0m)));

        var c = Render<VistaResumen>();

        var ana = c.FindAll("#resumen-personas tbody tr")[0].QuerySelectorAll("td");
        Assert.Contains("Cuenta común", c.Markup);
        Assert.Contains("la cuenta le debe", ana[2].TextContent);
        Assert.Contains("100,00", ana[2].TextContent);
    }

    [Fact]
    public void Resumen_cuenta_comun_en_negativo_indica_que_la_persona_debe_a_la_cuenta()
    {
        var resumen = new ResumenMensualResponse("2026-10", 0m,
            [new ResumenMiembroDto(AnaId, "Ana", 0m, 0m, -15m), new ResumenMiembroDto(LuisId, "Luis", 0m, 0m)], []);
        Registrar(ApiResumen(resumen, Liquidacion(0m)));

        var c = Render<VistaResumen>();

        var ana = c.FindAll("#resumen-personas tbody tr")[0].QuerySelectorAll("td");
        Assert.Contains("debe a la cuenta", ana[2].TextContent);
        Assert.Contains("15,00", ana[2].TextContent);
    }

    [Fact]
    public void Resumen_sin_gastos_sigue_mostrando_lo_que_la_cuenta_comun_debe_de_meses_anteriores()
    {
        var resumen = new ResumenMensualResponse("2026-10", 0m, [new ResumenMiembroDto(AnaId, "Ana", 0m, 0m, 40m)], []);
        Registrar(ApiResumen(resumen, Liquidacion(0m)));

        var c = Render<VistaResumen>();

        Assert.Contains("No hay gastos", c.Markup);
        Assert.Contains("La cuenta común le debe", c.Markup);
    }

    [Fact]
    public void Resumen_sin_gastos_invita_a_anadir_uno_y_dice_que_no_hay_nada_que_liquidar()
    {
        Registrar(ApiResumen(new ResumenMensualResponse("2026-10", 0m, [], []), new LiquidacionResponse("2026-10", [], [], [])));

        var c = Render<VistaResumen>();

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
        var c = Render<VistaResumen>();

        c.FindAll("button").First(b => b.TextContent == "Registrar pago").Click();

        var cuerpo = api.Cuerpos["POST /api/pagos-liquidacion"];
        Assert.Contains($"\"deMiembroId\":\"{LuisId}\"", cuerpo);
        Assert.Contains($"\"aMiembroId\":\"{AnaId}\"", cuerpo);
        Assert.Contains("\"importe\":300", cuerpo);
        Assert.Contains($"\"mes\":\"{DateTime.Today:yyyy-MM}-01\"", cuerpo);
    }

    [Fact]
    public void Resumen_pago_parcial_envia_el_importe_escrito_y_no_el_sugerido()
    {
        var api = ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), Liquidacion(300m))
            .Responde("POST /api/pagos-liquidacion", HttpStatusCode.Created,
                new PagoLiquidacionDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), LuisId, AnaId, 125.5m, new DateOnly(2026, 10, 20), null));
        Registrar(api);
        var c = Render<VistaResumen>();

        Assert.Equal("300,00", c.Find(".pagoimp input").GetAttribute("value")); // por defecto, el importe sugerido
        c.Find(".pagoimp input").Input("125,50");
        c.FindAll("button").First(b => b.TextContent == "Registrar pago").Click();

        Assert.Contains("\"importe\":125.5", api.Cuerpos["POST /api/pagos-liquidacion"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10,005")]
    [InlineData("1,230")]
    [InlineData("300,01")]
    [InlineData("abc")]
    public void Resumen_no_deja_registrar_un_pago_con_importe_invalido_o_mayor_que_lo_pendiente(string escrito)
    {
        var api = ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), Liquidacion(300m));
        Registrar(api);
        var c = Render<VistaResumen>();

        c.Find(".pagoimp input").Input(escrito);

        Assert.True(c.FindAll("button").First(b => b.TextContent == "Registrar pago").HasAttribute("disabled"));
        Assert.Contains("máximo 2 decimales", c.Markup);
        // El error queda asociado al campo para los lectores de pantalla.
        var campo = c.Find(".pagoimp input");
        Assert.Equal("true", campo.GetAttribute("aria-invalid"));
        Assert.NotNull(c.Find("#" + campo.GetAttribute("aria-describedby")));
    }

    [Fact]
    public void Resumen_filtrar_por_miembro_oculta_los_saldos_y_pagos_de_los_demas()
    {
        var otro = Guid.NewGuid();
        var pago = new PagoLiquidacionDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), otro, AnaId, 10m, new DateOnly(2026, 10, 20), "Efectivo");
        var liq = new LiquidacionResponse("2026-10",
            [new SaldoMiembroDto(AnaId, "Ana", 100m), new SaldoMiembroDto(LuisId, "Luis", -60m), new SaldoMiembroDto(otro, "Otra", -40m)],
            [new TransferenciaDto(LuisId, AnaId, 60m), new TransferenciaDto(otro, AnaId, 40m)], [pago]);
        Registrar(ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), liq));
        var c = Render<VistaResumen>();
        Assert.Equal(2, c.FindAll(".pagoimp").Count);

        c.Find("select[aria-label='Filtrar la liquidación por miembro']").Change(LuisId.ToString());

        Assert.Single(c.FindAll(".pagoimp"));
        Assert.Contains("Luis paga a Ana", c.Markup);
        Assert.DoesNotContain("Efectivo", c.Markup);
    }

    [Fact]
    public void Gastos_cambiar_un_filtro_vuelve_a_pedir_la_lista_y_permite_quitar_los_filtros()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var cat = new CategoriaDto(Guid.NewGuid(), "Comida", null, perfil.Id);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { cat })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m));
        Registrar(api);
        var c = Render<VistaGastos>();
        var antes = api.Recibidas.Count(r => r == "GET /api/gastos");
        Assert.Empty(c.FindAll(".filtros .linkbtn"));

        c.Find(".filtros select").Change(ana.Id.ToString());

        Assert.Equal(antes + 1, api.Recibidas.Count(r => r == "GET /api/gastos"));
        Assert.Contains("coincide con los filtros", c.Markup);
        c.Find(".filtros .linkbtn").Click();
        Assert.Equal(antes + 2, api.Recibidas.Count(r => r == "GET /api/gastos"));
        Assert.Empty(c.FindAll(".filtros .linkbtn"));
    }

    [Fact]
    public void Resumen_muestra_el_error_de_la_api_si_el_pago_supera_la_deuda()
    {
        var api = ApiResumen(new ResumenMensualResponse("2026-10", 600m, [], []), Liquidacion(300m))
            .Error("POST /api/pagos-liquidacion", HttpStatusCode.Conflict, "El importe supera la deuda pendiente entre ambos miembros (150.00).");
        Registrar(api);
        var c = Render<VistaResumen>();

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
        var c = Render<VistaResumen>();

        Assert.Contains("Bizum", c.Markup);
        c.FindAll("button").First(b => b.TextContent == "Eliminar").Click();
        Assert.DoesNotContain($"DELETE /api/pagos-liquidacion/{pago.Id}", api.Recibidas);
        c.FindAll("button").First(b => b.TextContent == "Sí").Click();

        Assert.Contains($"DELETE /api/pagos-liquidacion/{pago.Id}", api.Recibidas);
    }

    private static readonly CategoriaDto CatVivienda = new(Guid.NewGuid(), "Vivienda", null, null);
    private static readonly PerfilRepartoDto Perfil6040 = new(Guid.NewGuid(), "60/40", "porcentaje", [new(AnaId, 60m), new(LuisId, 40m)]);

    private static GastoRecurrenteResponse Alquiler(bool activo = true) =>
        new(Guid.NewGuid(), 850m, CatVivienda.Id, AnaId, Perfil6040.Id, 5, "Alquiler", activo);

    private static ApiFalsa ApiRecurrentes(params GastoRecurrenteResponse[] plantillas) =>
        new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK,
                new[] { ApiFalsa.Miembro("Ana", esYo: true, id: AnaId), ApiFalsa.Miembro("Luis", id: LuisId) })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { CatVivienda })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { Perfil6040 })
            .Responde("GET /api/gastos-recurrentes", HttpStatusCode.OK, plantillas);

    [Fact]
    public void Recurrentes_lista_las_plantillas_y_marca_las_pausadas()
    {
        Registrar(ApiRecurrentes(Alquiler(), Alquiler(activo: false) with { Concepto = "Gimnasio", Importe = 30m, DiaMes = 10 }));

        var c = Render<VistaRecurrentes>();

        Assert.Contains("Alquiler", c.Markup);
        Assert.Contains("Día 5", c.Markup);
        Assert.Contains("Gimnasio", c.Markup);
        Assert.Single(c.FindAll(".tag.off"));
        Assert.Contains("pausada", c.Find(".tag.off").TextContent);
    }

    [Fact]
    public void Recurrentes_desactivadas_avisa_sin_cargar_datos_ni_ofrece_generar()
    {
        var api = ApiRecurrentes(Alquiler());
        Registrar(api, Casa with { RecurrentesActivas = false });

        var c = Render<VistaRecurrentes>();

        Assert.Contains("no están activadas en este hogar", c.Markup);
        Assert.Contains("href=\"/hogar/configuracion\"", c.Markup);
        Assert.Empty(c.FindAll("#generar"));
        Assert.DoesNotContain("GET /api/gastos-recurrentes", api.Recibidas);
        Assert.DoesNotContain("GET /api/miembros", api.Recibidas);
    }

    [Fact]
    public void Recurrentes_sin_plantillas_no_permite_generar_y_lo_explica()
    {
        Registrar(ApiRecurrentes());

        var c = Render<VistaRecurrentes>();

        Assert.Contains("Todavía no hay gastos recurrentes", c.Markup);
        Assert.True(c.Find("#generar").HasAttribute("disabled"));
    }

    [Fact]
    public void Recurrentes_generar_envia_el_mes_y_cuenta_creados_y_existentes()
    {
        var api = ApiRecurrentes(Alquiler())
            .Responde("POST /api/gastos-recurrentes/generar", HttpStatusCode.OK, new GenerarRecurrentesResponse("2026-10", 2, 1));
        Registrar(api);
        var c = Render<VistaRecurrentes>();

        c.Find("#generar").Click();

        Assert.Contains("POST /api/gastos-recurrentes/generar", api.Recibidas);
        Assert.Contains("han creado 2 gastos", c.Find(".aviso.ok").TextContent);
        Assert.Contains("1 ya existían", c.Find(".aviso.ok").TextContent);
    }

    [Fact]
    public void Recurrentes_crear_plantilla_envia_importe_dia_pagador_y_perfil()
    {
        var api = ApiRecurrentes()
            .Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Alquiler());
        Registrar(api);
        var c = Render<VistaRecurrentes>();

        // Cada Input re-renderiza: se vuelve a buscar el campo para no usar un manejador obsoleto.
        c.FindAll("#form-recurrente input")[0].Input("Alquiler");
        c.FindAll("#form-recurrente input")[1].Input("850,50");
        c.FindAll("#form-recurrente input")[2].Input("5");
        c.Find("#form-recurrente").Submit();

        var cuerpo = api.Cuerpos["POST /api/gastos-recurrentes"];
        Assert.Contains("\"importe\":850.5", cuerpo);
        Assert.Contains("\"diaMes\":5", cuerpo);
        Assert.Contains("\"concepto\":\"Alquiler\"", cuerpo);
        Assert.Contains($"\"pagadoPor\":\"{AnaId}\"", cuerpo);
        Assert.Contains($"\"perfilRepartoId\":\"{Perfil6040.Id}\"", cuerpo);
        Assert.Contains("\"activo\":true", cuerpo);
    }

    [Fact]
    public void Recurrentes_crear_con_dia_ya_pasado_avisa_y_ofrece_crear_el_gasto_del_mes()
    {
        var api = ApiRecurrentes()
            .Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Alquiler())
            .Responde("POST /api/gastos-recurrentes/generar", HttpStatusCode.OK, new GenerarRecurrentesResponse("2026-10", 1, 0));
        Registrar(api);
        var c = Render<VistaRecurrentes>();

        c.FindAll("#form-recurrente input")[0].Input("Alquiler");
        c.FindAll("#form-recurrente input")[1].Input("850");
        c.FindAll("#form-recurrente input")[2].Input("6"); // hoy es 7
        c.Find("#form-recurrente").Submit();

        Assert.Contains("El día 6", c.Find("#aviso-dia-pasado").TextContent);
        c.Find("#crear-gasto-pasado").Click();

        Assert.Contains("POST /api/gastos-recurrentes/generar", api.Recibidas);
        Assert.Empty(c.FindAll("#aviso-dia-pasado"));
    }

    [Fact]
    public void Recurrentes_crear_con_dia_de_hoy_ofrece_crear_el_gasto_del_mes()
    {
        Registrar(ApiRecurrentes().Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Alquiler()));
        var c = Render<VistaRecurrentes>();

        c.FindAll("#form-recurrente input")[1].Input("850");
        c.FindAll("#form-recurrente input")[2].Input("7"); // hoy es 7
        c.Find("#form-recurrente").Submit();

        Assert.Contains("es hoy", c.Find("#aviso-dia-pasado").TextContent);
    }

    [Fact]
    public void Recurrentes_crear_con_dia_futuro_no_avisa()
    {
        Registrar(ApiRecurrentes().Responde("POST /api/gastos-recurrentes", HttpStatusCode.Created, Alquiler()));
        var c = Render<VistaRecurrentes>();

        c.FindAll("#form-recurrente input")[1].Input("850");
        c.FindAll("#form-recurrente input")[2].Input("8"); // hoy es 7
        c.Find("#form-recurrente").Submit();

        Assert.Empty(c.FindAll("#aviso-dia-pasado"));
    }

    [Fact]
    public void Recurrentes_dia_fuera_de_1_a_28_deshabilita_el_guardado()
    {
        Registrar(ApiRecurrentes());
        var c = Render<VistaRecurrentes>();

        c.FindAll("#form-recurrente input")[1].Input("100");
        c.FindAll("#form-recurrente input")[2].Input("31");

        Assert.True(c.Find("#form-recurrente button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void Recurrentes_pausar_reenvia_la_plantilla_con_activo_falso()
    {
        var p = Alquiler();
        var api = ApiRecurrentes(p).Responde($"PUT /api/gastos-recurrentes/{p.Id}", HttpStatusCode.OK, p with { Activo = false });
        Registrar(api);
        var c = Render<VistaRecurrentes>();

        c.FindAll("button").First(b => b.TextContent == "Pausar").Click();

        var cuerpo = api.Cuerpos[$"PUT /api/gastos-recurrentes/{p.Id}"];
        Assert.Contains("\"activo\":false", cuerpo);
        Assert.Contains("\"importe\":850", cuerpo);
    }

    [Fact]
    public void Recurrentes_eliminar_con_gastos_generados_muestra_el_conflicto_y_sugiere_pausar()
    {
        var p = Alquiler();
        var api = ApiRecurrentes(p).Error($"DELETE /api/gastos-recurrentes/{p.Id}", HttpStatusCode.Conflict, "La plantilla ya tiene gastos generados.");
        Registrar(api);
        var c = Render<VistaRecurrentes>();

        c.FindAll("button").First(b => b.TextContent == "Eliminar").Click();
        c.FindAll("button").First(b => b.TextContent == "Sí").Click();

        var error = c.Find("[role=alert]").TextContent;
        Assert.Contains("ya tiene gastos generados", error);
        Assert.Contains("pausa la plantilla", error);
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
                new CuentaComunResponse("2026-10", 0m, 0m, 0m, 0m, [], 0m, [], []) with { Activa = false });
        Registrar(api);

        var c = Render<VistaGastos>();

        Assert.DoesNotContain("la paga directamente", c.Markup);
        Assert.DoesNotContain("Cuenta común", c.Find("form.addcat").InnerHtml); // sin activar, tampoco se ofrece su perfil
    }

    [Fact]
    public void Gasto_de_categoria_a_cargo_de_la_cuenta_propone_su_perfil_y_lo_avisa()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var cuenta = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var partes = new PerfilRepartoDto(Guid.NewGuid(), "Por partes", "partes", [new(AnaId, 1m)]);
        var comunidad = new CategoriaDto(Guid.NewGuid(), "Comunidad", null, cuenta.Id, true);
        var comida = new CategoriaDto(Guid.NewGuid(), "Comida", null, partes.Id);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { comida, comunidad })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { partes, cuenta })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m))
            .Responde("POST /api/gastos", HttpStatusCode.Created,
                new GastoResponse(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), 40m, comunidad.Id, ana.Id, cuenta.Id, null, null, [], true));
        Registrar(api, new HogarResumen(Casa.Id, "Casa", true, false));
        var c = Render<VistaGastos>();
        Assert.DoesNotContain("va por defecto a cargo de la cuenta común", c.Markup); // la primera categoría (Comida) no lo está

        c.FindAll("form.addcat select")[0].Change(comunidad.Id.ToString());
        Assert.Contains("va por defecto a cargo de la cuenta común", c.Markup);
        c.Find("form.addcat input[inputmode=decimal]").Input("40");
        c.Find("form.addcat").Submit();

        Assert.Contains(cuenta.Id.ToString(), api.Cuerpos["POST /api/gastos"]);
    }

    [Fact]
    public void Categoria_marcada_a_cargo_de_la_cuenta_muestra_la_etiqueta_y_envia_el_indicador()
    {
        var cuenta = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta común", "cuenta_comun", []);
        var luz = new CategoriaDto(Guid.NewGuid(), "Luz", null, cuenta.Id, true);
        var api = new ApiFalsa()
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { luz })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { cuenta })
            .Responde("POST /api/categorias", HttpStatusCode.Created, new CategoriaDto(Guid.NewGuid(), "Agua", null, cuenta.Id, true));
        Registrar(api);
        var c = Render<VistaCategorias>();

        Assert.Contains("A cargo de la cuenta común", c.Find(".catrow .c-sel").TextContent);

        c.Find("form.addcat input").Input("Agua");
        c.Find("form.addcat input[type=checkbox]").Change(true);
        c.Find("form.addcat").Submit();

        Assert.Contains("\"aCargoCuentaComun\":true", api.Cuerpos["POST /api/categorias"]);
    }

    [Fact]
    public void Cuenta_comun_sin_activar_remite_a_la_configuracion_solo_a_un_admin()
    {
        var admin = ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { admin })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m) with { Activa = false });
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        Assert.Contains("Sin activar", c.Markup);
        Assert.Empty(c.FindAll("#form-operacion"));
        Assert.Equal("hogar/configuracion", c.Find("a.btn").GetAttribute("href"));
    }

    [Fact]
    public void Cuenta_comun_sin_ahorro_activado_oculta_el_ahorro_y_no_envia_ahorro_en_la_aportacion()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m) with { AhorroActivo = false })
            .Responde("PUT /api/cuenta-comun/aportaciones", HttpStatusCode.OK, new AportacionCuentaDto(Guid.NewGuid(), AnaId, new DateOnly(2026, 10, 1), 100m));
        Registrar(api);
        var c = Render<VistaCuentaComun>();

        Assert.Empty(c.FindAll("#ahorro"));
        Assert.Empty(c.FindAll("#t-ahorro"));
        Assert.Empty(c.FindAll("#ahorro-aportacion"));
        c.Find("#form-operacion input[inputmode=decimal]").Input("100");
        c.Find("#form-operacion").Submit();

        Assert.Contains("\"ahorro\":0", api.Cuerpos["PUT /api/cuenta-comun/aportaciones"]);
    }

    [Fact]
    public async Task Configuracion_pasa_la_liquidacion_a_ciclos_desde_el_selector()
    {
        var api = ApiConHogar(soyAdmin: true)
            .Responde("PUT /api/hogar/liquidacion", HttpStatusCode.OK, new ModoLiquidacionResponse(ModosLiquidacion.Ciclos));
        var c = await ConfiguracionAsync(true, Casa, api);

        var selector = c.FindAll("select").First(s => s.TextContent.Contains("Por ciclos"));
        Assert.Equal("mensual", selector.QuerySelector("option[selected]")?.GetAttribute("value"));
        selector.Change("ciclos");

        Assert.Contains("\"modo\":\"ciclos\"", api.Cuerpos["PUT /api/hogar/liquidacion"]);
        Assert.Contains("Por ciclos: cada vez que el grupo quiera cerrar", c.Markup);
    }

    [Fact]
    public void Resumen_en_modo_ciclos_cierra_la_liquidacion_solo_tras_confirmarlo_y_no_ofrece_pagos()
    {
        var ciclo = new CicloLiquidacionDto(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, AnaId, "Ana",
            new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5), 150m);
        var resumen = new ResumenMensualResponse("2026-10", 80m,
            [new ResumenMiembroDto(AnaId, "Ana", 80m, 40m), new ResumenMiembroDto(LuisId, "Luis", 0m, 40m)], []);
        var api = ApiResumen(resumen, Liquidacion(40m))
            .Responde("GET /api/liquidacion/ciclos", HttpStatusCode.OK, new[] { ciclo })
            .Responde("POST /api/liquidacion/ciclos", HttpStatusCode.Created, ciclo);
        Registrar(api, new HogarResumen(Guid.NewGuid(), "Casa", LiquidacionModo: ModosLiquidacion.Ciclos));

        var c = Render<VistaResumen>();

        Assert.DoesNotContain("Registrar pago", c.Markup);
        Assert.Contains("Liquidación n.º 1", c.Markup);
        Assert.DoesNotContain("POST /api/liquidacion/ciclos", api.Recibidas);

        // Primer clic: pide confirmación en la propia página, sin cerrar nada todavía.
        c.FindAll("button").First(b => b.TextContent.Trim() == "Cerrar liquidación").Click();
        Assert.Contains("¿Cerrar la liquidación?", c.Markup);
        Assert.DoesNotContain("POST /api/liquidacion/ciclos", api.Recibidas);

        c.FindAll("button").First(b => b.TextContent.Contains("Sí, cerrar")).Click();
        Assert.Contains("POST /api/liquidacion/ciclos", api.Recibidas);
    }

    [Fact]
    public void Resumen_en_modo_mensual_no_muestra_el_cierre_de_ciclos()
    {
        var resumen = new ResumenMensualResponse("2026-10", 80m,
            [new ResumenMiembroDto(AnaId, "Ana", 80m, 40m), new ResumenMiembroDto(LuisId, "Luis", 0m, 40m)], []);
        var api = ApiResumen(resumen, Liquidacion(40m));
        Registrar(api, Casa);

        var c = Render<VistaResumen>();

        Assert.DoesNotContain("Cerrar liquidación", c.Markup);
        Assert.DoesNotContain("GET /api/liquidacion/ciclos", api.Recibidas);
        Assert.Contains("Registrar pago", c.Markup);
    }

    /// <summary>Renderiza la configuración en la pestaña Hogar, donde viven las funciones y la eliminación.</summary>
    private async Task<IRenderedComponent<VistaConfiguracionHogar>> ConfiguracionAsync(bool soyAdmin, HogarResumen hogar, ApiFalsa? api = null)
    {
        Registrar(api ?? ApiConHogar(soyAdmin));
        await Services.GetRequiredService<EstadoHogar>().AplicarAsync(new YoResponse(null, [hogar], hogar));
        return Render<VistaConfiguracionHogar>(p => p.Add(x => x.Pestana, "hogar"));
    }

    [Fact]
    public async Task Configuracion_habilita_la_cuenta_comun_y_el_estado_del_hogar_lo_refleja()
    {
        var api = ApiConHogar(soyAdmin: true).Responde("PUT /api/cuenta-comun/activacion", HttpStatusCode.OK, new { activa = true });
        var c = await ConfiguracionAsync(true, Casa, api);
        var ahorro = c.FindAll(".chk input")[1];
        Assert.True(ahorro.HasAttribute("disabled")); // sin cuenta común no hay ahorro

        c.FindAll(".chk input")[0].Change(true);

        Assert.Contains("\"activa\":true", api.Cuerpos["PUT /api/cuenta-comun/activacion"]);
        Assert.True(Services.GetRequiredService<EstadoHogar>().HogarActual!.CuentaComunActiva);
        Assert.False(c.FindAll(".chk input")[1].HasAttribute("disabled"));
    }

    [Fact]
    public async Task Configuracion_activa_el_ahorro_con_la_cuenta_comun_habilitada()
    {
        var api = ApiConHogar(soyAdmin: true).Responde("PUT /api/cuenta-comun/ahorro/activacion", HttpStatusCode.OK, new { activo = true });
        var c = await ConfiguracionAsync(true, Casa with { CuentaComunActiva = true }, api);

        c.FindAll(".chk input")[1].Change(true);

        Assert.Contains("\"activo\":true", api.Cuerpos["PUT /api/cuenta-comun/ahorro/activacion"]);
        Assert.True(Services.GetRequiredService<EstadoHogar>().HogarActual!.AhorroActivo);
    }

    [Fact]
    public async Task Configuracion_desactiva_las_recurrentes_sin_tocar_la_cuenta_ni_el_ahorro()
    {
        var api = ApiConHogar(soyAdmin: true).Responde("PUT /api/hogar/recurrentes", HttpStatusCode.OK, new { activas = false });
        var c = await ConfiguracionAsync(true, Casa with { CuentaComunActiva = true, AhorroActivo = true }, api);

        c.FindAll(".chk input")[2].Change(false);

        Assert.Contains("\"activas\":false", api.Cuerpos["PUT /api/hogar/recurrentes"]);
        var hogar = Services.GetRequiredService<EstadoHogar>().HogarActual!;
        Assert.False(hogar.RecurrentesActivas);
        Assert.True(hogar.CuentaComunActiva);
        Assert.True(hogar.AhorroActivo);
    }

    [Fact]
    public async Task Configuracion_no_deja_cambiar_las_funciones_a_quien_no_es_admin()
    {
        var c = await ConfiguracionAsync(false, Casa with { CuentaComunActiva = true });

        Assert.All(c.FindAll(".chk input"), i => Assert.True(i.HasAttribute("disabled")));
        Assert.Contains("No tienes permiso para cambiar estas funciones", c.Markup);
    }

    [Fact]
    public async Task Configuracion_muestra_el_error_de_la_api_si_rechaza_el_cambio()
    {
        var api = ApiConHogar(soyAdmin: true).Responde("PUT /api/cuenta-comun/activacion", HttpStatusCode.Forbidden, new { error = "No puedes." });
        var c = await ConfiguracionAsync(true, Casa, api);

        c.FindAll(".chk input")[0].Change(true);

        Assert.Contains("No puedes.", c.Markup);
        Assert.False(Services.GetRequiredService<EstadoHogar>().HogarActual!.CuentaComunActiva);
    }

    [Fact]
    public void Cuenta_comun_sin_activar_no_ofrece_activarla_a_quien_no_es_admin()
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana", esYo: true, id: AnaId) })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m) with { Activa = false });
        Registrar(api);
        _permisos.Fijar([]);

        var c = Render<VistaCuentaComun>();

        Assert.Empty(c.FindAll("a.btn"));
        Assert.Contains("No tienes permiso para activarla", c.Markup);
    }

    [Fact]
    public void Cuenta_comun_muestra_su_parte_de_cada_persona()
    {
        var luis = ApiFalsa.Miembro("Luis", id: LuisId);
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var estado = Cuenta(699m) with
        {
            Partes =
            [
                new PartePersonaDto(AnaId, "Ana", 400m, 200m, 50m, 66.67m, 349.5m, 200m, 101m),
                new PartePersonaDto(LuisId, "Luis", 400m, 100m, 50m, 33.33m, 349.5m, 100m, 0m),
            ],
        };
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana, luis })
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, estado);
        Registrar(api);

        var c = Render<VistaCuentaComun>();

        var filas = c.FindAll(".catrow.parte");
        Assert.Equal(2, filas.Count);
        Assert.Contains("349,50", filas[0].TextContent);
        Assert.Contains("50 %", filas[0].TextContent);
        Assert.Contains("La cuenta le debe", filas[0].TextContent);
        Assert.DoesNotContain("La cuenta le debe", filas[1].TextContent);
    }

    [Fact]
    public void Resumen_muestra_la_tabla_de_la_cuenta_comun_del_mes()
    {
        var con = new ResumenMensualResponse("2026-10", 0m, [], [],
            new ResumenCuentaComunDto(500m, 120m, 280m, 400m, 120m, 100m, 100m));
        var fila = new FilaResumenCuentaDto(500m, 120m, 280m, 400m, 120m, 100m);
        var cuenta = Cuenta(0m) with { Resumen = new ResumenCuentaMesDto(fila, fila, fila) };
        Registrar(ApiResumen(con, Liquidacion(0m)).Responde("GET /api/cuenta-comun", HttpStatusCode.OK, cuenta));

        var c = Render<VistaResumen>();

        Assert.Contains("280,00", c.Find("#resumen-cuenta").TextContent);
        Assert.Contains("Pendiente de reembolsar", c.Find("#resumen-cuenta").TextContent);
        Assert.Contains("Ver la cuenta común", c.Markup);
    }

    [Fact]
    public void Resumen_no_muestra_la_cuenta_comun_si_el_hogar_no_la_tiene_activada()
    {
        Registrar(ApiResumen(new ResumenMensualResponse("2026-10", 0m, [], []), Liquidacion(0m)));

        var c = Render<VistaResumen>();

        Assert.Empty(c.FindAll("#resumen-cuenta"));
    }

    [Fact]
    public void Historial_del_hogar_pide_el_permiso_historial_ver()
    {
        Registrar(ApiConHogar(soyAdmin: false));
        _permisos.Fijar([]);
        var sinPermiso = Render<VistaHistorialHogar>();
        Assert.Contains("No tienes permiso para ver el historial", sinPermiso.Markup);
        Assert.Empty(sinPermiso.FindAll("#t-historial"));

        _permisos.Fijar([MiParte.Core.Domain.CatalogoPermisos.HistorialVer]);
        var conPermiso = Render<VistaHistorialHogar>();
        Assert.NotEmpty(conPermiso.FindAll("#t-historial"));
    }

    [Fact]
    public void Gastos_sin_permiso_de_crear_ofrece_aviso_en_vez_del_formulario()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Individual", "individual", []);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { new CategoriaDto(Guid.NewGuid(), "Casa", null, null) })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m));
        Registrar(api);
        _permisos.Fijar([MiParte.Core.Domain.CatalogoPermisos.GastosEditar]);

        var c = Render<VistaGastos>();

        Assert.Empty(c.FindAll("form.addcat"));
        Assert.Contains("No tienes permiso para crear gastos", c.Markup);
    }

    [Fact]
    public void Nuevo_gasto_muestra_solo_el_formulario_de_alta_sin_lista_ni_filtros()
    {
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId);
        var perfil = new PerfilRepartoDto(Guid.NewGuid(), "Individual", "individual", []);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana })
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { new CategoriaDto(Guid.NewGuid(), "Casa", null, null) })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { perfil })
            .Responde("GET /api/gastos", HttpStatusCode.OK, Array.Empty<GastoResponse>())
            .Responde("GET /api/cuenta-comun", HttpStatusCode.OK, Cuenta(0m));
        Registrar(api);
        _permisos.Fijar([MiParte.Core.Domain.CatalogoPermisos.GastosCrear]);

        var c = Render<VistaGastos>(p => p.Add(x => x.SoloAlta, true));

        Assert.Single(c.FindAll("form.addcat"));
        Assert.Empty(c.FindAll(".filtros"));
        Assert.Equal("Nuevo gasto", c.Find("#t-gastos").TextContent.Trim());
    }

    [Fact]
    public void Categorias_sin_permiso_no_ofrece_formulario_ni_acciones()
    {
        var api = new ApiFalsa()
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { new CategoriaDto(Guid.NewGuid(), "Luz", null, null) })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, Array.Empty<PerfilRepartoDto>());
        Registrar(api);
        _permisos.Fijar([]);

        var c = Render<VistaCategorias>();

        Assert.Empty(c.FindAll("#nueva-categoria"));
        Assert.DoesNotContain(c.FindAll("button"), b => b.TextContent.Trim() == "Editar");
        Assert.Contains("No tienes permiso para gestionar categorías", c.Find("section").TextContent);
    }

    [Fact]
    public void Panel_de_permisos_solo_lista_los_adultos_con_cuenta_y_no_muestra_la_matriz()
    {
        var ana = ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId);
        var peque = ApiFalsa.Miembro("Peque", tipo: "a_cargo", vinculado: false);
        Registrar(new ApiFalsa());

        var c = Render<PanelPermisos>(p => p.Add(x => x.Miembros, new List<MiembroDto> { ana, peque }).Add(x => x.Puede, true));

        Assert.Empty(c.FindAll("table"));
        Assert.Single(c.FindAll(".mrow"));
        Assert.Contains("Ana", c.Find(".mrow").TextContent);
        Assert.NotEmpty(c.FindAll("button[aria-label='Permisos de Ana']"));
    }

    [Fact]
    public void Panel_de_permisos_deja_a_un_admin_marcar_y_guardar_los_de_un_miembro()
    {
        var ana = ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId);
        var luis = ApiFalsa.Miembro("Luis", id: LuisId, permisos: [MiParte.Core.Domain.CatalogoPermisos.GastosCrear]);
        var api = new ApiFalsa().Responde("PUT /api/miembros/" + LuisId, HttpStatusCode.OK, luis);
        Registrar(api);
        var c = Render<PanelPermisos>(p => p.Add(x => x.Miembros, new List<MiembroDto> { ana, luis }).Add(x => x.Puede, true));

        Assert.Contains("Todos los permisos", c.Markup); // Ana
        Assert.Contains("1 de 19 permisos", c.Markup); // Luis
        c.Find("button[aria-label='Permisos de Luis']").Click();
        var historial = c.FindAll(".permisos .chk").First(l => l.TextContent.Contains("Ver el historial")).QuerySelector("input")!;
        historial.Change(true);
        c.FindAll("button").First(b => b.TextContent.Trim() == "Guardar permisos").Click();

        var cuerpo = api.Cuerpos["PUT /api/miembros/" + LuisId];
        Assert.Contains("\"gastos.crear\"", cuerpo);
        Assert.Contains("\"historial.ver\"", cuerpo);
        Assert.DoesNotContain("\"gastos.borrar\"", cuerpo);
    }

    [Fact]
    public void Panel_de_permisos_no_ofrece_editar_a_quien_no_es_admin()
    {
        var luis = ApiFalsa.Miembro("Luis", id: LuisId, permisos: []);
        Registrar(new ApiFalsa());

        var c = Render<PanelPermisos>(p => p.Add(x => x.Miembros, new List<MiembroDto> { luis }).Add(x => x.Puede, false));

        Assert.Empty(c.FindAll("button[aria-label='Permisos de Luis']"));
        Assert.Contains("No tienes permiso para cambiar los permisos", c.Markup);
    }

    [Fact]
    public void Panel_de_permisos_deja_editar_tambien_a_un_admin_y_las_plantillas_marcan_su_conjunto()
    {
        var ana = ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId);
        var luis = ApiFalsa.Miembro("Luis", rol: "admin", id: LuisId);
        var api = new ApiFalsa().Responde("PUT /api/miembros/" + LuisId, HttpStatusCode.OK, luis);
        Registrar(api);
        var c = Render<PanelPermisos>(p => p.Add(x => x.Miembros, new List<MiembroDto> { ana, luis }).Add(x => x.Puede, true));

        c.Find("button[aria-label='Permisos de Luis']").Click();
        Assert.Equal(19, c.FindAll(".permisos .chk input").Count(i => i.HasAttribute("checked"))); // un admin parte con todos

        c.FindAll(".plantillas button").First(b => b.TextContent.Trim() == "Solo lectura").Click();
        Assert.Equal(0, c.FindAll(".permisos .chk input").Count(i => i.HasAttribute("checked")));
        c.FindAll(".plantillas button").First(b => b.TextContent.Trim() == "Colaborador").Click();
        Assert.Equal(MiParte.Core.Domain.CatalogoPermisos.PorDefecto.Count, c.FindAll(".permisos .chk input").Count(i => i.HasAttribute("checked")));

        c.FindAll("button").First(b => b.TextContent.Trim() == "Guardar permisos").Click();
        var cuerpo = api.Cuerpos["PUT /api/miembros/" + LuisId];
        Assert.Contains("\"gastos.crear\"", cuerpo);
        Assert.DoesNotContain("\"permisos.gestionar\"", cuerpo);
    }

    [Fact]
    public void Panel_de_permisos_avisa_si_alguien_se_quita_a_si_mismo_el_de_cambiar_permisos()
    {
        var ana = ApiFalsa.Miembro("Ana", rol: "admin", esYo: true, id: AnaId);
        Registrar(new ApiFalsa());
        var c = Render<PanelPermisos>(p => p.Add(x => x.Miembros, new List<MiembroDto> { ana }).Add(x => x.Puede, true));

        c.Find("button[aria-label='Permisos de Ana']").Click();
        Assert.DoesNotContain("Te quitas a ti mismo", c.Markup);
        c.FindAll(".plantillas button").First(b => b.TextContent.Trim() == "Gestor").Click();

        Assert.Contains("Te quitas a ti mismo", c.Markup);
    }

    [Fact]
    public async Task Configuracion_ofrece_cada_gestion_solo_con_su_permiso()
    {
        // Puede invitar y renombrar a otros, pero no añadir miembros ni cambiar funciones.
        var ana = ApiFalsa.Miembro("Ana", esYo: true, id: AnaId, permisos: [MiParte.Core.Domain.CatalogoPermisos.InvitacionesCrear]);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ana, ApiFalsa.Miembro("Luis", id: LuisId) })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, Array.Empty<PerfilRepartoDto>());
        Registrar(api);
        await Services.GetRequiredService<EstadoHogar>().AplicarAsync(new YoResponse(null, [Casa], Casa));

        var c = Render<VistaConfiguracionHogar>();

        Assert.Empty(c.FindAll("form.addm"));
        Assert.All(c.FindAll(".chk input"), i => Assert.True(i.HasAttribute("disabled")));
        Assert.False(c.Find("#card-invite .btn-orange").HasAttribute("disabled"));
        Assert.Empty(c.FindAll("button[aria-label='Permisos de Luis']"));
    }

    [Fact]
    public async Task Eliminar_hogar_muestra_el_aviso_y_solo_se_habilita_al_escribir_el_nombre()
    {
        var c = await ConfiguracionAsync(true, Casa);

        var seccion = c.Find("section.peligro");
        Assert.Contains("irreversible", seccion.TextContent);
        var boton = seccion.QuerySelector("button")!;
        Assert.Equal("Eliminar hogar definitivamente", boton.TextContent.Trim());
        Assert.True(boton.HasAttribute("disabled"));

        c.Find("#confirmar-eliminar").Input("otra cosa");
        Assert.True(c.Find("section.peligro button").HasAttribute("disabled"));
        c.Find("#confirmar-eliminar").Input("Casa");
        Assert.False(c.Find("section.peligro button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Eliminar_hogar_llama_a_la_api_con_el_nombre_y_recarga_los_hogares()
    {
        var otra = new HogarResumen(Guid.NewGuid(), "Playa");
        var api = ApiConHogar(soyAdmin: true)
            .Responde("DELETE /api/hogar", HttpStatusCode.NoContent)
            .Responde("GET /api/yo", HttpStatusCode.OK, new YoResponse(null, [otra], null));
        var c = await ConfiguracionAsync(true, Casa, api);

        c.Find("#confirmar-eliminar").Input("Casa");
        c.Find("section.peligro form").Submit();

        Assert.Contains("DELETE /api/hogar", api.Recibidas);
        Assert.Contains("/api/hogar?nombre=Casa", api.Consultas);
        Assert.Contains("GET /api/yo", api.Recibidas);
        var estado = Services.GetRequiredService<EstadoHogar>();
        Assert.Equal([otra.Id], estado.Hogares.Select(h => h.Id));
        Assert.Equal("Playa", estado.HogarActual!.Nombre); // con un solo hogar restante pasa a ser el actual
    }

    [Fact]
    public async Task Eliminar_hogar_muestra_el_error_de_la_api_y_no_cambia_de_hogar()
    {
        var api = ApiConHogar(soyAdmin: true).Error("DELETE /api/hogar", HttpStatusCode.Forbidden, "No tienes permiso para esta acción.");
        var c = await ConfiguracionAsync(true, Casa, api);

        c.Find("#confirmar-eliminar").Input("Casa");
        c.Find("section.peligro form").Submit();

        Assert.Contains("No tienes permiso para esta acción.", c.Find("section.peligro").TextContent);
        Assert.Equal("Casa", Services.GetRequiredService<EstadoHogar>().HogarActual!.Nombre);
    }

    [Fact]
    public async Task Eliminar_hogar_sin_permiso_no_ofrece_el_boton()
    {
        var c = await ConfiguracionAsync(false, Casa);

        var seccion = c.Find("section.peligro");
        Assert.Contains("irreversible", seccion.TextContent); // el aviso se ve igual
        Assert.Empty(seccion.QuerySelectorAll("button"));
        Assert.Contains("No tienes permiso para eliminar el hogar", seccion.TextContent);
    }
}
