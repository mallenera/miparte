using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Unión por enlace de grupo contra PostgreSQL real: el uso del enlace, el alta del miembro y la auditoría van en la misma
/// transacción. Se saltan sin MIPARTE_TEST_DB; en CI siempre se ejecutan.
/// </summary>
public class EnlacesGrupoPostgresTests
{
    private static MiParteDbContext Crear(Guid hogar)
    {
        var options = new DbContextOptionsBuilder<MiParteDbContext>()
            .UseNpgsql(Cadena!)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MiParteDbContext(options, new HogarActual { HogarId = hogar });
    }

    /// <summary>Crea el hogar de Ana con la API y devuelve su id y el id de su miembro de administradora.</summary>
    private static async Task<(Guid Hogar, Guid MiembroAna)> HogarDeAnaAsync(Entorno e, Guid ana)
    {
        var hogar = await CrearHogar(e.Cliente(ana), "Casa enlaces", "Ana");
        var miembro = (Guid)(await EscalarAsync("select id from public.miembro where hogar_id = @h and user_id = @u",
            ("h", hogar.Id), ("u", ana)))!;
        return (hogar.Id, miembro);
    }

    /// <summary>Inserta un enlace activo y sin caducidad; la copia en memoria que devuelve es la que se pasa a <c>UsarAsync</c>.</summary>
    private static async Task<EnlaceGrupo> CrearEnlaceAsync(Guid hogar, Guid miembroCreador)
    {
        var enlace = new EnlaceGrupo
        {
            // El hash debe ser hex de 64 caracteres (check de la tabla); creado antes para que se pueda caducar después.
            Id = Guid.NewGuid(), HogarId = hogar, TokenHash = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant(),
            CreadoPor = miembroCreador, CreadoEn = DateTimeOffset.UtcNow.AddHours(-2), Activo = true, Usos = 0,
        };
        await using var db = Crear(hogar);
        db.EnlacesGrupo.Add(enlace);
        await db.SaveChangesAsync();
        return enlace;
    }

    /// <summary>Usa el enlace como lo hace el servicio: con un miembro nuevo para <paramref name="userId"/>.</summary>
    private static async Task<ResultadoUsoEnlace> UsarAsync(EnlaceGrupo enlace, Guid userId, string nombre)
    {
        await using var db = Crear(enlace.HogarId);
        var repositorio = new RepositorioEnlacesGrupo(db);
        var nuevo = new Miembro
        {
            Id = Guid.NewGuid(), HogarId = enlace.HogarId, Nombre = nombre, Tipo = TipoMiembro.Adulto,
            UserId = userId, Rol = RolMiembro.Miembro,
        };
        return await repositorio.UsarAsync(enlace, userId, nuevo, DateTimeOffset.UtcNow, default);
    }

    /// <summary>Lee un valor escalar de la base de datos de prueba (conexión de superusuario, sin filtros de hogar).</summary>
    private static async Task<object?> EscalarAsync(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>Ejecuta una sentencia de preparación de la prueba (desactivar o caducar un enlace ya creado).</summary>
    private static async Task EjecutarAsync(string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> UsosAsync(Guid enlace) => Convert.ToInt32(await EscalarAsync(
        "select usos from public.enlace_grupo where id = @e", ("e", enlace)));

    private static async Task<int> MiembrosDeAsync(Guid hogar, Guid userId) => Convert.ToInt32(await EscalarAsync(
        "select count(*) from public.miembro where hogar_id = @h and user_id = @u", ("h", hogar), ("u", userId)));

    private static async Task<int> AuditoriaUsarAsync(Guid enlace) => Convert.ToInt32(await EscalarAsync(
        "select count(*) from public.auditoria where entidad = 'enlace_grupo' and entidad_id = @e and accion = 'usar'", ("e", enlace)));

    [SkippableFact]
    public async Task UnionCorrecta_CuentaElUso_DaDeAltaAlMiembro_Y_Audita_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var ana = await e.CrearUsuarioAsync();
        var laura = await e.CrearUsuarioAsync();
        var (hogar, miembroAna) = await HogarDeAnaAsync(e, ana);
        var enlace = await CrearEnlaceAsync(hogar, miembroAna);

        var resultado = await UsarAsync(enlace, laura, "Laura");

        Assert.Equal(ResultadoUsoEnlace.Usado, resultado);
        Assert.Equal(1, await UsosAsync(enlace.Id));
        Assert.Equal(1, await MiembrosDeAsync(hogar, laura));
        Assert.Equal(1, await AuditoriaUsarAsync(enlace.Id));
    }

    [SkippableFact]
    public async Task EnlaceDesactivadoEnBd_PeseAQueLaCopiaEstaActiva_EsEnlaceNoDisponible_SinCambios_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var ana = await e.CrearUsuarioAsync();
        var laura = await e.CrearUsuarioAsync();
        var (hogar, miembroAna) = await HogarDeAnaAsync(e, ana);
        var enlace = await CrearEnlaceAsync(hogar, miembroAna);
        await EjecutarAsync("update public.enlace_grupo set activo = false where id = @e", ("e", enlace.Id));

        var resultado = await UsarAsync(enlace, laura, "Laura");

        Assert.Equal(ResultadoUsoEnlace.EnlaceNoDisponible, resultado);
        Assert.Equal(0, await UsosAsync(enlace.Id));
        Assert.Equal(0, await MiembrosDeAsync(hogar, laura));
        Assert.Equal(0, await AuditoriaUsarAsync(enlace.Id));
    }

    [SkippableFact]
    public async Task EnlaceCaducadoEnBd_PeseAQueLaCopiaNoCaduca_EsEnlaceNoDisponible_SinCambios_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var ana = await e.CrearUsuarioAsync();
        var laura = await e.CrearUsuarioAsync();
        var (hogar, miembroAna) = await HogarDeAnaAsync(e, ana);
        var enlace = await CrearEnlaceAsync(hogar, miembroAna);
        await EjecutarAsync("update public.enlace_grupo set caduca_en = now() - interval '1 minute' where id = @e", ("e", enlace.Id));

        var resultado = await UsarAsync(enlace, laura, "Laura");

        Assert.Equal(ResultadoUsoEnlace.EnlaceNoDisponible, resultado);
        Assert.Equal(0, await UsosAsync(enlace.Id));
        Assert.Equal(0, await MiembrosDeAsync(hogar, laura));
        Assert.Equal(0, await AuditoriaUsarAsync(enlace.Id));
    }

    [SkippableFact]
    public async Task UsuarioYaEnElHogar_DevuelveUsuarioYaEnHogar_YRevierteElUso_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var ana = await e.CrearUsuarioAsync();
        var (hogar, miembroAna) = await HogarDeAnaAsync(e, ana);
        var enlace = await CrearEnlaceAsync(hogar, miembroAna);

        // Ana ya figura en el hogar: el alta del miembro choca con miembro_hogar_id_user_id_key (23505) tras contar el uso.
        var resultado = await UsarAsync(enlace, ana, "Ana otra vez");

        Assert.Equal(ResultadoUsoEnlace.UsuarioYaEnHogar, resultado);
        Assert.Equal(0, await UsosAsync(enlace.Id));
        Assert.Equal(1, await MiembrosDeAsync(hogar, ana));
        Assert.Equal(0, await AuditoriaUsarAsync(enlace.Id));
    }
}
