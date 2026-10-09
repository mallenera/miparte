using System.Net;
using System.Net.Http.Json;
using Npgsql;
using MiParte.Contracts;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Pruebas contra PostgreSQL real de la migración 20261021120000: el cierre de mes y las escrituras de gasto del mismo
/// hogar y mes se serializan con un bloqueo advisory, de modo que una petición que ya pasó la comprobación previa de
/// Core.Api no puede guardar un gasto en un mes que otra acaba de cerrar. Se saltan sin MIPARTE_TEST_DB.
/// El cierre concurrente se simula con una transacción abierta que ha insertado en mes_cerrado y aún no confirma:
/// la comprobación previa (otra conexión) no la ve, igual que en la carrera real.
/// </summary>
public class CierreMesConcurrenciaTests
{
    // Meses de 2020: siempre pasados, para poder cerrarlos sin depender de la fecha actual.
    private static readonly DateOnly Marzo = new(2020, 3, 1);
    private static readonly DateOnly Abril = new(2020, 4, 1);

    /// <summary>Tiempo que se espera para dar por bloqueada una petición que debería estar esperando el bloqueo.</summary>
    private static readonly TimeSpan EsperaBloqueo = TimeSpan.FromSeconds(1.5);

    private sealed record Escenario(Entorno Entorno, HttpClient Cliente, Guid Hogar, Guid Ana, Guid Categoria, Guid Perfil);

