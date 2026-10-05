using MiParte.Web.Api;

namespace MiParte.Web.Hogares;

/// <summary>Flujo de arranque de <c>docs/api.md</c> §2: carga <c>/api/yo</c> y decide a qué pantalla ir.</summary>
public sealed class ServicioArranque
{
    private readonly CoreApiClient _api;
    private readonly EstadoHogar _hogar;

    /// <summary>Crea el servicio.</summary>
    /// <param name="api">Cliente de Core.Api.</param>
    /// <param name="hogar">Estado del hogar seleccionado.</param>
    public ServicioArranque(CoreApiClient api, EstadoHogar hogar)
    {
        _api = api;
        _hogar = hogar;
    }

    /// <summary>Recarga usuario y hogares y devuelve el destino según cuántos hogares tiene el usuario.</summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ApiException">Error de la API o sin conexión.</exception>
    public async Task<DestinoArranque> CargarAsync(CancellationToken ct = default) =>
        await _hogar.AplicarAsync(await _api.YoAsync(ct));
}
