using Npgsql;
using Xunit;

namespace MiParte.Database.Tests;

/// <summary>
/// Comprueba el migrador contra PostgreSQL real. Cada prueba crea (y borra) su propia base a partir del servidor
/// de MIPARTE_TEST_DB, que necesita permiso para crear bases de datos.
/// </summary>
public class MigradorTests
{
    private static readonly string? Cadena = Environment.GetEnvironmentVariable("MIPARTE_TEST_DB");

    /// <summary>Base temporal que se elimina al terminar.</summary>
    private sealed class BaseTemporal : IDisposable
    {
        private readonly NpgsqlConnectionStringBuilder _servidor;
        private readonly string _nombre = "miparte_dbtest_" + Guid.NewGuid().ToString("N")[..12];

        public BaseTemporal()
        {
            _servidor = new NpgsqlConnectionStringBuilder(Cadena!) { Pooling = false };
            Ejecutar($"create database {_nombre}");
            Conexion = new NpgsqlConnectionStringBuilder(Cadena!) { Database = _nombre, Pooling = false }.ConnectionString;
        }

        public string Conexion { get; }

        public void Ejecutar(string sql, string? conexion = null)
        {
            using var c = new NpgsqlConnection(conexion ?? _servidor.ConnectionString);
            c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        public long Contar(string sql)
        {
            using var c = new NpgsqlConnection(Conexion);
            c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        public void Dispose() => Ejecutar($"drop database if exists {_nombre} with (force)");
    }

    private static string Auth() => Migrador.Leer("previos/auth_stub.sql");

    [SkippableFact]
    public void Migrar_AplicaTodoYRepetirEsInocuo()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        var migrador = new Migrador(bd.Conexion, TextWriter.Null);

        var primera = migrador.Migrar(conAuthStub: true);
        var segunda = migrador.Migrar(conAuthStub: true);

        Assert.Equal(Migrador.NombresDeScripts().Count, primera);
        Assert.Equal(0, segunda);
        var (aplicados, pendientes) = migrador.Estado();
        Assert.Equal(Migrador.NombresDeScripts().Count, aplicados.Count);
        Assert.Empty(pendientes);
    }

    [SkippableFact]
    public void ScriptGenerado_EsIdempotenteYDejaElJournal()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        bd.Ejecutar(Auth(), bd.Conexion);
        var sql = Migrador.GenerarScriptCompleto();

        bd.Ejecutar(sql, bd.Conexion);
        bd.Ejecutar(sql, bd.Conexion);

        Assert.Equal(Migrador.NombresDeScripts().Count,
            bd.Contar($"select count(*) from {Migrador.EsquemaJournal}.{Migrador.TablaJournal}"));
        // El journal que deja el script lo entiende el migrador: no queda nada pendiente.
        var (_, pendientes) = new Migrador(bd.Conexion, TextWriter.Null).Estado();
        Assert.Empty(pendientes);
    }

    [SkippableFact]
    public void MarcarAplicadas_AdoptaUnaBaseYaMigradaSinReejecutar()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        bd.Ejecutar(Auth(), bd.Conexion);
        foreach (var nombre in Migrador.NombresDeScripts())
            bd.Ejecutar(Migrador.Leer(nombre), bd.Conexion); // migrada «a mano», sin journal

        var migrador = new Migrador(bd.Conexion, TextWriter.Null);
        migrador.MarcarAplicadas();

        Assert.Equal(0, migrador.Migrar(conAuthStub: false));
    }

    [SkippableFact]
    public void ScriptsNuevos_SePuedenEjecutarDosVeces()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        new Migrador(bd.Conexion, TextWriter.Null).Migrar(conAuthStub: true);

        // Contrato de la carpeta Scripts/: reejecutar un script ya aplicado no debe fallar ni duplicar nada.
        foreach (var nombre in Migrador.NombresDeScripts().Where(n => n.StartsWith("scripts/", StringComparison.Ordinal)))
            bd.Ejecutar(Migrador.Leer(nombre), bd.Conexion);
    }
}
