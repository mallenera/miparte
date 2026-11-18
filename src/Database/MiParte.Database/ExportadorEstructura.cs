using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace MiParte.Database;

/// <summary>
/// Vuelca la estructura actual del esquema <c>public</c> (tablas, funciones, RLS, privilegios) a ficheros SQL
/// versionables, uno por objeto. Es una instantánea de lectura: el origen de verdad siguen siendo los scripts, y el diff de
/// estos ficheros en cada PR muestra qué cambia realmente en la base.
/// </summary>
public static class ExportadorEstructura
{
    /// <summary>Ruta por defecto de la instantánea, relativa a la raíz del repositorio.</summary>
    public const string RutaPorDefecto = "src/Database/MiParte.Database/Estructura";

    /// <summary>
    /// Roles a los que la estructura concede privilegios: los de Supabase (PostgREST) y el de Core.Api. Se les revoca
    /// todo antes de cada bloque de GRANT para que no sobreviva un privilegio que ya no está en la estructura.
    /// </summary>
    public const string RolesAplicacion = "anon, authenticated, miparte_api";

    /// <summary>
    /// Ejecuta <c>pg_dump --schema-only</c> contra la base y escribe el resultado normalizado.
    /// Requiere <c>pg_dump</c> en el PATH.
    /// </summary>
    /// <param name="conexion">Cadena de conexión Npgsql de una base ya migrada.</param>
    /// <param name="destino">Carpeta de salida (se regenera entera).</param>
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
        foreach (var a in new[] { "--schema-only", "--no-password", "--no-owner", "--schema=public", "--encoding=UTF8", "-h", cs.Host ?? "localhost",
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

        // Se regenera por completo: un objeto eliminado no debe dejar su fichero atrás.
        if (Directory.Exists(destino))
            Directory.Delete(destino, recursive: true);
        foreach (var (relativa, contenido) in Dividir(salida.Result))
        {
            var fichero = Path.Combine(destino, relativa);
            Directory.CreateDirectory(Path.GetDirectoryName(fichero)!);
            File.WriteAllText(fichero, contenido, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Reparte el volcado en un fichero por objeto, agrupando con cada tabla o función lo que depende de ella.
    /// El prefijo numérico de las carpetas es también el orden en que se pueden aplicar sobre una base vacía.
    /// </summary>
    /// <returns>Ruta relativa del fichero → contenido.</returns>
    internal static IReadOnlyDictionary<string, string> Dividir(string sql)
    {
        var lineas = Normalizar(sql).Split('\n');
        var ficheros = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
        var ruta = "00_Esquema.sql";

        for (var i = 0; i < lineas.Length; i++)
        {
            if (EsCabecera(lineas, i))
            {
                var cabecera = lineas[i + 1];
                // Cuerpo del bloque hasta la siguiente cabecera, para localizar el objeto al que pertenece.
                var fin = i + 3;
                while (fin < lineas.Length && !EsCabecera(lineas, fin))
                    fin++;
                var cuerpo = string.Join('\n', lineas[(i + 3)..fin]);
                ruta = Clasificar(cabecera, cuerpo);

                // pg_dump expresa los privilegios de tabla relativos a los de por defecto vigentes al crearla; al
                // revocar antes a los roles de la aplicación, los GRANT que siguen valen sobre cualquier base de partida.
                if (cabecera.Contains("Type: DEFAULT ACL;", StringComparison.Ordinal))
                {
                    // Igual que con las tablas: sin esto, los permisos por defecto previos de la base se sumarían.
                    if (!ficheros.TryGetValue(ruta, out var d))
                        ficheros[ruta] = d = new StringBuilder();
                    d.Append("--\n").Append(cabecera).Append("\n--\n\n")
                        .Append($"ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON TABLES FROM {RolesAplicacion};\n");
                    i += 2;
                    continue;
                }
                var tabla = Regex.Match(cuerpo, @"\bON TABLE public\.""?(\w+)""?");
                if (cabecera.Contains("Type: ACL;", StringComparison.Ordinal) && tabla.Success)
                {
                    if (!ficheros.TryGetValue(ruta, out var destino))
                        ficheros[ruta] = destino = new StringBuilder();
                    destino.Append("--\n").Append(cabecera).Append("\n--\n\n")
                        .Append($"REVOKE ALL ON TABLE public.{tabla.Groups[1].Value} FROM {RolesAplicacion};\n");
                    i += 2;
                    continue;
                }
            }
            if (lineas[i].StartsWith("-- PostgreSQL database dump complete", StringComparison.Ordinal))
                break;
            if (!ficheros.TryGetValue(ruta, out var sb))
                ficheros[ruta] = sb = new StringBuilder();
            sb.Append(lineas[i]).Append('\n');
        }
        return ficheros.ToDictionary(p => p.Key, p => p.Value.ToString().Trim() + "\n");
    }

    private static bool EsCabecera(string[] l, int i) =>
        i + 2 < l.Length && l[i] == "--" && l[i + 1].StartsWith("-- Name: ", StringComparison.Ordinal) && l[i + 2] == "--";

    private static string Clasificar(string cabecera, string cuerpo)
    {
        var tipo = Regex.Match(cabecera, @"Type: ([^;]+);").Groups[1].Value;
        var objeto = Regex.Match(cuerpo, @"(?:\bON(?: TABLE| FUNCTION)?|\bONLY|\bTABLE|\bFUNCTION)\s+public\.""?(\w+)""?")
            .Groups[1].Value;
        if (objeto.Length == 0)
            return "00_Esquema.sql";
        return tipo switch
        {
            "TABLE" or "CONSTRAINT" or "CHECK CONSTRAINT" or "INDEX" => $"01_Tablas/{objeto}.sql",
            "FUNCTION" => $"02_Funciones/{objeto}.sql",
            "ACL" when cuerpo.Contains(" ON FUNCTION ", StringComparison.Ordinal) => $"02_Funciones/{objeto}.sql",
            "TRIGGER" => $"03_Disparadores/{objeto}.sql",
            "FK CONSTRAINT" => $"04_ClavesForaneas/{objeto}.sql",
            "POLICY" or "ROW SECURITY" or "ACL" => $"05_Seguridad/{objeto}.sql",
            _ => "00_Esquema.sql",
        };
    }

    /// <summary>
    /// Quita lo que cambia en cada volcado sin que cambie el esquema (versión de pg_dump, fecha, tokens
    /// <c>\restrict</c>) y lo que solo existe en PostgreSQL recientes y rompería en versiones anteriores:
    /// <c>SET transaction_timeout</c> (17+) y el privilegio <c>MAINTAIN</c> (17+, que además aparece solo por
    /// los permisos por defecto de la base de origen).
    /// </summary>
    internal static string Normalizar(string sql)
    {
        var lineas = sql.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.StartsWith("\\restrict ", StringComparison.Ordinal)
                        && !l.StartsWith("\\unrestrict ", StringComparison.Ordinal)
                        && !l.StartsWith("-- Dumped from database version", StringComparison.Ordinal)
                        && !l.StartsWith("-- Dumped by pg_dump version", StringComparison.Ordinal)
                        && !l.StartsWith("SET transaction_timeout", StringComparison.Ordinal)
                        && !l.StartsWith("GRANT MAINTAIN ON ", StringComparison.Ordinal))
            .Select(l => l.Replace(",MAINTAIN", "", StringComparison.Ordinal));
        return string.Join('\n', lineas);
    }
}
