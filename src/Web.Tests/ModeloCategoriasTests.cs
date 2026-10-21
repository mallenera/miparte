using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Modelos;

namespace MiParte.Web.Tests;

/// <summary>Orquestación de la pestaña Categorías probada sin renderizar componentes.</summary>
public class ModeloCategoriasTests
{
    private readonly CategoriaDto _casa = new(Guid.NewGuid(), "Casa", null, null);
    private readonly CategoriaDto _cocina;
    private readonly CategoriaDto _ocio = new(Guid.NewGuid(), "Ocio", null, null);

    public ModeloCategoriasTests() => _cocina = new CategoriaDto(Guid.NewGuid(), "Cocina", _casa.Id, null);

    private (ModeloCategorias Modelo, ApiFalsa Api) Crear()
    {
        var api = new ApiFalsa()
            .Responde("GET /api/categorias", HttpStatusCode.OK, new[] { _casa, _cocina, _ocio })
            .Responde("GET /api/perfiles", HttpStatusCode.OK, new[] { new PerfilRepartoDto(Guid.NewGuid(), "Individual", "individual", []) });
        var cliente = new CoreApiClient(new HttpClient(api) { BaseAddress = new Uri("http://localhost:5001/") });
        return (new ModeloCategorias(cliente), api);
    }

    [Fact]
    public async Task Cargar_arma_el_arbol_y_no_ofrece_como_padre_a_la_propia_ni_a_sus_descendientes()
    {
        var (m, _) = Crear();

        await m.CargarAsync();

        Assert.False(m.Cargando);
        Assert.Equal(3, m.Arbol.Count);
        var posibles = m.PadresPosibles(_casa.Id).Select(n => n.Categoria.Id).ToList();
        Assert.Equal([_ocio.Id], posibles);
    }

    [Fact]
    public async Task Eliminar_una_categoria_sin_uso_la_borra_y_recarga()
    {
        var (m, api) = Crear();
        api.Responde($"DELETE /api/categorias/{_ocio.Id}", HttpStatusCode.NoContent);
        await m.CargarAsync();

        var aviso = await m.EliminarAsync(_ocio);

        Assert.Equal("Categoría eliminada", aviso);
        Assert.Equal(2, api.Recibidas.Count(r => r == "GET /api/categorias"));
        Assert.Null(m.Moviendo);
    }

    [Fact]
    public async Task Eliminar_una_categoria_en_uso_pregunta_a_cual_mover_sus_gastos()
    {
        var (m, api) = Crear();
        api.Responde($"DELETE /api/categorias/{_ocio.Id}", HttpStatusCode.Conflict,
            new { error = "La categoría tiene gastos asociados.", enUso = true });
        await m.CargarAsync();
        m.ConfirmandoEliminar = _ocio.Id;

        var aviso = await m.EliminarAsync(_ocio);

        Assert.Null(aviso);
        Assert.Equal(_ocio.Id, m.Moviendo);
        Assert.Null(m.ConfirmandoEliminar);
        Assert.Null(m.Error); // no es un error: es una pregunta
    }

    [Fact]
    public async Task Mover_y_eliminar_envia_el_destino_elegido()
    {
        var (m, api) = Crear();
        api.Responde($"DELETE /api/categorias/{_ocio.Id}", HttpStatusCode.NoContent);
        await m.CargarAsync();
        m.Moviendo = _ocio.Id;
        m.Destino = _casa.Id.ToString();

        var aviso = await m.MoverYEliminarAsync(_ocio);

        Assert.Equal("Gastos movidos y categoría eliminada", aviso);
        Assert.Contains($"/api/categorias/{_ocio.Id}?moverA={_casa.Id}", api.Consultas);
        Assert.Null(m.Moviendo);
    }

    [Fact]
    public async Task Un_error_distinto_de_en_uso_queda_en_Error()
    {
        var (m, api) = Crear();
        api.Error($"DELETE /api/categorias/{_casa.Id}", HttpStatusCode.Conflict, "La categoría tiene subcategorías.");
        await m.CargarAsync();

        var aviso = await m.EliminarAsync(_casa);

        Assert.Null(aviso);
        Assert.Contains("subcategorías", m.Error);
        Assert.Null(m.Moviendo);
    }

    [Fact]
    public async Task Nueva_subcategoria_hereda_padre_y_perfil_y_cierra_la_edicion()
    {
        var perfil = Guid.NewGuid();
        var (m, _) = Crear();
        await m.CargarAsync();
        m.Editando = _ocio.Id;

        m.NuevaSubcategoria(_casa with { PerfilRepartoId = perfil });

        Assert.Equal(_casa.Id.ToString(), m.NuevoPadre);
        Assert.Equal(perfil.ToString(), m.NuevoPerfil);
        Assert.Null(m.Editando);
    }

    [Fact]
    public async Task Editar_y_guardar_envia_los_campos_del_formulario()
    {
        var (m, api) = Crear();
        api.Responde($"PUT /api/categorias/{_cocina.Id}", HttpStatusCode.OK, _cocina);
        await m.CargarAsync();

        m.EmpezarEdicion(_cocina);
        Assert.Equal("Cocina", m.Nombre);
        Assert.Equal(_casa.Id.ToString(), m.Padre);
        m.Nombre = "  Cocina grande ";
        var aviso = await m.GuardarEdicionAsync(_cocina);

        Assert.Equal("Categoría guardada", aviso);
        Assert.Contains("\"nombre\":\"Cocina grande\"", api.Cuerpos[$"PUT /api/categorias/{_cocina.Id}"]);
        Assert.Null(m.Editando);
    }

    [Fact]
    public async Task Predeterminada_para_gastos_viaja_al_editar_y_al_crear()
    {
        var marcada = new CategoriaDto(_cocina.Id, "Cocina", _casa.Id, null, false, true);
        var (m, api) = Crear();
        api.Responde($"PUT /api/categorias/{_cocina.Id}", HttpStatusCode.OK, marcada);
        api.Responde("POST /api/categorias", HttpStatusCode.Created, _ocio);
        await m.CargarAsync();

        m.EmpezarEdicion(marcada);
        Assert.True(m.Predeterminado);
        await m.GuardarEdicionAsync(marcada);
        Assert.Contains("\"predeterminadoGastos\":true", api.Cuerpos[$"PUT /api/categorias/{_cocina.Id}"]);

        m.NuevoNombre = "Super";
        m.NuevoPredeterminado = true;
        await m.AnadirAsync();
        Assert.Contains("\"predeterminadoGastos\":true", api.Cuerpos["POST /api/categorias"]);
        Assert.False(m.NuevoPredeterminado);
    }

    [Fact]
    public async Task Anadir_una_subcategoria_lo_avisa_y_limpia_el_nombre()
    {
        var (m, api) = Crear();
        api.Responde("POST /api/categorias", HttpStatusCode.Created, _cocina);
        await m.CargarAsync();
        m.NuevoNombre = "Baño";
        m.NuevoPadre = _casa.Id.ToString();

        var aviso = await m.AnadirAsync();

        Assert.Equal("Subcategoría añadida", aviso);
        Assert.Equal("", m.NuevoNombre);
    }
}
