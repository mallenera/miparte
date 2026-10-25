using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado y orquestación de la pestaña Categorías: árbol, alta, edición y borrado, preguntando a qué categoría mover los gastos
/// cuando la que se borra está en uso. No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
public sealed class ModeloCategorias(CoreApiClient api)
{
    // ───── Datos ─────

    /// <summary>Categorías del hogar.</summary>
    public List<CategoriaDto> Categorias { get; private set; } = [];

    /// <summary>Perfiles de reparto del hogar.</summary>
    public List<PerfilRepartoDto> Perfiles { get; private set; } = [];

    /// <summary>Categorías aplanadas en árbol, con su nivel de sangrado.</summary>
    public List<NodoCategoria> Arbol { get; private set; } = [];

    // ───── Estado de pantalla ─────

    /// <summary>Si se están cargando los datos.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Error de la carga.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Error de la última operación.</summary>
    public string? Error { get; private set; }

    /// <summary>Categoría que se está editando.</summary>
    public Guid? Editando { get; set; }

    /// <summary>Categoría cuyo borrado se está confirmando.</summary>
    public Guid? ConfirmandoEliminar { get; set; }

    /// <summary>Categoría en uso para la que se pregunta el destino de sus gastos.</summary>
    public Guid? Moviendo { get; set; }

    // ───── Formularios ─────

    /// <summary>Nombre al editar.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Padre al editar (id como texto; «» = primer nivel).</summary>
    public string Padre { get; set; } = "";

    /// <summary>Perfil por defecto al editar («» = sin perfil).</summary>
    public string Perfil { get; set; } = "";

    /// <summary>Destino elegido para mover los gastos.</summary>
    public string Destino { get; set; } = "";

    /// <summary>Si la categoría que se edita va a cargo de la cuenta común.</summary>
    public bool ACargo { get; set; }

    /// <summary>Si la categoría que se edita está marcada «predeterminada para gastos».</summary>
    public bool Predeterminado { get; set; }

    /// <summary>Nombre de la categoría nueva.</summary>
    public string NuevoNombre { get; set; } = "";

    /// <summary>Padre de la categoría nueva.</summary>
    public string NuevoPadre { get; set; } = "";

    /// <summary>Perfil de la categoría nueva.</summary>
    public string NuevoPerfil { get; set; } = "";

    /// <summary>Si la categoría nueva va a cargo de la cuenta común.</summary>
    public bool NuevoACargo { get; set; }

    /// <summary>Si la categoría nueva se marca «predeterminada para gastos».</summary>
    public bool NuevoPredeterminado { get; set; }

    /// <summary>Si la categoría va a cargo del ahorro de la cuenta común: su perfil es el de cuenta común (ahorro).</summary>
    public bool DelAhorro(CategoriaDto c)
        => c.PerfilRepartoId is { } id && Perfiles.FirstOrDefault(p => p.Id == id)?.Modo == ModosReparto.CuentaComunAhorro;

    /// <summary>Nombre del perfil, «—» si no existe o «Sin perfil» si no tiene.</summary>
    public string NombrePerfil(Guid? id)
        => id is { } g ? Perfiles.FirstOrDefault(p => p.Id == g)?.Nombre ?? "—" : "Sin perfil";

    /// <summary>Nodos del árbol que pueden ser padre de <paramref name="id"/>: ni ella misma ni sus descendientes.</summary>
    public IEnumerable<NodoCategoria> PadresPosibles(Guid id)
    {
        var excluidos = ArbolCategorias.ConDescendientes(Categorias, id);
        return Arbol.Where(n => !excluidos.Contains(n.Categoria.Id));
    }

    private static Guid? ComoGuid(string texto) => Guid.TryParse(texto, out var g) ? g : null;

    // ───── Operaciones ─────

    /// <summary>Marca el modelo como «cargando» antes de pedir los permisos y los datos.</summary>
    public void IniciarCarga()
    {
        Cargando = true;
        ErrorCarga = null;
    }

    /// <summary>Carga categorías y perfiles.</summary>
    public async Task CargarAsync()
    {
        await RecargarAsync();
        Cargando = false;
    }

