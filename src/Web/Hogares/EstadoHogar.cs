using MiParte.Contracts;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Hogares;

/// <summary>Resultado de aplicar <c>GET /api/yo</c>: a dónde debe ir el usuario a continuación.</summary>
public enum DestinoArranque
{
    /// <summary>No pertenece a ningún hogar: crear uno o aceptar una invitación.</summary>
    SinHogar,

    /// <summary>Pertenece a varios hogares y no hay uno elegido.</summary>
    ElegirHogar,

    /// <summary>Hay un hogar actual seleccionado.</summary>
    Listo,
}

/// <summary>
/// Hogares del usuario y hogar seleccionado. La elección se recuerda en <c>localStorage</c> y el hogar actual
/// se envía como <c>X-Hogar-Id</c> en las llamadas a Core.Api.
/// </summary>
public sealed class EstadoHogar
{
    private const string ClaveAlmacen = "miparte.hogar";

    private readonly IAlmacenLocal _almacen;

    /// <summary>Se dispara cuando cambia el hogar actual o la lista de hogares.</summary>
    public event Action? Cambiado;

    /// <summary>Hogares a los que pertenece el usuario.</summary>
    public IReadOnlyList<HogarResumen> Hogares { get; private set; } = [];

    /// <summary>Hogar seleccionado, o null si aún no hay ninguno.</summary>
    public HogarResumen? HogarActual { get; private set; }

    /// <summary>Indica si ya se ha cargado <c>/api/yo</c> en esta sesión de la aplicación.</summary>
    public bool Cargado { get; private set; }

    /// <summary>Crea el estado.</summary>
    /// <param name="almacen">Almacén persistente del navegador.</param>
    public EstadoHogar(IAlmacenLocal almacen) => _almacen = almacen;

    /// <summary>
    /// Aplica la respuesta de <c>/api/yo</c>: recupera la elección guardada si sigue siendo válida; con un solo hogar lo
    /// selecciona; con varios y sin elección guardada válida pide elegir.
    /// </summary>
    /// <param name="yo">Respuesta de <c>/api/yo</c>.</param>
    public async Task<DestinoArranque> AplicarAsync(YoResponse yo)
    {
        Hogares = yo.Hogares;
        Cargado = true;

        if (Hogares.Count == 0)
        {
            await FijarAsync(null);
            return DestinoArranque.SinHogar;
        }

        var guardado = Guid.TryParse(await _almacen.LeerAsync(ClaveAlmacen), out var id) ? id : (Guid?)null;
        var elegido = Hogares.FirstOrDefault(h => h.Id == guardado)
                      ?? (Hogares.Count == 1 ? Hogares[0] : null);
        await FijarAsync(elegido);
        return elegido is null ? DestinoArranque.ElegirHogar : DestinoArranque.Listo;
    }

    /// <summary>Selecciona un hogar de la lista y lo recuerda.</summary>
    /// <param name="id">Identificador del hogar.</param>
    /// <exception cref="ArgumentException">El hogar no está entre los del usuario.</exception>
    public async Task SeleccionarAsync(Guid id)
    {
        var hogar = Hogares.FirstOrDefault(h => h.Id == id)
                    ?? throw new ArgumentException("El hogar no pertenece al usuario.", nameof(id));
        await FijarAsync(hogar);
    }

    /// <summary>
    /// Actualiza las funciones activadas del hogar actual (cuenta común y ahorro) tras cambiarlas, para que el menú y las
    /// pestañas reaccionen sin volver a pedir <c>/api/yo</c>.
    /// </summary>
    /// <param name="cuentaComunActiva">Si el hogar usa la cuenta común.</param>
    /// <param name="ahorroActivo">Si el hogar usa el ahorro.</param>
    public void ActualizarFunciones(bool cuentaComunActiva, bool ahorroActivo)
    {
        if (HogarActual is not { } actual) return;
        var nuevo = actual with { CuentaComunActiva = cuentaComunActiva, AhorroActivo = ahorroActivo };
        Hogares = Hogares.Select(h => h.Id == nuevo.Id ? nuevo : h).ToList();
        HogarActual = nuevo;
        Cambiado?.Invoke();
    }

    /// <summary>Olvida hogares y elección (al cerrar sesión), para que otro usuario del mismo navegador no la herede.</summary>
    public async Task LimpiarAsync()
    {
        Hogares = [];
        Cargado = false;
        await FijarAsync(null);
    }

    private async Task FijarAsync(HogarResumen? hogar)
    {
        HogarActual = hogar;
        if (hogar is null) await _almacen.EliminarAsync(ClaveAlmacen);
        else await _almacen.EscribirAsync(ClaveAlmacen, hogar.Id.ToString());
        Cambiado?.Invoke();
    }
}
