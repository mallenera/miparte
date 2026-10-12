using System.Reflection;
using System.Text;
using DbUp;
using DbUp.Engine;
using Npgsql;

namespace MiParte.Database;

/// <summary>
/// Aplica, lista y exporta los scripts SQL del esquema. Los scripts viajan incrustados en el ensamblado:
/// <c>migraciones/</c> es la línea base histórica (supabase/migrations) y <c>scripts/</c> los cambios nuevos.
/// </summary>
public sealed class Migrador
{
    /// <summary>Esquema propio del journal: fuera de <c>public</c> para que PostgREST no lo exponga.</summary>
    public const string EsquemaJournal = "miparte_migraciones";

    /// <summary>Tabla del journal: un registro por script aplicado.</summary>
    public const string TablaJournal = "scripts_aplicados";

    private const string PrefijoMigraciones = "migraciones/";
    private const string PrefijoScripts = "scripts/";
    private const string PrefijoPrevios = "previos/";
    private const string PrefijoSemillas = "semillas/";

    // Etiqueta de dólar propia: ningún script del repositorio la usa, así que no cierra el literal por error.
    private const string EtiquetaDolar = "$miparte_script$";

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

    /// <summary>Nombres de los scripts del esquema (línea base y nuevos) en el orden en que se aplican.</summary>
    public static IReadOnlyList<string> NombresDeScripts() =>
        Ensamblado.GetManifestResourceNames()
            .Where(EsScriptDeEsquema)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Aplica los scripts pendientes, cada uno en su transacción. Es seguro repetirlo: los ya aplicados se omiten.
    /// </summary>
    /// <param name="conAuthStub">Ejecuta antes el esquema <c>auth</c> simulado (solo para PostgreSQL sin Supabase).</param>
    /// <returns>Cuántos scripts se han aplicado.</returns>
    public int Migrar(bool conAuthStub)
    {
        if (conAuthStub && !ExisteJournal())
            EjecutarSuelto(PrefijoPrevios + "auth_stub.sql");
        PrepararJournal();
        var resultado = Motor().PerformUpgrade();
        if (!resultado.Successful)
            throw new InvalidOperationException(
                $"Fallo en el script «{resultado.ErrorScript?.Name}»: {resultado.Error?.Message}", resultado.Error);
        return resultado.Scripts.Count();
    }

    /// <summary>Compara el journal con los scripts incrustados sin tocar el esquema.</summary>
    /// <returns>Los nombres ya aplicados y los pendientes.</returns>
    public (IReadOnlyList<string> Aplicados, IReadOnlyList<string> Pendientes) Estado()
    {
        var aplicados = ExisteJournal()
            ? Motor().GetExecutedScripts().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var todos = NombresDeScripts();
        return (todos.Where(aplicados.Contains).ToList(), todos.Where(n => !aplicados.Contains(n)).ToList());
    }

    /// <summary>
    /// Registra como aplicados todos los scripts sin ejecutarlos. Sirve para adoptar una base que ya se migró
    /// por otra vía (CLI de Supabase, psql) y a partir de ahí migrar solo con este proyecto.
    /// </summary>
    public void MarcarAplicadas()
    {
        PrepararJournal();
        Motor().MarkAsExecuted();
    }

    /// <summary>Ejecuta la semilla de la cuenta demo (idempotente: vuelve a restablecerla).</summary>
    public void SembrarDemo() => EjecutarSuelto(PrefijoSemillas + "demo.sql");

    /// <summary>
    /// Genera un único SQL idempotente con todos los scripts: cada uno solo corre si no figura en el journal y deja
    /// su registro. Se puede pegar en el editor SQL de Supabase o pasar a psql sin este proyecto y ejecutarlo
    /// cuantas veces haga falta.
    /// </summary>
    public static string GenerarScriptCompleto()
    {
        var sb = new StringBuilder();
        var tabla = $"{EsquemaJournal}.{TablaJournal}";
        sb.AppendLine("-- Generado por MiParte.Database (comando «script»). Idempotente: cada script solo se aplica si no consta en el journal.");
        sb.AppendLine($"create schema if not exists {EsquemaJournal};");
        sb.AppendLine($"create table if not exists {tabla} (");
        sb.AppendLine("    schemaversionsid serial primary key,");
        sb.AppendLine("    scriptname varchar(255) not null,");
        sb.AppendLine("    applied timestamp not null);");
        sb.AppendLine();
        foreach (var nombre in NombresDeScripts())
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
        return sb.ToString();
    }

    private static bool EsScriptDeEsquema(string nombre) =>
        nombre.StartsWith(PrefijoMigraciones, StringComparison.Ordinal)
        || nombre.StartsWith(PrefijoScripts, StringComparison.Ordinal);

    private UpgradeEngine Motor() =>
        DeployChanges.To.PostgresqlDatabase(_conexion)
            .WithScriptsEmbeddedInAssembly(Ensamblado, EsScriptDeEsquema)
            .JournalToPostgresqlTable(EsquemaJournal, TablaJournal)
            .WithTransactionPerScript()
            .LogTo(new RegistroSalida(_salida))
            .Build();

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

    /// <summary>Lee el SQL de un script incrustado por su nombre (tal como lo devuelve <see cref="NombresDeScripts"/>).</summary>
    public static string Leer(string recurso)
    {
        using var flujo = Ensamblado.GetManifestResourceStream(recurso)
                          ?? throw new FileNotFoundException($"Recurso incrustado no encontrado: {recurso}");
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        return lector.ReadToEnd();
    }
}
