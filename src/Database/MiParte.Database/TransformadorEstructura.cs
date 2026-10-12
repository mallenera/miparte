using System.Text;
using System.Text.RegularExpressions;

namespace MiParte.Database;

/// <summary>
/// Convierte los ficheros de <c>Estructura/</c> (estado deseado, formato de pg_dump) en SQL que converge sobre una
/// base ya existente: crea lo que falta, reemplaza funciones y políticas y añade columnas y restricciones ausentes.
/// Los ficheros no se tocan; la transformación se hace al aplicarlos. No infiere borrados, renombrados ni cambios de
/// tipo: eso va en <c>Scripts/Antes</c> o <c>Scripts</c>.
/// </summary>
public static class TransformadorEstructura
{
    private const string Etiqueta = "$estructura$";

    private static readonly Regex CreateTable = new(
        @"^CREATE TABLE (?!IF NOT EXISTS)(public\.(\w+)) \(\n(.*?)\n\);",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AddConstraint = new(
        @"^ALTER TABLE (?:ONLY )?(public\.\w+)\n\s+ADD CONSTRAINT (\w+)[^;]*;",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex Politica = new(
        @"^CREATE POLICY (\w+) ON (public\.\w+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex Indice = new(
        @"^CREATE (UNIQUE )?INDEX (?!IF NOT EXISTS)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Devuelve el SQL idempotente equivalente al de un fichero de estructura.</summary>
    public static string HacerIdempotente(string sql)
    {
        var s = sql.Replace("\r\n", "\n");
        s = AddConstraint.Replace(s, m => Guardada(m.Groups[1].Value, m.Groups[2].Value, m.Value));
        s = CreateTable.Replace(s, m => Tabla(m.Groups[1].Value, m.Groups[3].Value));
        s = Politica.Replace(s, "DROP POLICY IF EXISTS $1 ON $2;\nCREATE POLICY $1 ON $2");
        s = Indice.Replace(s, "CREATE $1INDEX IF NOT EXISTS ");
        s = Regex.Replace(s, @"^CREATE FUNCTION ", "CREATE OR REPLACE FUNCTION ", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^CREATE PROCEDURE ", "CREATE OR REPLACE PROCEDURE ", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^CREATE VIEW ", "CREATE OR REPLACE VIEW ", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^CREATE TRIGGER ", "CREATE OR REPLACE TRIGGER ", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^CREATE SCHEMA (?!IF NOT EXISTS)", "CREATE SCHEMA IF NOT EXISTS ", RegexOptions.Multiline);
        return s;
    }

    // La tabla se crea si no existe y, si existe, se le añaden las columnas y restricciones con nombre que le falten.
    private static string Tabla(string tabla, string cuerpo)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS ").Append(tabla).Append(" (\n").Append(cuerpo).Append("\n);");
        foreach (var linea in cuerpo.Split('\n'))
        {
            var d = linea.Trim().TrimEnd(',');
            if (d.Length == 0)
                continue;
            if (d.StartsWith("CONSTRAINT ", StringComparison.Ordinal))
            {
                var nombre = d.Split(' ', 3)[1];
                sb.Append('\n').Append(Guardada(tabla, nombre, $"ALTER TABLE {tabla} ADD {d};"));
            }
            else if (!EsRestriccionSinNombre(d))
            {
                sb.Append($"\n  ALTER TABLE {tabla} ADD COLUMN IF NOT EXISTS {d};");
            }
        }
        return sb.ToString();
    }

    private static bool EsRestriccionSinNombre(string definicion) =>
        new[] { "PRIMARY KEY", "UNIQUE", "FOREIGN KEY", "CHECK", "EXCLUDE", "LIKE" }
            .Any(p => definicion.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    // Las restricciones no admiten IF NOT EXISTS: se comprueba por nombre en el catálogo.
    private static string Guardada(string tabla, string nombre, string sentencia) =>
        $"DO {Etiqueta} BEGIN\n  IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint WHERE conname = '{nombre}' " +
        $"AND conrelid = '{tabla}'::regclass) THEN\n  {sentencia.Replace("\n", "\n  ")}\n  END IF;\nEND {Etiqueta};";
}
