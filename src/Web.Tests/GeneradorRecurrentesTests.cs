using System.Net;
using Microsoft.Extensions.Time.Testing;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Web.Api;
using MiParte.Web.Hogares;

namespace MiParte.Web.Tests;

/// <summary>Generación automática de los gastos recurrentes del mes al entrar en el hogar.</summary>
public class GeneradorRecurrentesTests
{
    private const string Generar = "POST /api/gastos-recurrentes/generar";

    private sealed record Montaje(GeneradorRecurrentes Generador, ApiFalsa Api, AlmacenMemoria Almacen, ServicioAvisos Avisos, EstadoHogar Hogar,
        FakeTimeProvider Reloj, HogarResumen Resumen);

    private static Montaje Crear(bool recurrentesActivas = true, IReadOnlyList<string>? permisos = null, AlmacenMemoria? almacen = null)
    {
        var yo = ApiFalsa.Miembro("Ana", esYo: true, permisos: permisos);
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { yo })
            .Responde(Generar, HttpStatusCode.OK, new GenerarRecurrentesResponse("2026-11", 2, 1));
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        almacen ??= new AlmacenMemoria();
        var hogar = new EstadoHogar(almacen);
        var resumen = new HogarResumen(Guid.NewGuid(), "Casa", false, false, RecurrentesActivas: recurrentesActivas);
        hogar.AplicarAsync(new YoResponse(Guid.NewGuid().ToString(), [resumen], resumen)).GetAwaiter().GetResult();
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        var avisos = new ServicioAvisos();
        var generador = new GeneradorRecurrentes(cliente, hogar, new ServicioPermisos(cliente, hogar), almacen, avisos, reloj);
        return new Montaje(generador, api, almacen, avisos, hogar, reloj, resumen);
    }

    [Fact]
    public async Task Genera_el_mes_en_curso_hasta_hoy_y_avisa_de_lo_creado()
    {
        var m = Crear();

        Assert.Equal(2, await m.Generador.GenerarPendientesAsync());

        Assert.Contains("/api/gastos-recurrentes/generar?mes=2026-11&hastaDia=18", m.Api.Consultas);
        Assert.Equal("Se han creado 2 gastos recurrentes de noviembre", m.Avisos.Texto);
        Assert.Equal("2026-11-18", m.Almacen.Datos[$"miparte.recurrentes.{m.Resumen.Id}"]);
    }

    [Fact]
    public async Task Una_vez_al_dia_por_hogar_aunque_se_pida_a_la_vez_o_se_recargue_la_app()
    {
        var m = Crear();

        await Task.WhenAll(m.Generador.GenerarPendientesAsync(), m.Generador.GenerarPendientesAsync());
        Assert.Single(m.Api.Recibidas, Generar);

        // Otra sesión del mismo navegador ese día no repite la llamada; al día siguiente sí.
        var otra = Crear(almacen: m.Almacen);
        otra.Hogar.AplicarAsync(new YoResponse(Guid.NewGuid().ToString(), [m.Resumen], m.Resumen)).GetAwaiter().GetResult();
        otra.Reloj.SetUtcNow(m.Reloj.GetUtcNow());
        Assert.Equal(0, await otra.Generador.GenerarPendientesAsync());
        Assert.DoesNotContain(Generar, otra.Api.Recibidas);

        otra.Reloj.Advance(TimeSpan.FromDays(1));
        await otra.Generador.GenerarPendientesAsync();
        Assert.Contains("/api/gastos-recurrentes/generar?mes=2026-11&hastaDia=19", otra.Api.Consultas);
    }

    [Fact]
    public async Task Sin_recurrentes_activas_o_sin_permiso_no_genera()
    {
        var inactivas = Crear(recurrentesActivas: false);
        var sinPermiso = Crear(permisos: [CatalogoPermisos.GastosCrear]);

        Assert.Equal(0, await inactivas.Generador.GenerarPendientesAsync());
        Assert.Equal(0, await sinPermiso.Generador.GenerarPendientesAsync());

        Assert.DoesNotContain(Generar, inactivas.Api.Recibidas);
        Assert.DoesNotContain(Generar, sinPermiso.Api.Recibidas);
    }

    [Fact]
    public async Task Un_error_de_la_API_no_se_propaga_ni_se_recuerda_para_otra_sesion()
    {
        var m = Crear();
        m.Api.Error(Generar, HttpStatusCode.Conflict, "El mes está cerrado.");

        Assert.Equal(0, await m.Generador.GenerarPendientesAsync());

        Assert.Null(m.Avisos.Texto);
        Assert.False(m.Almacen.Datos.ContainsKey($"miparte.recurrentes.{m.Resumen.Id}"));
    }

    [Fact]
    public async Task Un_timeout_no_se_propaga_y_la_pagina_puede_seguir()
    {
        var m = Crear();
        m.Api.Responde(Generar, _ => throw new TaskCanceledException("timeout"));

        Assert.Equal(0, await m.Generador.GenerarPendientesAsync());
        Assert.Equal(0, await m.Generador.GenerarPendientesAsync());

        Assert.False(m.Almacen.Datos.ContainsKey($"miparte.recurrentes.{m.Resumen.Id}"));
    }

    [Fact]
    public async Task Si_se_cambia_de_hogar_durante_la_llamada_no_marca_el_hogar_de_partida()
    {
        var m = Crear();
        var otro = new HogarResumen(Guid.NewGuid(), "Piso", false, false, RecurrentesActivas: true);
        m.Api.Responde(Generar, _ =>
        {
            m.Hogar.AplicarAsync(new YoResponse(Guid.NewGuid().ToString(), [m.Resumen, otro], otro)).GetAwaiter().GetResult();
            m.Hogar.SeleccionarAsync(otro.Id).GetAwaiter().GetResult();
            return ApiFalsa.Json(HttpStatusCode.OK, new GenerarRecurrentesResponse("2026-11", 2, 1));
        });

        Assert.Equal(0, await m.Generador.GenerarPendientesAsync());

        Assert.False(m.Almacen.Datos.ContainsKey($"miparte.recurrentes.{m.Resumen.Id}"));
        Assert.Null(m.Avisos.Texto);
    }
}
