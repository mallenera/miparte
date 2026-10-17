using System.Net;
using MiParte.Contracts;
using MiParte.Web.Api;

namespace MiParte.Web.Modelos;

/// <summary>Situación de un código abierto desde <c>/unirse</c>: enlace de grupo, invitación nominal o error.</summary>
public enum EstadoUnirseEnlace
{
    /// <summary>Todavía no se ha preguntado a la API qué es el código.</summary>
    Comprobando,

    /// <summary>Es un enlace de grupo activo: falta el nombre para entrar como miembro nuevo.</summary>
    PideNombre,

    /// <summary>No es un enlace de grupo: puede ser una invitación nominal, que sigue su propio flujo.</summary>
    EsInvitacion,

    /// <summary>El enlace se ha revocado y ya no admite uniones.</summary>
    Revocado,

    /// <summary>El enlace ha pasado su fecha de caducidad.</summary>
    Caducado,

    /// <summary>La persona ya pertenece a este hogar.</summary>
    YaEsMiembro,

    /// <summary>La API ha limitado las peticiones; se puede reintentar tras una espera.</summary>
    Limite,

    /// <summary>Fallo de conexión o de la API; el mensaje dice qué hacer.</summary>
    Error,

    /// <summary>Unión hecha: el hogar ya es de la persona.</summary>
    Unido,
}

/// <summary>
/// Estado de un enlace de grupo al abrir <c>/unirse</c> y al unirse. Comprueba el código sin efectos: sin nombre la API
/// no une a nadie; responde 404 si el código no es de un grupo (entonces puede ser una invitación) y 400 si es válido y pide nombre.
/// No depende de Blazor; la página solo enlaza su estado y pinta. El código nunca se muestra ni aparece en los mensajes.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
public sealed class ModeloUnirseEnlace
{
    private readonly CoreApiClient _api;
    private string? _token;
    private string? _nombre;

    /// <summary>Crea el modelo.</summary>
    /// <param name="api">Cliente de Core.Api.</param>
    public ModeloUnirseEnlace(CoreApiClient api) => _api = api;

    /// <summary>Estado actual del enlace.</summary>
    public EstadoUnirseEnlace Estado { get; private set; } = EstadoUnirseEnlace.Comprobando;

    /// <summary>Mensaje para la persona cuando el estado es un error o una situación que bloquea la unión; null si no hay nada que decir.</summary>
    public string? Mensaje { get; private set; }

    /// <summary>Hogar al que se ha unido la persona (solo con <see cref="EstadoUnirseEnlace.Unido"/>).</summary>
    public HogarResumen? Hogar { get; private set; }

    /// <summary>Indica si tiene sentido ofrecer «Reintentar» (límite de peticiones o fallo transitorio).</summary>
    public bool PuedeReintentar { get; private set; }

    /// <summary>Pregunta a la API qué es el código, sin unir a nadie todavía.</summary>
    /// <param name="token">Código del enlace, tal como viene en el fragmento.</param>
    public Task ComprobarAsync(string token)
    {
        _token = token;
        _nombre = null;
        return EjecutarAsync(new UnirseEnlaceGrupoRequest(token), comprobando: true);
    }

    /// <summary>Une a la persona al grupo como miembro nuevo con el nombre indicado.</summary>
    /// <param name="token">Código del enlace.</param>
    /// <param name="nombre">Nombre en el grupo.</param>
    public Task UnirseAsync(string token, string nombre)
    {
        _token = token;
        _nombre = nombre;
        return EjecutarAsync(new UnirseEnlaceGrupoRequest(token, nombre), comprobando: false);
    }

    /// <summary>Repite la última operación (comprobación o unión). Sin operación previa no hace nada.</summary>
    public Task ReintentarAsync() =>
        _token is null ? Task.CompletedTask
        : _nombre is null ? ComprobarAsync(_token)
        : UnirseAsync(_token, _nombre);

    private async Task EjecutarAsync(UnirseEnlaceGrupoRequest peticion, bool comprobando)
    {
        Mensaje = null;
        PuedeReintentar = false;
        try
        {
            Hogar = await _api.UnirseEnlaceGrupoAsync(peticion);
            Estado = EstadoUnirseEnlace.Unido;
        }
        catch (ApiException e)
        {
            // Al comprobar, el código sin unión dice qué es: 404 = no es de un grupo; 400 = válido y falta el nombre.
            if (comprobando && e.Codigo == HttpStatusCode.NotFound) { Estado = EstadoUnirseEnlace.EsInvitacion; return; }
            if (comprobando && e.Codigo == HttpStatusCode.BadRequest) { Estado = EstadoUnirseEnlace.PideNombre; return; }

            var (estado, mensaje, reintentable) = Clasificar(e);
            Estado = estado;
            Mensaje = mensaje;
            PuedeReintentar = reintentable;
        }
    }

    /// <summary>
    /// Traduce un error de Core.Api a un estado y a un mensaje accionable en español. Nunca devuelve el texto técnico de la API
    /// (puede traer rutas o configuración): los casos conocidos tienen su frase y el resto, una genérica.
    /// </summary>
    /// <param name="e">Error de la API.</param>
    public static (EstadoUnirseEnlace Estado, string Mensaje, bool Reintentable) Clasificar(ApiException e)
    {
        switch (e.Codigo)
        {
            case HttpStatusCode.Conflict when e.Message.Contains("revocado", StringComparison.OrdinalIgnoreCase):
                return (EstadoUnirseEnlace.Revocado,
                    "Este enlace ya no funciona porque lo han desactivado. Pide a quien organiza el grupo un enlace nuevo.", false);
            case HttpStatusCode.Conflict when e.Message.Contains("caducado", StringComparison.OrdinalIgnoreCase):
                return (EstadoUnirseEnlace.Caducado,
                    "Este enlace ha caducado. Pide a quien organiza el grupo un enlace nuevo.", false);
            case HttpStatusCode.Conflict when e.Message.Contains("Ya perteneces", StringComparison.OrdinalIgnoreCase):
                return (EstadoUnirseEnlace.YaEsMiembro, "Ya formas parte de este hogar.", false);
            case HttpStatusCode.NotFound:
                return (EstadoUnirseEnlace.Error,
                    "Este enlace no es válido. Revisa que lo hayas copiado entero o pide a quien organiza el grupo uno nuevo.", false);
            case HttpStatusCode.TooManyRequests:
                return (EstadoUnirseEnlace.Limite,
                    "Has hecho demasiados intentos seguidos. Espera un minuto y pulsa «Reintentar».", true);
            case HttpStatusCode.ServiceUnavailable:
                return (EstadoUnirseEnlace.Error,
                    "No se pudo conectar con el servidor. Comprueba tu conexión y pulsa «Reintentar».", true);
            default:
                return (EstadoUnirseEnlace.Error,
                    "No se ha podido unir al grupo. Inténtalo de nuevo más tarde.", true);
        }
    }
}
