using System.Reflection;
using System.Text;
using DbUp;
using DbUp.Engine;
using Npgsql;

namespace MiParte.Database;

/// <summary>
/// Converge una base PostgreSQL con el esquema del proyecto. <c>Estructura/</c> es el estado deseado y se aplica
/// siempre de forma idempotente; <c>Scripts/Antes</c> y <c>Scripts</c> son pasos versionados que corren una sola vez
/// (con journal) antes y después de la estructura, para lo que no se puede inferir: borrar, renombrar, rellenar datos.
/// </summary>
public sealed class Migrador
{
    /// <summary>Esquema propio del journal: fuera de <c>public</c> para que PostgREST no lo exponga.</summary>
    public const string EsquemaJournal = "miparte_migraciones";

    /// <summary>Tabla del journal: un registro por script aplicado.</summary>
    public const string TablaJournal = "scripts_aplicados";

    private const string PrefijoAntes = "antes/";
    private const string PrefijoDespues = "scripts/";
    private const string PrefijoEstructura = "estructura/";
    private const string PrefijoPrevios = "previos/";
    private const string PrefijoSemillas = "semillas/";

    // Etiqueta de dólar propia: ningún script del repositorio la usa, así que no cierra el literal por error.
    private const string EtiquetaDolar = "$miparte_script$";

    private const string RestaurarSearchPath = "select pg_catalog.set_config('search_path', '\"$user\", public', false);";

    private static readonly Assembly Ensamblado = typeof(Migrador).Assembly;

    private readonly string _conexion;
    private readonly TextWriter _salida;

    /// <summary>Crea el migrador contra una base PostgreSQL.</summary>
    /// <param name="conexion">Cadena de conexión Npgsql.</param>
    /// <param name="salida">Destino de los mensajes de progreso.</param>
    public Migrador(string conexion, TextWriter salida)
    {
        _conexion = conexion;
        _salida = salida;
    }

    /// <summary>
    /// Nombres de los scripts versionados (con journal) en el orden en que se aplican: primero los de
    /// <c>Scripts/Antes</c>, luego los de <c>Scripts</c>.
    /// </summary>
    public static IReadOnlyList<string> NombresDeScripts() =>
        Nombres(PrefijoAntes).Concat(Nombres(PrefijoDespues)).ToList();

    /// <summary>Nombres de los ficheros de <c>Estructura/</c> en orden de aplicación.</summary>
    public static IReadOnlyList<string> NombresDeEstructura() => Nombres(PrefijoEstructura).ToList();

    /// <summary>
    /// Converge la base: scripts «antes» pendientes, estructura completa y scripts pendientes. Es seguro repetirlo.
    /// </summary>
    /// <param name="conAuthStub">Crea antes el esquema <c>auth</c> simulado (solo PostgreSQL sin Supabase).</param>
    /// <returns>Cuántos scripts versionados se han aplicado (la estructura se aplica siempre).</returns>
    public int Migrar(bool conAuthStub)
    {
        if (conAuthStub && !ExisteEsquema("auth"))
            EjecutarSuelto(PrefijoPrevios + "auth_stub.sql");
        PrepararJournal();
        var n = AplicarScripts(PrefijoAntes);
        AplicarEstructura();
        return n + AplicarScripts(PrefijoDespues);
    }

    /// <summary>Compara el journal con los scripts versionados sin tocar el esquema.</summary>
    /// <returns>Los nombres ya aplicados y los pendientes.</returns>
    public (IReadOnlyList<string> Aplicados, IReadOnlyList<string> Pendientes) Estado()
    {
        var aplicados = ExisteJournal()
            ? Motor(PrefijoAntes).GetExecutedScripts().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var todos = NombresDeScripts();
        return (todos.Where(aplicados.Contains).ToList(), todos.Where(n => !aplicados.Contains(n)).ToList());
    }

    /// <summary>
    /// Registra como aplicados los scripts versionados sin ejecutarlos. Sirve para dar de alta una base que ya
    /// incluye su efecto (p. ej. restaurada de otra copia).
    /// </summary>
    public void MarcarAplicadas()
    {
        PrepararJournal();
        Motor(PrefijoAntes).MarkAsExecuted();
        Motor(PrefijoDespues).MarkAsExecuted();
    }

    /// <summary>Ejecuta la semilla de la cuenta demo (idempotente: vuelve a restablecerla).</summary>
    public void SembrarDemo() => EjecutarSuelto(PrefijoSemillas + "demo.sql");

    /// <summary>
    /// Genera un único SQL idempotente equivalente a <see cref="Migrar"/>: scripts «antes», estructura y scripts,
    /// cada script versionado protegido por el journal. Se puede pegar en el editor SQL de Supabase o pasar a psql
    /// sin este proyecto y ejecutarlo cuantas veces haga falta.
    /// </summary>
    public static string GenerarScriptCompleto()
    {
        var sb = new StringBuilder();
        var tabla = $"{EsquemaJournal}.{TablaJournal}";
        sb.AppendLine("-- Generado por MiParte.Database (comando «script»). Idempotente: converge la base con Estructura/ y");
        sb.AppendLine("-- aplica cada script versionado solo si no consta en el journal.");
        sb.AppendLine($"create schema if not exists {EsquemaJournal};");
        sb.AppendLine($"create table if not exists {tabla} (");
        sb.AppendLine("    schemaversionsid serial primary key,");
        sb.AppendLine("    scriptname varchar(255) not null,");
        sb.AppendLine("    applied timestamp not null);");
        sb.AppendLine();

        foreach (var nombre in Nombres(PrefijoAntes))
            AnadirGuardado(sb, nombre, tabla);

        sb.AppendLine("-- ===== Estructura =====");
        foreach (var nombre in Nombres(PrefijoEstructura))
        {
            sb.AppendLine($"-- {nombre}");
            sb.AppendLine(TransformadorEstructura.HacerIdempotente(Leer(nombre)).TrimEnd());
            sb.AppendLine();
        }
        sb.AppendLine(RestaurarSearchPath);
        sb.AppendLine();

        foreach (var nombre in Nombres(PrefijoDespues))
            AnadirGuardado(sb, nombre, tabla);
        return sb.ToString();
    }

