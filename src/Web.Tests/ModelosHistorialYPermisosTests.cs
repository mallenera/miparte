using System.Net;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Web.Api;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Historial y matriz de permisos probados sin renderizar componentes.</summary>
public class ModeloHistorialTests
{
    private static EventoAuditoriaDto Evento(int minuto) => new(
        Guid.NewGuid(), new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(-minuto), null, "Ana", "crear", "gasto",
        Guid.NewGuid(), null, null);

    private (ModeloHistorial Modelo, ApiFalsa Api) Crear(Func<HttpRequestMessage, HttpResponseMessage> auditoria)
    {
        var api = new ApiFalsa()
            .Responde("GET /api/miembros", HttpStatusCode.OK, new[] { ApiFalsa.Miembro("Ana") })
            .Responde("GET /api/auditoria", auditoria);
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloHistorial(cliente), api);
    }

    [Fact]
    public async Task Una_pagina_incompleta_no_ofrece_cargar_mas()
    {
        var (m, _) = Crear(_ => ApiFalsa.Json(HttpStatusCode.OK, new[] { Evento(1), Evento(2) }));

        await m.IniciarAsync();

        Assert.False(m.Cargando);
        Assert.Equal(2, m.Eventos.Count);
        Assert.False(m.HayMas);
        Assert.Single(m.Miembros);
    }

    [Fact]
    public async Task Una_pagina_completa_ofrece_mas_y_el_cursor_es_el_ultimo_evento()
    {
        var primera = Enumerable.Range(1, ModeloHistorial.PorPagina).Select(Evento).ToArray();
        var llamadas = 0;
        var (m, api) = Crear(_ => ApiFalsa.Json(HttpStatusCode.OK, llamadas++ == 0 ? primera : new[] { Evento(99) }));
        await m.IniciarAsync();
        Assert.True(m.HayMas);

        await m.CargarMasAsync();

        Assert.Equal(ModeloHistorial.PorPagina + 1, m.Eventos.Count);
        Assert.False(m.HayMas);
        var ultimo = primera[^1];
        Assert.Contains(api.Consultas, c => c.Contains("despuesDeId=" + ultimo.Id));
    }

    [Fact]
    public async Task Cambiar_el_filtro_vuelve_a_la_primera_pagina_con_ese_tipo()
    {
        var (m, api) = Crear(_ => ApiFalsa.Json(HttpStatusCode.OK, new[] { Evento(1) }));
        await m.IniciarAsync();

        m.Filtro = "gasto";
        await m.CargarAsync();

        Assert.Contains(api.Consultas, c => c.Contains("entidad=gasto"));
        Assert.Single(m.Eventos);
    }

    [Fact]
    public async Task Un_error_de_la_primera_pagina_queda_en_Error()
    {
        var (m, _) = Crear(_ => ApiFalsa.Json(HttpStatusCode.Forbidden, new { error = "Sin permiso." }));

        await m.IniciarAsync();

        Assert.Equal("Sin permiso.", m.Error);
        Assert.Empty(m.Eventos);
        Assert.False(m.Cargando);
    }

    [Fact]
    public async Task Si_fallan_los_nombres_el_historial_se_sigue_viendo()
    {
        var (m, api) = Crear(_ => ApiFalsa.Json(HttpStatusCode.OK, new[] { Evento(1) }));
        api.Error("GET /api/miembros", HttpStatusCode.InternalServerError, "x");

        await m.IniciarAsync();

        Assert.Null(m.Error);
        Assert.Single(m.Eventos);
        Assert.Null(m.NombreMiembro(Guid.NewGuid()));
    }
}

public class ModeloPermisosTests
{
    private static MiembroDto Miembro(IReadOnlyList<string> permisos) => ApiFalsa.Miembro("Marcos", permisos: permisos);

    private (ModeloPermisos Modelo, ApiFalsa Api) Crear()
    {
        var api = new ApiFalsa();
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloPermisos(cliente), api);
    }

    [Fact]
    public void Abrir_el_editor_parte_de_los_permisos_actuales_y_volver_a_pulsar_lo_cierra()
    {
        var (m, _) = Crear();
        var marcos = Miembro([CatalogoPermisos.GastosCrear]);

        m.Alternar(marcos);

        Assert.Equal(marcos.Id, m.Abierto);
        Assert.True(m.Marcado(CatalogoPermisos.GastosCrear));
        Assert.False(m.Marcado(CatalogoPermisos.GastosBorrar));

        m.Alternar(marcos);
        Assert.Null(m.Abierto);
    }

    [Fact]
    public void Aplicar_una_plantilla_sustituye_el_borrador_y_cambia_la_version_de_las_casillas()
    {
        var (m, _) = Crear();
        m.Alternar(Miembro([CatalogoPermisos.GastosCrear]));
        var version = m.Version;

        m.Aplicar([CatalogoPermisos.GastosBorrar]);

        Assert.False(m.Marcado(CatalogoPermisos.GastosCrear));
        Assert.True(m.Marcado(CatalogoPermisos.GastosBorrar));
        Assert.NotEqual(version, m.Version);
    }

    [Fact]
    public async Task Guardar_envia_solo_claves_del_catalogo_en_orden_y_cierra_el_editor()
    {
        var (m, api) = Crear();
        var marcos = Miembro([]);
        api.Responde($"PUT /api/miembros/{marcos.Id}", HttpStatusCode.OK, marcos);
        m.Alternar(marcos);
        m.Marcar(CatalogoPermisos.GastosBorrar, true);
        m.Marcar(CatalogoPermisos.GastosCrear, true);
        m.Marcar("no.existe", true);

        var aviso = await m.GuardarAsync(marcos);

        Assert.Equal("Permisos de Marcos guardados", aviso);
        var cuerpo = api.Cuerpos[$"PUT /api/miembros/{marcos.Id}"];
        Assert.Contains(CatalogoPermisos.GastosCrear, cuerpo);
        Assert.DoesNotContain("no.existe", cuerpo);
        Assert.Null(m.Abierto);
    }

    [Fact]
    public async Task Si_la_api_rechaza_el_cambio_deja_el_error_y_el_editor_abierto()
    {
        var (m, api) = Crear();
        var marcos = Miembro([]);
        api.Error($"PUT /api/miembros/{marcos.Id}", HttpStatusCode.Conflict, "El hogar debe conservar al menos un miembro que pueda cambiar los permisos.");
        m.Alternar(marcos);

        var aviso = await m.GuardarAsync(marcos);

        Assert.Null(aviso);
        Assert.Contains("conservar al menos", m.Error);
        Assert.Equal(marcos.Id, m.Abierto);
        Assert.False(m.Guardando);
    }
}
