namespace MiParte.Web.Hogares;

/// <summary>Aviso breve («toast») que se muestra unos segundos en la parte inferior de la pantalla.</summary>
public sealed class ServicioAvisos : IDisposable
{
    private CancellationTokenSource? _cierre;

    /// <summary>Texto del aviso visible, o null si no hay.</summary>
    public string? Texto { get; private set; }

    /// <summary>Se dispara al mostrar u ocultar el aviso.</summary>
    public event Action? Cambiado;

    /// <summary>Muestra un aviso durante unos segundos; uno nuevo sustituye al anterior.</summary>
    /// <param name="texto">Mensaje.</param>
    /// <param name="duracion">Duración; por defecto 3 segundos.</param>
    public async void Mostrar(string texto, TimeSpan? duracion = null)
    {
        _cierre?.Cancel();
        _cierre = new CancellationTokenSource();
        var token = _cierre.Token;
        Texto = texto;
        Cambiado?.Invoke();
        try
        {
            await Task.Delay(duracion ?? TimeSpan.FromSeconds(3), token);
            Texto = null;
            Cambiado?.Invoke();
        }
        catch (TaskCanceledException)
        {
            // Lo ha sustituido otro aviso.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cierre?.Cancel();
}
