using System.Net;
using System.Net.Http.Headers;
using MiParte.Web.Autenticacion;
using MiParte.Web.Hogares;

namespace MiParte.Web.Api;

/// <summary>
/// Añade <c>Authorization: Bearer</c> (renovando el token si hace falta) y <c>X-Hogar-Id</c> a las llamadas a Core.Api.
/// Si la API responde 401 descarta la sesión local, lo que devuelve al usuario a la pantalla de acceso.
/// </summary>
public sealed class ManejadorCoreApi : DelegatingHandler
{
    private readonly ServicioSesion _sesion;
    private readonly EstadoHogar _hogar;

    /// <summary>Crea el manejador.</summary>
    /// <param name="sesion">Servicio de sesión.</param>
    /// <param name="hogar">Estado del hogar seleccionado.</param>
    public ManejadorCoreApi(ServicioSesion sesion, EstadoHogar hogar)
    {
        _sesion = sesion;
        _hogar = hogar;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _sesion.ObtenerTokenAsync();
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (_hogar.HogarActual is { } hogar && !request.Headers.Contains("X-Hogar-Id"))
            request.Headers.Add("X-Hogar-Id", hogar.Id.ToString());

        var respuesta = await base.SendAsync(request, cancellationToken);
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized && token is not null) await _sesion.DescartarAsync();
        return respuesta;
    }
}
