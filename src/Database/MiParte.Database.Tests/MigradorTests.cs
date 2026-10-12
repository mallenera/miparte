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
            Ejecutar($"create database {_nombre}", _servidor.ConnectionString);
            Conexion = new NpgsqlConnectionStringBuilder(Cadena!) { Database = _nombre, Pooling = false }.ConnectionString;
        }

        public string Conexion { get; }

        public void Ejecutar(string sql, string? conexion = null)
        {
            using var c = new NpgsqlConnection(conexion ?? Conexion);
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

        public void Dispose() => Ejecutar($"drop database if exists {_nombre} with (force)", _servidor.ConnectionString);
    }

    private static string Auth() => Migrador.Leer("previos/auth_stub.sql");

    [SkippableFact]
    public void Migrar_CreaLaEstructuraYRepetirEsInocuo()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        var migrador = new Migrador(bd.Conexion, TextWriter.Null);

        migrador.Migrar(conAuthStub: true);
        migrador.Migrar(conAuthStub: true);

        Assert.Equal(1, bd.Contar("select count(*) from pg_class where oid = 'public.gasto'::regclass"));
        Assert.True(bd.Contar("select count(*) from pg_proc where proname = 'crear_hogar'") >= 1);
        Assert.Empty(migrador.Estado().Pendientes);
    }

    [SkippableFact]
    public void Migrar_ConvergeUnaBaseQueSeHaQuedadoAtras()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        var migrador = new Migrador(bd.Conexion, TextWriter.Null);
        migrador.Migrar(conAuthStub: true);

        // Estado antiguo: falta una columna y una restricción con nombre.
        bd.Ejecutar("alter table public.categoria drop column a_cargo_cuenta_comun");
        bd.Ejecutar("alter table public.categoria drop constraint categoria_nombre_longitud");
        migrador.Migrar(conAuthStub: true);

        Assert.Equal(1, bd.Contar("select count(*) from information_schema.columns " +
                                  "where table_name = 'categoria' and column_name = 'a_cargo_cuenta_comun'"));
        Assert.Equal(1, bd.Contar("select count(*) from pg_constraint where conname = 'categoria_nombre_longitud'"));
    }

    [SkippableFact]
    public void ScriptGenerado_EsIdempotenteYEquivaleAMigrar()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        bd.Ejecutar(Auth());
        var sql = Migrador.GenerarScriptCompleto();

        bd.Ejecutar(sql);
        bd.Ejecutar(sql);

        Assert.Equal(1, bd.Contar("select count(*) from pg_class where oid = 'public.gasto'::regclass"));
        // Lo que dejó el script lo reconoce el migrador: no queda nada pendiente y migrar no falla.
        var migrador = new Migrador(bd.Conexion, TextWriter.Null);
        Assert.Empty(migrador.Estado().Pendientes);
        migrador.Migrar(conAuthStub: false);
    }

    [SkippableFact]
    public void ScriptsVersionados_SePuedenEjecutarDosVeces()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        using var bd = new BaseTemporal();
        new Migrador(bd.Conexion, TextWriter.Null).Migrar(conAuthStub: true);

        // Contrato de Scripts/ y Scripts/Antes: reejecutar un script ya aplicado no debe fallar ni duplicar nada.
        foreach (var nombre in Migrador.NombresDeScripts())
            bd.Ejecutar(Migrador.Leer(nombre));
    }
}
