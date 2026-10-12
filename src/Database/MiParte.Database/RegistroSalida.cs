using DbUp.Engine.Output;

namespace MiParte.Database;

/// <summary>Reenvía el progreso de DbUp a un <see cref="TextWriter"/> (sin colores ni dependencias de consola).</summary>
internal sealed class RegistroSalida(TextWriter salida) : IUpgradeLog
{
    /// <inheritdoc />
    public void LogTrace(string format, params object[] args) { }

    /// <inheritdoc />
    public void LogDebug(string format, params object[] args) { }

    /// <inheritdoc />
    public void LogInformation(string format, params object[] args) => salida.WriteLine(string.Format(format, args));

    /// <inheritdoc />
    public void LogWarning(string format, params object[] args) => salida.WriteLine("AVISO " + string.Format(format, args));

    /// <inheritdoc />
    public void LogError(string format, params object[] args) => salida.WriteLine("ERROR " + string.Format(format, args));

    /// <inheritdoc />
    public void LogError(Exception ex, string format, params object[] args) =>
        salida.WriteLine("ERROR " + string.Format(format, args) + ": " + ex.Message);
}