    private async Task RecargarAsync()
    {
        try
        {
            var categorias = api.ListarCategoriasAsync();
            var perfiles = api.ListarPerfilesAsync();
            await Task.WhenAll(categorias, perfiles);
            Categorias = categorias.Result;
            Perfiles = perfiles.Result;
            Arbol = ArbolCategorias.Aplanar(Categorias);
        }
        catch (ApiException e)
        {
            ErrorCarga = e.Message;
        }
    }

    /// <summary>Prepara el formulario de alta con la categoría elegida como padre.</summary>
    public void NuevaSubcategoria(CategoriaDto padre)
    {
        NuevoPadre = padre.Id.ToString();
        NuevoPerfil = padre.PerfilRepartoId?.ToString() ?? "";
        Editando = null;
        Error = null;
    }

    /// <summary>Carga la categoría en el formulario de edición.</summary>
    public void EmpezarEdicion(CategoriaDto c)
    {
        Editando = c.Id;
        Nombre = c.Nombre;
        Padre = c.CategoriaPadreId?.ToString() ?? "";
        Perfil = c.PerfilRepartoId?.ToString() ?? "";
        ACargo = c.ACargoCuentaComun;
        Predeterminado = c.PredeterminadoGastos;
        Error = null;
    }

    /// <summary>Guarda la edición.</summary>
    public Task<string?> GuardarEdicionAsync(CategoriaDto c) =>
        EjecutarAsync(async () =>
        {
            await api.GuardarCategoriaAsync(c.Id, new GuardarCategoriaRequest(Nombre.Trim(), ComoGuid(Padre), ComoGuid(Perfil), ACargo, Predeterminado));
            Editando = null;
            return "Categoría guardada";
        });

    /// <summary>
    /// Elimina la categoría; si tiene gastos la API lo rechaza como «en uso» y pasa a preguntarse a qué categoría moverlos
    /// (<see cref="Moviendo"/>). Devuelve el aviso, o null si no se eliminó.
    /// </summary>
    public async Task<string?> EliminarAsync(CategoriaDto c)
    {
        Error = null;
        try
        {
            await api.EliminarCategoriaAsync(c.Id);
            ConfirmandoEliminar = null;
            await RecargarAsync();
            return "Categoría eliminada";
        }
        catch (ApiException e) when (e.EnUso)
        {
            ConfirmandoEliminar = null;
            Destino = "";
            Moviendo = c.Id;
            return null;
        }
        catch (ApiException e)
        {
            Error = e.Message;
            ConfirmandoEliminar = null;
            return null;
        }
    }

    /// <summary>Mueve los gastos a la categoría elegida y elimina la categoría.</summary>
    public Task<string?> MoverYEliminarAsync(CategoriaDto c) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarCategoriaAsync(c.Id, ComoGuid(Destino));
            Moviendo = null;
            return "Gastos movidos y categoría eliminada";
        });

    /// <summary>Añade la categoría del formulario de alta.</summary>
    public Task<string?> AnadirAsync() =>
        EjecutarAsync(async () =>
        {
            await api.CrearCategoriaAsync(new GuardarCategoriaRequest(NuevoNombre.Trim(), ComoGuid(NuevoPadre), ComoGuid(NuevoPerfil), NuevoACargo, NuevoPredeterminado));
            NuevoNombre = "";
            NuevoACargo = false;
            NuevoPredeterminado = false;
            return string.IsNullOrEmpty(NuevoPadre) ? "Categoría añadida" : "Subcategoría añadida";
        });

    /// <summary>Ejecuta una operación y recarga la lista; los errores de la API (409 duplicada...) quedan en <see cref="Error"/>.</summary>
    private async Task<string?> EjecutarAsync(Func<Task<string>> accion)
    {
        Error = null;
        try
        {
            var aviso = await accion();
            await RecargarAsync();
            return aviso;
        }
        catch (ApiException e)
        {
            Error = e.Message;
            ConfirmandoEliminar = null;
            return null;
        }
    }
}
