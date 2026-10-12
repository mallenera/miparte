using System.Diagnostics;
using System.Text;
using Npgsql;

namespace MiParte.Database;

/// <summary>
/// Vuelca la estructura actual del esquema <c>public</c> (tablas, funciones, RLS, privilegios) a un único SQL
/// versionable. Es una instantánea de lectura: el origen de verdad siguen siendo los scripts, y el diff de este
/// fichero en cada PR muestra qué cambia realmente en la base.
/// </summary>
public static class ExportadorEstructura
{
    /// <summary>Ruta por defecto de la instantánea, relativa a la raíz del repositorio.</summary>
    public const string RutaPorDefecto = "src/Database/MiParte.Database/Estructura/esquema.sql";

    /// <summary>
    /// Ejecuta <c>pg_dump --schema-only</c> contra la base y escribe el resultado normalizado.
    /// Requiere <c>pg_dump</c> en el PATH.
    /// </summary>
    /// <param name="conexion">Cadena de conexión Npgsql de una base ya migrada.</param>
    /// <param name="destino">Fichero de salida.</param>
    public static void Exportar(string conexion, string destino)
    {
        var cs = new NpgsqlConnectionStringBuilder(conexion);
        var psi = new ProcessStartInfo("pg_dump")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        // Solo el esquema de la aplicación: sin el journal ni el auth simulado, sin propietarios (varían por entorno).
        foreach (var a in new[] { "--schema-only", "--no-owner", "--schema=public", "--encoding=UTF8", "-h", cs.Host ?? "localhost",
                                  "-p", cs.Port.ToString(), "-U", cs.Username ?? "postgres", "-d", cs.Database ?? "postgres" })
            psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(cs.Password))
            psi.Environment["PGPASSWORD"] = cs.Password;

        using var proceso = Process.Start(psi)
                            ?? throw new InvalidOperationException("No se pudo iniciar pg_dump.");
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        proceso.WaitForExit();
        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"pg_dump falló: {error.Result.Trim()}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destino))!);
        File.WriteAllText(destino, Normalizar(salida.Result), new UTF8Encoding(false));
    }

    /// <summary>
    /// Quita lo que cambia en cada volcado sin que cambie el esquema: la versión de pg_dump, la fecha y los
    /// tokens aleatorios <c>\restrict</c> de PostgreSQL recientes.
    /// </summary>
    internal static string Normalizar(string sql)
    {
        var lineas = sql.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.StartsWith("\\restrict ", StringComparison.Ordinal)
                        && !l.StartsWith("\\unrestrict ", StringComparison.Ordinal)
                        && !l.StartsWith("-- Dumped from database version", StringComparison.Ordinal)
                        && !l.StartsWith("-- Dumped by pg_dump version", StringComparison.Ordinal));
        return string.Join('\n', lineas);
    }
}
