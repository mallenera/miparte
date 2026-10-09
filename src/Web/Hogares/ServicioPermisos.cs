using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Web.Api;

namespace MiParte.Web.Hogares;

/// <summary>
/// Permisos del usuario en el hogar actual (los de su <see cref="MiembroDto"/>), para ocultar o deshabilitar lo que la API
/// rechazaría con 403. Se vuelve a cargar al cambiar de hogar. La API es quien decide: esto solo evita ofrecer lo que no se puede hacer.
/// </summary>
public sealed class ServicioPermisos
{
    private readonly CoreApiClient _api;
    private readonly EstadoHogar _hogar;
    private IReadOnlySet<string> _claves = new HashSet<string>();
    private Guid? _hogarId;
    private bool _cargado;
    private bool _esAdmin;

    /// <summary>Se dispara cuando cambian los permisos del usuario (carga, cambio de hogar o edición).</summary>
    public event Action? Cambiado;

    /// <summary>Crea el servicio y lo engancha al cambio de hogar.</summary>
    /// <param name="api">Cliente de Core.Api.</param>
    /// <param name="hogar">Estado del hogar seleccionado.</param>
    public ServicioPermisos(CoreApiClient api, EstadoHogar hogar)
    {
        _api = api;
        _hogar = hogar;
        _hogar.Cambiado += AlCambiarHogar;
    }

    /// <summary>Si ya se conocen los permisos del hogar actual.</summary>
    public bool Cargado => _cargado;

    /// <summary>Si el usuario es admin del hogar actual (los admins lo tienen todo y gestionan miembros y funciones).</summary>
    public bool EsAdmin => _esAdmin;

    /// <summary>Si el usuario tiene el permiso <paramref name="clave"/> en el hogar actual.</summary>
    /// <param name="clave">Clave de <see cref="CatalogoPermisos"/>.</param>
    public bool Tiene(string clave) => _claves.Contains(clave);

    /// <summary>Carga los permisos si aún no se conocen para el hogar actual; si la carga falla se queda sin permisos.</summary>
    public async Task AsegurarAsync()
    {
        if (_cargado && _hogarId == _hogar.HogarActual?.Id) return;
        try
        {
            Establecer(await _api.ListarMiembrosAsync());
        }
        catch (ApiException)
        {
            Fijar([]);
        }
    }

    /// <summary>Fija los permisos a partir de la lista de miembros del hogar actual (el marcado como <c>EsYo</c>).</summary>
    /// <param name="miembros">Miembros del hogar actual.</param>
    public void Establecer(IEnumerable<MiembroDto> miembros)
    {
        var yo = miembros.FirstOrDefault(m => m.EsYo);
        Fijar(yo?.Permisos ?? [], yo?.Rol == "admin");
    }

    /// <summary>Fija directamente los permisos del usuario en el hogar actual.</summary>
    /// <param name="claves">Claves concedidas.</param>
    /// <param name="esAdmin">Si el usuario es admin.</param>
    public void Fijar(IEnumerable<string> claves, bool esAdmin = false)
    {
        _claves = claves.ToHashSet();
        _esAdmin = esAdmin;
        _hogarId = _hogar.HogarActual?.Id;
        _cargado = true;
        Cambiado?.Invoke();
    }

    private void AlCambiarHogar()
    {
        if (_cargado && _hogarId == _hogar.HogarActual?.Id) return;
        _cargado = false;
        _claves = new HashSet<string>();
        _esAdmin = false;
        Cambiado?.Invoke();
        if (_hogar.HogarActual is not null) _ = AsegurarAsync();
    }
}