    /// <summary>Lee el SQL de un script o fichero incrustado por su nombre.</summary>
    public static string Leer(string recurso)
    {
        using var flujo = Ensamblado.GetManifestResourceStream(Real(recurso))
                          ?? throw new FileNotFoundException($"Recurso incrustado no encontrado: {recurso}");
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        return lector.ReadToEnd();
    }

    private static void AnadirGuardado(StringBuilder sb, string nombre, string tabla)
    {
        var contenido = Leer(nombre);
        if (contenido.Contains(EtiquetaDolar, StringComparison.Ordinal))
            throw new InvalidOperationException($"El script «{nombre}» usa la etiqueta reservada {EtiquetaDolar}.");
        var literal = nombre.Replace("'", "''");
        sb.AppendLine($"-- {nombre}");
        sb.AppendLine("do $migracion$ begin");
        sb.AppendLine($"  if not exists (select 1 from {tabla} where scriptname = '{literal}') then");
        sb.AppendLine($"    execute {EtiquetaDolar}");
        sb.AppendLine(contenido.TrimEnd());
        sb.AppendLine($"    {EtiquetaDolar};");
        sb.AppendLine($"    insert into {tabla} (scriptname, applied) values ('{literal}', now());");
        sb.AppendLine("  end if;");
        sb.AppendLine("end $migracion$;");
        sb.AppendLine();
    }

    // Los nombres de recurso pueden traer «\» según el sistema donde se compiló: se normalizan a «/».
    private static string Normal(string nombre) => nombre.Replace('\\', '/');

    private static string Real(string nombre) =>
        Ensamblado.GetManifestResourceNames().FirstOrDefault(n => Normal(n) == nombre) ?? nombre;

    private static IEnumerable<string> Nombres(string prefijo) =>
        Ensamblado.GetManifestResourceNames()
            .Select(Normal)
            .Where(n => n.StartsWith(prefijo, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private UpgradeEngine Motor(string prefijo) =>
        DeployChanges.To.PostgresqlDatabase(_conexion)
            .WithScriptsEmbeddedInAssembly(Ensamblado, n => Normal(n).StartsWith(prefijo, StringComparison.Ordinal))
            .JournalToPostgresqlTable(EsquemaJournal, TablaJournal)
            .WithTransactionPerScript()
            .LogTo(new RegistroSalida(_salida))
            .Build();

    private int AplicarScripts(string prefijo)
    {
        if (!Nombres(prefijo).Any())
            return 0;
        var resultado = Motor(prefijo).PerformUpgrade();
        if (!resultado.Successful)
            throw new InvalidOperationException(
                $"Fallo en el script «{resultado.ErrorScript?.Name}»: {resultado.Error?.Message}", resultado.Error);
        return resultado.Scripts.Count();
    }

    // Toda la estructura en una transacción y en su propia conexión: el preámbulo del volcado fija search_path = ''
    // a nivel de sesión y no debe filtrarse a los scripts que corren después.
    private void AplicarEstructura()
    {
        using var conexion = new NpgsqlConnection(_conexion);
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        foreach (var nombre in Nombres(PrefijoEstructura))
        {
            try
            {
                using var cmd = new NpgsqlCommand(TransformadorEstructura.HacerIdempotente(Leer(nombre)), conexion, tx);
                cmd.ExecuteNonQuery();
            }
            catch (PostgresException ex)
            {
                throw new InvalidOperationException($"Fallo en «{nombre}»: {ex.MessageText}", ex);
            }
        }
        tx.Commit();
        _salida.WriteLine($"Estructura aplicada ({Nombres(PrefijoEstructura).Count()} ficheros).");
    }

    private void PrepararJournal()
    {
        // DbUp crea la tabla del journal pero no su esquema.
        using var conexion = new NpgsqlConnection(_conexion);
        conexion.Open();
        using var cmd = new NpgsqlCommand($"create schema if not exists {EsquemaJournal}", conexion);
        cmd.ExecuteNonQuery();
    }

    private bool ExisteJournal()
    {
        using var conexion = new NpgsqlConnection(_conexion);
        conexion.Open();
        using var cmd = new NpgsqlCommand("select to_regclass(@t) is not null", conexion);
        cmd.Parameters.AddWithValue("t", $"{EsquemaJournal}.{TablaJournal}");
        return (bool)cmd.ExecuteScalar()!;
    }

    private bool ExisteEsquema(string nombre)
    {
        using var conexion = new NpgsqlConnection(_conexion);
        conexion.Open();
        using var cmd = new NpgsqlCommand("select exists (select 1 from pg_namespace where nspname = @n)", conexion);
        cmd.Parameters.AddWithValue("n", nombre);
        return (bool)cmd.ExecuteScalar()!;
    }

    private void EjecutarSuelto(string recurso)
    {
        using var conexion = new NpgsqlConnection(_conexion);
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        using var cmd = new NpgsqlCommand(Leer(recurso), conexion, tx);
        cmd.ExecuteNonQuery();
        tx.Commit();
        _salida.WriteLine($"Ejecutado {recurso}");
    }
}
