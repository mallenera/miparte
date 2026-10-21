using MiParte.Contracts;

namespace MiParte.Web.Api;

/// <summary>Usuario, hogares, miembros, invitaciones, enlaces de grupo y auditoría.</summary>
public sealed partial class CoreApiClient
{
    /// <summary>Usuario autenticado, sus hogares y el hogar actual (<c>GET /api/yo</c>).</summary>
    /// <param name="ct">Token de cancelación.</param>
    public Task<YoResponse> YoAsync(CancellationToken ct = default) => _nucleo.ObtenerAsync<YoResponse>("api/yo", ct);

    /// <summary>Crea un hogar del que el usuario es admin (<c>POST /api/hogares</c>).</summary>
    /// <param name="peticion">Nombre del hogar y del miembro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> CrearHogarAsync(CrearHogarRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<HogarResumen>(HttpMethod.Post, "api/hogares", peticion, ct);

    /// <summary>Acepta una invitación y devuelve el hogar al que se une (<c>POST /api/invitaciones/aceptar</c>).</summary>
    /// <param name="peticion">Token de la invitación y, si hace falta, el nombre del nuevo miembro.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> AceptarInvitacionAsync(AceptarInvitacionRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<HogarResumen>(HttpMethod.Post, "api/invitaciones/aceptar", peticion, ct);

    /// <summary>
    /// Une al usuario al hogar de un enlace de grupo reutilizable (<c>POST /api/enlaces-grupo/unirse</c>, sin hogar actual).
    /// Sin <c>Nombre</c> no se une a nadie: 400 si el enlace es válido y pide nombre, 404 si el código no es de un grupo.
    /// </summary>
    /// <param name="peticion">Código del enlace y, para entrar como miembro nuevo, el nombre.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<HogarResumen> UnirseEnlaceGrupoAsync(UnirseEnlaceGrupoRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<HogarResumen>(HttpMethod.Post, "api/enlaces-grupo/unirse", peticion, ct);

    /// <summary>Miembros activos del hogar actual (<c>GET /api/miembros</c>); <c>EsYo</c> marca al usuario autenticado.</summary>
    /// <param name="incluirInactivos">Incluye también a los desactivados (<c>?incluirInactivos=true</c>), para poner nombre a quien aparece en el historial.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<MiembroDto>> ListarMiembrosAsync(bool incluirInactivos = false, CancellationToken ct = default) =>
        _nucleo.ObtenerListaAsync<MiembroDto>(incluirInactivos ? "api/miembros?incluirInactivos=true" : "api/miembros", ct);

    /// <summary>Añade una persona sin cuenta (<c>POST /api/miembros</c>, solo admin).</summary>
    /// <param name="peticion">Nombre, tipo y responsable (si es a cargo).</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MiembroDto> CrearMiembroAsync(CrearMiembroRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<MiembroDto>(HttpMethod.Post, "api/miembros", peticion, ct);

    /// <summary>Edita un miembro; los campos null no cambian (<c>PUT /api/miembros/{id}</c>).</summary>
    /// <param name="id">Miembro a editar.</param>
    /// <param name="peticion">Cambios.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<MiembroDto> ActualizarMiembroAsync(Guid id, ActualizarMiembroRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<MiembroDto>(HttpMethod.Put, $"api/miembros/{id}", peticion, ct);

    /// <summary>Crea una invitación (<c>POST /api/invitaciones</c>, solo admin); el token solo se devuelve aquí.</summary>
    /// <param name="peticion">Miembro existente al que vincular, o null para un nuevo adulto.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<InvitacionCreada> CrearInvitacionAsync(CrearInvitacionRequest peticion, CancellationToken ct = default) =>
        _nucleo.EnviarAsync<InvitacionCreada>(HttpMethod.Post, "api/invitaciones", peticion, ct);

    /// <summary>Historial de cambios del hogar, del más reciente al más antiguo (<c>GET /api/auditoria</c>, solo admin).</summary>
    /// <param name="entidad">Tipo de entidad a filtrar, o null para todas.</param>
    /// <param name="limite">Eventos por página (1-200).</param>
    /// <param name="hasta">Instante del último evento recibido, para pedir la página siguiente.</param>
    /// <param name="despuesDeId">Id del último evento recibido; desempata los que comparten <paramref name="hasta"/>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public Task<List<EventoAuditoriaDto>> ListarAuditoriaAsync(
        string? entidad = null, int limite = 50, DateTimeOffset? hasta = null, Guid? despuesDeId = null, CancellationToken ct = default)
    {
        var consulta = new List<string> { $"limite={limite}" };
        if (!string.IsNullOrEmpty(entidad)) consulta.Add($"entidad={Uri.EscapeDataString(entidad)}");
        if (hasta is { } h) consulta.Add($"hasta={Uri.EscapeDataString(h.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");
        if (despuesDeId is { } d) consulta.Add($"despuesDeId={d}");
        return _nucleo.ObtenerAsync<List<EventoAuditoriaDto>>($"api/auditoria?{string.Join('&', consulta)}", ct);
    }
}
