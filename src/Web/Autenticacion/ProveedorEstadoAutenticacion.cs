using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MiParte.Web.Autenticacion;

/// <summary>Expone la sesión de Supabase a <c>AuthorizeView</c> y <c>AuthorizeRouteView</c>.</summary>
public sealed class ProveedorEstadoAutenticacion : AuthenticationStateProvider, IDisposable
{
    private readonly ServicioSesion _sesion;

    /// <summary>Crea el proveedor y se suscribe a los cambios de sesión.</summary>
    /// <param name="sesion">Servicio de sesión.</param>
    public ProveedorEstadoAutenticacion(ServicioSesion sesion)
    {
        _sesion = sesion;
        _sesion.SesionCambiada += AlCambiarSesion;
    }

    /// <inheritdoc />
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _sesion.InicializarAsync();
        return new AuthenticationState(Construir(_sesion.SesionActual));
    }

    /// <inheritdoc />
    public void Dispose() => _sesion.SesionCambiada -= AlCambiarSesion;

    private void AlCambiarSesion() =>
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Construir(_sesion.SesionActual))));

    private static ClaimsPrincipal Construir(SesionSupabase? sesion)
    {
        if (sesion is null) return new ClaimsPrincipal(new ClaimsIdentity());
        var claims = new List<Claim> { new("sub", sesion.UserId), new(ClaimTypes.NameIdentifier, sesion.UserId) };
        if (sesion.Email is not null) claims.Add(new Claim(ClaimTypes.Name, sesion.Email));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "supabase"));
    }
}
