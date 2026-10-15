using MiParte.Contracts;
using MiParte.Web.Api;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado y paginación del historial de cambios del hogar: lista con filtro por tipo y «Cargar más» con cursor compuesto. Descarta
/// la respuesta de una carga anterior si el filtro cambió entretanto. No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
public sealed class ModeloHistorial(CoreApiClient api)
{
    /// <summary>Eventos por página.</summary>
    public const int PorPagina = 50;

    private int _generacion;

    /// <summary>Tipo de entidad elegido en el filtro («» = todo).</summary>
    public string Filtro { get; set; } = "";

    /// <summary>Eventos cargados, del más reciente al más antiguo.</summary>
    public List<EventoAuditoriaDto> Eventos { get; } = [];

    /// <summary>Miembros, incluidos los desactivados: un cambio antiguo puede referirse a alguien que ya no está activo.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Si se está cargando la primera página.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Si se está cargando una página siguiente.</summary>
    public bool CargandoMas { get; private set; }

    /// <summary>Si puede haber más páginas (la última vino completa).</summary>
    public bool HayMas { get; private set; }

    /// <summary>Error de la primera página.</summary>
    public string? Error { get; private set; }

    /// <summary>Error de una página siguiente.</summary>
    public string? ErrorMas { get; private set; }

    /// <summary>Nombre del miembro, o null si no se conoce.</summary>
    public string? NombreMiembro(Guid id) => Miembros.FirstOrDefault(m => m.Id == id)?.Nombre;

    private string? FiltroONull => string.IsNullOrEmpty(Filtro) ? null : Filtro;

    /// <summary>Carga los nombres de los miembros (opcionales) y la primera página.</summary>
    public async Task IniciarAsync()
    {
        try
        {
            Miembros = await api.ListarMiembrosAsync(incluirInactivos: true);
        }
        catch (ApiException)
        {
            // Sin nombres el detalle omite los campos que son personas, pero el historial sigue siendo útil.
        }

        await CargarAsync();
    }

    /// <summary>Vuelve a la primera página con el filtro actual.</summary>
    public async Task CargarAsync()
    {
        var generacion = ++_generacion;
        Cargando = true;
        CargandoMas = false;
        Error = ErrorMas = null;
        Eventos.Clear();
        try
        {
            var pagina = await api.ListarAuditoriaAsync(FiltroONull, PorPagina);
            if (generacion != _generacion) return;
            Eventos.AddRange(pagina);
            HayMas = pagina.Count == PorPagina;
        }
        catch (ApiException e) when (generacion == _generacion)
        {
            Error = e.Message;
        }

        if (generacion == _generacion) Cargando = false;
    }

    /// <summary>Pide la página siguiente a partir del último evento cargado.</summary>
    public async Task CargarMasAsync()
    {
        if (Eventos.Count == 0) return;
        var generacion = _generacion;
        var ultimo = Eventos[^1];
        CargandoMas = true;
        ErrorMas = null;
        try
        {
            var pagina = await api.ListarAuditoriaAsync(FiltroONull, PorPagina, ultimo.Cuando, ultimo.Id);
            if (generacion != _generacion) return;
            Eventos.AddRange(pagina);
            HayMas = pagina.Count == PorPagina;
        }
        catch (ApiException e) when (generacion == _generacion)
        {
            ErrorMas = e.Message;
        }

        if (generacion == _generacion) CargandoMas = false;
    }
}
