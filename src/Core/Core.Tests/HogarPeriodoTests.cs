using Microsoft.EntityFrameworkCore;
using Npgsql;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Tests;

/// <summary>
/// Periodo propio del hogar: la propiedad derivada de las dos columnas y, con PostgreSQL real (MIPARTE_TEST_DB),
/// la restricción hogar_periodo_check y la lectura por EF.
/// </summary>
public class HogarPeriodoTests
{
    private static readonly string? Cadena = Environment.GetEnvironmentVariable("MIPARTE_TEST_DB");

    [Fact]
    public void HogarSinPeriodo_PeriodoPropioEsNulo_YElConstructorSigueFuncionando()
    {
        var hogar = new Hogar { Id = Guid.NewGuid(), Nombre = "Casa" };

        Assert.Null(hogar.PeriodoPropio);
        Assert.Null(hogar.PeriodoDesde);
        Assert.Null(hogar.PeriodoHasta);
        Assert.Equal(TipoGrupo.Hogar, hogar.Tipo);
    }

    [Fact]
    public void PeriodoConRango_SeReflejaEnLasDosColumnas()
    {
        var hogar = new Hogar { Nombre = "Viaje" };

        hogar.PeriodoPropio = Periodo.Rango(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 15));

        Assert.Equal(new DateOnly(2026, 7, 1), hogar.PeriodoDesde);
        Assert.Equal(new DateOnly(2026, 7, 15), hogar.PeriodoHasta);
        Assert.Equal(TipoPeriodo.Rango, hogar.PeriodoPropio!.Tipo);
        Assert.Equal("2026-07-01/2026-07-15", hogar.PeriodoPropio.ATexto());
    }

    [Fact]
    public void PeriodoAbierto_GuardaSoloElInicio_YVuelveComoAbierto()
    {
        var hogar = new Hogar { Nombre = "Evento" };

        hogar.PeriodoPropio = Periodo.Abierto(new DateOnly(2026, 9, 3));

        Assert.Equal(new DateOnly(2026, 9, 3), hogar.PeriodoDesde);
        Assert.Null(hogar.PeriodoHasta);
        Assert.Equal(TipoPeriodo.Abierto, hogar.PeriodoPropio!.Tipo);
        Assert.True(hogar.PeriodoPropio.Contiene(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void PeriodoDeMes_SeGuardaComoSuRango_YVuelveComoRango()
    {
        var hogar = new Hogar { Nombre = "Mes" };

        hogar.PeriodoPropio = Periodo.DeMes("2026-02");

        Assert.Equal(new DateOnly(2026, 2, 1), hogar.PeriodoDesde);
        Assert.Equal(new DateOnly(2026, 2, 28), hogar.PeriodoHasta);
        Assert.Equal(TipoPeriodo.Rango, hogar.PeriodoPropio!.Tipo);
        Assert.Equal("2026-02-01/2026-02-28", hogar.PeriodoPropio!.ATexto());
    }

    [Fact]
    public void AsignarNulo_LimpiaAmbasColumnas()
    {
        var hogar = new Hogar { Nombre = "Viaje" };
        hogar.PeriodoPropio = Periodo.Abierto(new DateOnly(2026, 9, 3));

        hogar.PeriodoPropio = null;

        Assert.Null(hogar.PeriodoDesde);
        Assert.Null(hogar.PeriodoHasta);
        Assert.Null(hogar.PeriodoPropio);
    }

    [SkippableFact]
    public async Task Restriccion_AceptaSinPeriodoRangoYAbierto_YRechazaLasCombinacionesInvalidas()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();

        // Sin periodo, con rango y abierto: válidos.
        await InsertarHogar(conn, null, null);
        await InsertarHogar(conn, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 15));
        await InsertarHogar(conn, new DateOnly(2026, 9, 3), null);

        // Hasta sin desde, y fin anterior al inicio: inválidos.
        await Assert.ThrowsAsync<PostgresException>(() => InsertarHogar(conn, null, new DateOnly(2026, 7, 15)));
        await Assert.ThrowsAsync<PostgresException>(() => InsertarHogar(conn, new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 1)));
    }

    [SkippableFact]
    public async Task EfLeePeriodoPropioDelHogar_SegunLasColumnas()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        var sinPeriodo = await InsertarHogar(conn, null, null);
        var conRango = await InsertarHogar(conn, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 15));
        var abierto = await InsertarHogar(conn, new DateOnly(2026, 9, 3), null);

        Assert.Null((await LeerHogar(sinPeriodo)).PeriodoPropio);
        Assert.Equal("2026-07-01/2026-07-15", (await LeerHogar(conRango)).PeriodoPropio!.ATexto());
        Assert.Equal("2026-09-03/", (await LeerHogar(abierto)).PeriodoPropio!.ATexto());
    }

    /// <summary>Inserta un hogar con su periodo por SQL y devuelve su id.</summary>
    private static async Task<Guid> InsertarHogar(NpgsqlConnection conn, DateOnly? desde, DateOnly? hasta)
    {
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into hogar (id, nombre, periodo_desde, periodo_hasta) values (@id, 'Periodo', @desde, @hasta)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("desde", (object?)desde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hasta", (object?)hasta ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Lee el hogar por EF con el rol de conexión (se salta RLS, como el servicio); solo protege el filtro por hogar.</summary>
    private static async Task<Hogar> LeerHogar(Guid id)
    {
        var options = new DbContextOptionsBuilder<MiParteDbContext>()
            .UseNpgsql(Cadena!)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var ctx = new MiParteDbContext(options, new HogarActual { HogarId = id });
        return await ctx.Hogares.AsNoTracking().SingleAsync();
    }
}