    /// <summary>Crea un hogar por la API con su única miembro (Ana) y devuelve lo necesario para registrar gastos.</summary>
    private static async Task<Escenario> Montar(Entorno e)
    {
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa concurrencia", "Ana");
        var c = e.Cliente(user, hogar.Id);
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single();
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).First(p => p.Modo == "individual");
        var categoria = (await Leer<List<CategoriaDto>>(await c.GetAsync("/api/categorias"))).First();
        return new Escenario(e, c, hogar.Id, ana.Id, categoria.Id, perfil.Id);
    }

    private static GastoRequest Pedido(Escenario s, DateOnly fecha)
        => new(fecha, 10m, s.Categoria, s.Ana, s.Perfil, "Compra");

    private static async Task<GastoResponse> CrearGasto(Escenario s, DateOnly fecha)
        => await Leer<GastoResponse>(await s.Cliente.PostAsJsonAsync("/api/gastos", Pedido(s, fecha)), HttpStatusCode.Created);

    private static async Task<int> ContarGastos(Guid hogar)
    {
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from public.gasto where hogar_id = @h", conn);
        cmd.Parameters.AddWithValue("h", hogar);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Abre una conexión con una transacción que cierra el mes (insert en mes_cerrado) sin confirmarla.</summary>
    private static async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> CerrarSinConfirmar(Guid hogar, DateOnly mes)
    {
        var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand("insert into public.mes_cerrado (hogar_id, mes) values (@h, @m)", conn, tx);
        cmd.Parameters.AddWithValue("h", hogar);
        cmd.Parameters.AddWithValue("m", mes);
        await cmd.ExecuteNonQueryAsync();
        return (conn, tx);
    }

    /// <summary>Comprueba que la tarea sigue esperando (bloqueada por el cierre sin confirmar), confirma el cierre y devuelve su resultado.</summary>
    private static async Task<T> BloqueadaHastaConfirmar<T>(Task<T> peticion, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var _ = conn;
        var terminada = await Task.WhenAny(peticion, Task.Delay(EsperaBloqueo));
        Assert.NotSame(peticion, terminada); // si ya hubiera terminado, no se estaría serializando con el cierre
        await tx.CommitAsync();
        return await peticion;
    }

    [SkippableFact]
    public async Task CrearGasto_ConCierreDelMesSinConfirmar_EsperaYDa409_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);

        var (conn, tx) = await CerrarSinConfirmar(s.Hogar, Marzo);
        var r = await BloqueadaHastaConfirmar(s.Cliente.PostAsJsonAsync("/api/gastos", Pedido(s, new DateOnly(2020, 3, 15))), conn, tx);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("cerrado", await r.Content.ReadAsStringAsync());
        Assert.Equal(0, await ContarGastos(s.Hogar));
    }

    [SkippableFact]
    public async Task EditarGasto_FechaNuevaEnMesQueSeCierraConcurrentemente_409_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);
        var g = await CrearGasto(s, new DateOnly(2020, 4, 10));

        var (conn, tx) = await CerrarSinConfirmar(s.Hogar, Marzo);
        var r = await BloqueadaHastaConfirmar(s.Cliente.PutAsJsonAsync($"/api/gastos/{g.Id}", Pedido(s, new DateOnly(2020, 3, 20))), conn, tx);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var actual = await Leer<List<GastoResponse>>(await s.Cliente.GetAsync("/api/gastos?mes=2020-04"));
        Assert.Equal(new DateOnly(2020, 4, 10), Assert.Single(actual).Fecha); // sigue en su mes original
    }

    [SkippableFact]
    public async Task EditarGasto_FechaAntiguaEnMesQueSeCierraConcurrentemente_409_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);
        var g = await CrearGasto(s, new DateOnly(2020, 3, 10));

        var (conn, tx) = await CerrarSinConfirmar(s.Hogar, Marzo);
        var r = await BloqueadaHastaConfirmar(s.Cliente.PutAsJsonAsync($"/api/gastos/{g.Id}", Pedido(s, new DateOnly(2020, 4, 20))), conn, tx);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); // no se puede sacar un gasto de un mes cerrado
        var actual = await Leer<List<GastoResponse>>(await s.Cliente.GetAsync("/api/gastos?mes=2020-03"));
        Assert.Equal(new DateOnly(2020, 3, 10), Assert.Single(actual).Fecha);
    }

    [SkippableFact]
    public async Task BorrarGasto_ConCierreDelMesSinConfirmar_409YSeConserva_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);
        var g = await CrearGasto(s, new DateOnly(2020, 3, 10));

        var (conn, tx) = await CerrarSinConfirmar(s.Hogar, Marzo);
        var r = await BloqueadaHastaConfirmar(s.Cliente.DeleteAsync($"/api/gastos/{g.Id}"), conn, tx);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, await ContarGastos(s.Hogar));
    }

    [SkippableFact]
    public async Task GenerarRecurrentes_ConCierreDelMesSinConfirmar_409YNoCreaGastos_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);
        await Leer<GastoRecurrenteResponse>(await s.Cliente.PostAsJsonAsync("/api/gastos-recurrentes",
            new GastoRecurrenteRequest(50m, s.Categoria, s.Ana, s.Perfil, 5, "Internet")), HttpStatusCode.Created);

        var (conn, tx) = await CerrarSinConfirmar(s.Hogar, Marzo);
        var r = await BloqueadaHastaConfirmar(s.Cliente.PostAsync("/api/gastos-recurrentes/generar?mes=2020-03", null), conn, tx);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, await ContarGastos(s.Hogar));
    }

    [SkippableFact]
    public async Task CerrarMes_EsperaAUnaEscrituraDeGastoEnCurso_YLaEscrituraSeConserva_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);

        // Una transacción de escritura de gasto en marzo, abierta y sin confirmar, retiene el bloqueo del mes.
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            "insert into public.gasto (hogar_id, fecha, importe, categoria_id, pagado_por, perfil_reparto_id) values (@h, @f, 10, @c, @a, @p)", conn, tx))
        {
            cmd.Parameters.AddWithValue("h", s.Hogar);
            cmd.Parameters.AddWithValue("f", new DateOnly(2020, 3, 15));
            cmd.Parameters.AddWithValue("c", s.Categoria);
            cmd.Parameters.AddWithValue("a", s.Ana);
            cmd.Parameters.AddWithValue("p", s.Perfil);
            await cmd.ExecuteNonQueryAsync();
        }

        var cierre = s.Cliente.PostAsJsonAsync("/api/cierres-mes", new CerrarMesRequest("2020-03"));
        Assert.NotSame(cierre, await Task.WhenAny(cierre, Task.Delay(EsperaBloqueo))); // el cierre espera a la escritura

        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Created, (await cierre).StatusCode);
        Assert.Equal(1, await ContarGastos(s.Hogar)); // el gasto confirmado antes del cierre queda en el mes cerrado

        // Con el mes ya cerrado, la base de datos rechaza cualquier escritura nueva aunque no pase por la comprobación de la API.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertarGastoDirecto(s, new DateOnly(2020, 3, 16)));
        Assert.Equal("MP409", ex.SqlState);
    }

    [SkippableFact]
    public async Task Triggers_RechazanInsertUpdateDeleteYRepartoEnMesCerrado_PeroNoEnOtroMes_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);
        var g = await CrearGasto(s, new DateOnly(2020, 3, 10));
        var otro = await CrearGasto(s, new DateOnly(2020, 4, 10));
        await EjecutarAsync("insert into public.mes_cerrado (hogar_id, mes) values (@h, @m)", ("h", s.Hogar), ("m", Marzo));

        async Task Rechazado(string sql, params (string, object)[] p)
            => Assert.Equal("MP409", (await Assert.ThrowsAsync<PostgresException>(() => EjecutarAsync(sql, p))).SqlState);

        await Rechazado("update public.gasto set importe = 20 where id = @g", ("g", g.Id));
        await Rechazado("update public.gasto set fecha = '2020-04-11' where id = @g", ("g", g.Id)); // sacarlo del mes cerrado
        await Rechazado("update public.gasto set fecha = '2020-03-11' where id = @g", ("g", otro.Id)); // meterlo en el mes cerrado
        await Rechazado("delete from public.gasto where id = @g", ("g", g.Id));
        // Cambiar solo el reparto no toca la fila de gasto: lo para el trigger de gasto_reparto.
        await Rechazado("update public.gasto_reparto set importe_asumido = importe_asumido where gasto_id = @g", ("g", g.Id));
        await Rechazado("delete from public.gasto_reparto where gasto_id = @g", ("g", g.Id));

        // Un mes abierto no se ve afectado.
        await EjecutarAsync("update public.gasto set importe = 20 where id = @g", ("g", otro.Id));

        // Reabrir (borrar el cierre) vuelve a permitir las escrituras.
        await EjecutarAsync("delete from public.mes_cerrado where hogar_id = @h and mes = @m", ("h", s.Hogar), ("m", Marzo));
        await EjecutarAsync("update public.gasto set importe = 20 where id = @g", ("g", g.Id));
    }

    [SkippableFact]
    public async Task BorrarHogar_ConMesesCerradosYGastos_NoLoBloqueaElCierre_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var s = await Montar(e);
        await CrearGasto(s, new DateOnly(2020, 3, 10));
        await EjecutarAsync("insert into public.mes_cerrado (hogar_id, mes) values (@h, @m)", ("h", s.Hogar), ("m", Marzo));

        await EjecutarAsync("delete from public.hogar where id = @h", ("h", s.Hogar)); // cascada: gastos con el mes cerrado

        Assert.Equal(0, await ContarGastos(s.Hogar));
    }

    private static Task InsertarGastoDirecto(Escenario s, DateOnly fecha)
        => EjecutarAsync(
            "insert into public.gasto (hogar_id, fecha, importe, categoria_id, pagado_por, perfil_reparto_id) values (@h, @f, 10, @c, @a, @p)",
            ("h", s.Hogar), ("f", fecha), ("c", s.Categoria), ("a", s.Ana), ("p", s.Perfil));

    /// <summary>Ejecuta una sentencia SQL con parámetros en su propia conexión (y transacción implícita).</summary>
    private static async Task EjecutarAsync(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }
}
