namespace MiParte.Web.Api;

/// <summary>
/// Estado de la conexión con Core.Api: indica si hay peticiones reintentándose porque el servidor no responde
/// (reinicio por un despliegue o arranque en frío). Lo escribe <see cref="ManejadorReintentos"/> y lo muestra <c>AvisoConexion</c>.
/// </summary>
public sealed class ServicioConexion
{
    private int _reintentando;

    /// <summary>Hay al menos una petición esperando para reintentarse.</summary>
    public bool Reintentando => _reintentando > 0;

    /// <summary>Se dispara al empezar o terminar de reintentar.</summary>
    public event Action? Cambiado;

    /// <summary>Una petición empieza a reintentarse.</summary>
    public void Empezar()
    {
        if (Interlocked.Increment(ref _reintentando) == 1) Cambiado?.Invoke();
    }

    /// <summary>Una petición deja de reintentarse (por éxito, por agotar los intentos o por cancelación).</summary>
    public void Terminar()
    {
        if (Interlocked.Decrement(ref _reintentando) == 0) Cambiado?.Invoke();
    }
}
