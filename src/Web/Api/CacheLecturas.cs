using MiParte.Web.Autenticacion;
using MiParte.Web.Hogares;

namespace MiParte.Web.Api;

/// <summary>
/// Caché en memoria de las lecturas de Core.Api que casi no cambian (miembros, categorías, perfiles y cierres de mes) y
/// que cada pestaña pedía de nuevo. Es singleton: dura lo que la aplicación. Guarda la tarea en curso, así que dos
/// pantallas que piden lo mismo a la vez comparten una sola petición, y descarta lo que lleva más de
/// <see cref="Vigencia"/> o lo que cambia el propio usuario (<see cref="Invalidar"/>). Separa los datos por hogar y los
/// olvida al cambiar de hogar o de usuario.
/// </summary>
public sealed class CacheLecturas
{
    /// <summary>Tiempo máximo que se reutiliza una lectura: lo que otra persona del hogar cambie desde otro dispositivo aparece pasado este plazo.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(60);

    private readonly EstadoHogar _hogar;
    private readonly ServicioSesion _sesion;
    private readonly TimeProvider _reloj;
    private readonly Dictionary<string, Entrada> _entradas = [];
    private Guid? _hogarVigente;
    private string? _usuarioVigente;

    /// <summary>Lectura guardada: la tarea (en curso o resuelta) y cuándo se pidió.</summary>
    private sealed record Entrada(object Tarea, DateTimeOffset Pedida);

    /// <summary>Crea la caché y la engancha al cambio de hogar y de usuario.</summary>
    /// <param name="hogar">Estado del hogar seleccionado.</param>
    /// <param name="sesion">Sesión: al cambiar de usuario se olvida todo (p. ej. <c>EsYo</c> y los permisos de los miembros dependen de quien pregunta).</param>
    /// <param name="reloj">Reloj para la vigencia.</param>
    public CacheLecturas(EstadoHogar hogar, ServicioSesion sesion, TimeProvider reloj)
    {
        _hogar = hogar;
        _sesion = sesion;
        _reloj = reloj;
        _hogarVigente = hogar.HogarActual?.Id;
        _usuarioVigente = sesion.SesionActual?.UserId;
        hogar.Cambiado += AlCambiarHogar;
        sesion.SesionCambiada += AlCambiarSesion;
    }

    /// <summary>
    /// Devuelve la lectura de <paramref name="clave"/>, pidiéndola con <paramref name="cargar"/> solo si no hay una vigente.
    /// Cada llamante recibe su propia copia de la lista, para que pueda ordenarla o filtrarla sin afectar a los demás.
    /// </summary>
    /// <typeparam name="T">Tipo de los elementos de la lista.</typeparam>
    /// <param name="clave">Identifica la lectura (ruta de la API).</param>
    /// <param name="cargar">Hace la petición real.</param>
    /// <param name="ct">Cancela la espera de este llamante, no la petición compartida.</param>
    public async Task<List<T>> ListaAsync<T>(string clave, Func<Task<List<T>>> cargar, CancellationToken ct)
    {
        var llave = $"{_hogar.HogarActual?.Id}|{clave}";
        Task<List<T>> tarea;
        lock (_entradas)
        {
            if (_entradas.TryGetValue(llave, out var e) && _reloj.GetUtcNow() - e.Pedida < Vigencia && e.Tarea is Task<List<T>> existente)
            {
                tarea = existente;
            }
            else
            {
                tarea = cargar();
                _entradas[llave] = new Entrada(tarea, _reloj.GetUtcNow());
            }
        }

        try
        {
            return [.. await tarea.WaitAsync(ct)];
        }
        catch when (tarea.IsFaulted || tarea.IsCanceled)
        {
            // Un fallo no se recuerda: el siguiente intento vuelve a pedir.
            lock (_entradas)
            {
                if (_entradas.TryGetValue(llave, out var e) && ReferenceEquals(e.Tarea, tarea)) _entradas.Remove(llave);
            }

            throw;
        }
    }

    /// <summary>Olvida las lecturas cuya clave empieza por alguno de los prefijos; la siguiente pantalla que las pida las recargará.</summary>
    /// <param name="prefijos">Comienzos de ruta, p. ej. <c>api/categorias</c>.</param>
    public void Invalidar(params string[] prefijos)
    {
        lock (_entradas)
        {
            foreach (var llave in _entradas.Keys.ToList())
                if (prefijos.Any(p => llave[(llave.IndexOf('|') + 1)..].StartsWith(p, StringComparison.Ordinal))) _entradas.Remove(llave);
        }
    }

    /// <summary>Olvida todo.</summary>
    public void Limpiar()
    {
        lock (_entradas) _entradas.Clear();
    }

    private void AlCambiarSesion()
    {
        // También se dispara al renovar el token: con el mismo usuario no hay nada que olvidar. Al cerrar sesión (o descartarla
        // tras un 401) el usuario pasa a ser nadie, y la caché del anterior no debe servirse a quien entre después.
        var actual = _sesion.SesionActual?.UserId;
        if (actual == _usuarioVigente) return;
        _usuarioVigente = actual;
        Limpiar();
    }

    private void AlCambiarHogar()
    {
        // También se dispara al activar la cuenta común o el ahorro: ahí el hogar es el mismo y no hay nada que olvidar.
        var actual = _hogar.HogarActual?.Id;
        if (actual == _hogarVigente) return;
        _hogarVigente = actual;
        Limpiar();
    }
}
