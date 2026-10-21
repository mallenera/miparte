using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Cliente tipado de Core.Api. Cubre usuario, hogares, miembros, invitaciones, categorías, perfiles, gastos, gastos recurrentes, cuenta común, resumen, liquidación y auditoría; el resto se añadirá con cada pantalla.</summary>
public sealed partial class CoreApiClient
{
    private readonly ClienteHttpCore _nucleo;

    /// <summary>Crea el cliente.</summary>
    /// <param name="http">Cliente HTTP con la URL base de Core.Api y el <see cref="ManejadorCoreApi"/>.</param>
    /// <param name="cache">Caché de lecturas compartida por toda la aplicación; sin ella cada lectura va a la API.</param>
    public CoreApiClient(HttpClient http, CacheLecturas? cache = null)
    {
        _nucleo = new ClienteHttpCore(http, cache);
    }
}
