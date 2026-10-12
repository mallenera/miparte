using System.Text;
using MiParte.Database;

const string Ayuda = """
    MiParte.Database: migraciones del esquema (PostgreSQL / Supabase)

    Uso: dotnet run --project src/Database/MiParte.Database -- <comando> [opciones]

    Comandos
      migrar             Aplica los scripts pendientes. Repetirlo es seguro.
      pendientes         Lista aplicados y pendientes sin tocar el esquema (sale con 1 si hay pendientes).
      script [fichero]   Genera un SQL idempotente con todos los scripts (a stdout o a un fichero).
      marcar-aplicadas   Registra todos los scripts como aplicados sin ejecutarlos (adoptar una base ya migrada).
      semilla-demo       Ejecuta la semilla de la cuenta demo (la restablece).

    Opciones
      --conexion <cadena>   Cadena Npgsql. Por defecto, la variable ConnectionStrings__Default.
      --con-auth-stub       Con «migrar»: crea antes el esquema auth simulado (solo PostgreSQL sin Supabase).
    """;

var comando = args.FirstOrDefault();
if (comando is null or "-h" or "--help" or "ayuda")
{
    Console.WriteLine(Ayuda);
    return comando is null ? 2 : 0;
}

string? Opcion(string nombre)
{
    var i = Array.IndexOf(args, nombre);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// «script» no necesita base de datos: el SQL sale solo de los recursos incrustados.
if (comando == "script")
{
    var sql = Migrador.GenerarScriptCompleto();
    var destino = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
    if (destino is null)
        Console.Write(sql);
    else
    {
        File.WriteAllText(destino, sql, new UTF8Encoding(false));
        Console.WriteLine($"Escrito {destino} ({Migrador.NombresDeScripts().Count} scripts)");
    }
    return 0;
}

var conexion = Opcion("--conexion") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");
if (string.IsNullOrWhiteSpace(conexion))
{
    Console.Error.WriteLine("Falta la cadena de conexión: usa --conexion o la variable ConnectionStrings__Default.");
    return 2;
}

var migrador = new Migrador(conexion, Console.Out);
try
{
    switch (comando)
    {
        case "migrar":
            var n = migrador.Migrar(args.Contains("--con-auth-stub"));
            Console.WriteLine(n == 0 ? "Nada que aplicar: la base está al día." : $"Aplicados {n} scripts.");
            return 0;
        case "pendientes":
            var (aplicados, pendientes) = migrador.Estado();
            Console.WriteLine($"Aplicados: {aplicados.Count}");
            foreach (var p in pendientes)
                Console.WriteLine($"  pendiente  {p}");
            Console.WriteLine($"Pendientes: {pendientes.Count}");
            return pendientes.Count == 0 ? 0 : 1;
        case "marcar-aplicadas":
            migrador.MarcarAplicadas();
            Console.WriteLine($"Marcados como aplicados {Migrador.NombresDeScripts().Count} scripts.");
            return 0;
        case "semilla-demo":
            migrador.SembrarDemo();
            return 0;
        default:
            Console.Error.WriteLine($"Comando desconocido: {comando}\n\n{Ayuda}");
            return 2;
    }
}
catch (Exception ex) when (ex is InvalidOperationException or Npgsql.NpgsqlException or FileNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
