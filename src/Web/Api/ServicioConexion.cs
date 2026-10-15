namespace MiParte.Web.Api;

/// <summary>
/// Estado de la conexión con Core.Api: indica si hay peticiones reintentándose porque el servidor no responde
/// (reinicio por un despliegue o arranque en frío). Lo escribe <see cref="ManejadorReintentos"/> y lo muestra <c>AvisoConexion</c>.
/// </summary>
public sealed class ServicioConexion
{
    private int _reintentando;
    private int _porLimite;

    /// <summary>Hay al menos una petición esperando para reintentarse.</summary>
    public bool Reintentando => _reintentando > 0;

    /// <summary>Alguna de las peticiones que esperan lo hace porque la API pidió espaciar las peticiones (429), no porque no responda.</summary>
    public bool PorLimite => _porLimite > 0;

    /// <summary>Se dispara al empezar o terminar de reintentar.</summary>
    public event Action? Cambiado;

    /// <summary>Una petición empieza a reintentarse.</summary>
    /// <param name="porLimite">Si espera porque la API respondió 429 (demasiadas peticiones) y no porque esté caída.</param>
    public void Empezar(bool porLimite = false)
    {
        if (porLimite) Interlocked.Increment(ref _porLimite);
        if (Interlocked.Increment(ref _reintentando) == 1 || porLimite) Cambiado?.Invoke();
    }

    /// <summary>Una petición deja de reintentarse (por éxito, por agotar los intentos o por cancelación).</summary>
    /// <param name="porLimite">El mismo valor con el que se llamó a <see cref="Empezar"/>.</param>
    public void Terminar(bool porLimite = false)
    {
        if (porLimite) Interlocked.Decrement(ref _porLimite);
        if (Interlocked.Decrement(ref _reintentando) == 0 || porLimite) Cambiado?.Invoke();
    }
}
