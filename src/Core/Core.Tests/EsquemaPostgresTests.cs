using Microsoft.EntityFrameworkCore;
using Npgsql;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Tests;

/// <summary>
/// Comprueba el mapeo EF contra el esquema real de supabase/migrations.
/// Solo se ejecuta si MIPARTE_TEST_DB apunta a una base con la migración aplicada
/// (se usa un rol que se salta RLS, como haría el servicio: solo protege el filtro de EF).
/// </summary>
public class EsquemaPostgresTests
{
    private static readonly string? Cadena = Environment.GetEnvironmentVariable("MIPARTE_TEST_DB");

    private static MiParteDbContext Crear(Guid? hogar)
    {
        var options = new DbContextOptionsBuilder<MiParteDbContext>()
            .UseNpgsql(Cadena!)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MiParteDbContext(options, new HogarActual { HogarId = hogar });
    }

    [SkippableFact]
    public async Task GuardaYLeeGastoConRepartos_AislandoPorHogar()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        var hogarA = Guid.NewGuid();
        var hogarB = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var perfil = Guid.NewGuid();
        var perfilComun = Guid.NewGuid();
        var categoria = Guid.NewGuid();

        // Datos base por SQL (hogar y miembros no se crean desde EF en esta fase).
        await using (var conn = new NpgsqlConnection(Cadena))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                insert into hogar (id, nombre) values (@ha, 'A'), (@hb, 'B');
                insert into miembro (id, hogar_id, nombre, tipo) values
                    (@ana, @ha, 'Ana', 'adulto'), (@beto, @ha, 'Beto', 'adulto'), (@otro, @hb, 'Otro', 'adulto');
                """;
            cmd.Parameters.AddWithValue("ha", hogarA);
            cmd.Parameters.AddWithValue("hb", hogarB);
            cmd.Parameters.AddWithValue("ana", ana);
            cmd.Parameters.AddWithValue("beto", beto);
            cmd.Parameters.AddWithValue("otro", otro);
            await cmd.ExecuteNonQueryAsync();
        }

        var gastoId = Guid.NewGuid();
        var gastoComunId = Guid.NewGuid();
        await using (var ctx = Crear(hogarA))
        {
            ctx.PerfilesReparto.Add(new PerfilReparto
            {
                Id = perfil, HogarId = hogarA, Nombre = "Partes", Modo = ModoReparto.Partes,
            });
            ctx.PerfilesReparto.Add(new PerfilReparto
            {
                Id = perfilComun, HogarId = hogarA, Nombre = "Cuenta común", Modo = ModoReparto.CuentaComun,
            });
            ctx.Categorias.Add(new Categoria { Id = categoria, HogarId = hogarA, Nombre = "Casa" });
            await ctx.SaveChangesAsync();

            ctx.Gastos.Add(new Gasto
            {
                Id = gastoId, HogarId = hogarA, Fecha = new DateOnly(2026, 10, 1), Importe = 900m,
                CategoriaId = categoria, PagadoPor = ana, PerfilRepartoId = perfil, Concepto = "Hipoteca",
                Repartos =
                [
                    new GastoReparto { MiembroId = ana, HogarId = hogarA, ImporteAsumido = 600m },
                    new GastoReparto { MiembroId = beto, HogarId = hogarA, ImporteAsumido = 300m },
                ],
            });
            // Gasto a cargo de la cuenta común: sin filas en gasto_reparto.
            ctx.Gastos.Add(new Gasto
            {
                Id = gastoComunId, HogarId = hogarA, Fecha = new DateOnly(2026, 10, 2), Importe = 120m,
                CategoriaId = categoria, PagadoPor = ana, PerfilRepartoId = perfilComun, Concepto = "Luz",
                ACargoCuentaComun = true,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = Crear(hogarA))
        {
            var gasto = await ctx.Gastos.Include(g => g.Repartos).SingleAsync(g => g.Id == gastoId);
            Assert.Equal(900m, gasto.Importe);
            Assert.Equal(900m, gasto.Repartos.Sum(r => r.ImporteAsumido));

            var comun = await ctx.Gastos.Include(g => g.Repartos).SingleAsync(g => g.Id == gastoComunId);
            Assert.True(comun.ACargoCuentaComun);
            Assert.False(comun.EsPersonal);
            Assert.Empty(comun.Repartos);
            var p = await ctx.PerfilesReparto.SingleAsync(x => x.Id == perfilComun);
            Assert.Equal(ModoReparto.CuentaComun, p.Modo);
        }

        await using (var ctx = Crear(hogarB))
        {
            Assert.Empty(await ctx.Gastos.ToListAsync());
            Assert.Empty(await ctx.Categorias.ToListAsync());
            Assert.Equal("Otro", (await ctx.Miembros.SingleAsync()).Nombre);
        }
    }

    [SkippableFact]
    public async Task CrearHogarYMiembro_UsaLaFechaDeLaBaseDeDatos()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        var hogarId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(Cadena))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "insert into auth.users (id) values (@u)";
            cmd.Parameters.AddWithValue("u", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var ctx = Crear(null))
        {
            ctx.Hogares.Add(new Hogar { Id = hogarId, Nombre = "Casa" });
            ctx.Miembros.Add(new Miembro { Id = Guid.NewGuid(), HogarId = hogarId, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = userId });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = Crear(hogarId))
        {
            var h = await ctx.Hogares.SingleAsync();
            Assert.True(h.CreadoEn > DateTimeOffset.UtcNow.AddMinutes(-5), $"CreadoEn = {h.CreadoEn}");
            Assert.Equal(userId, (await ctx.Miembros.SingleAsync()).UserId);
        }
    }

    // --- Migración 20261005: roles, invitaciones, seed y RLS de miembro ---------------

    private static async Task<NpgsqlConnection> AbrirAsync()
    {
        var conn = new NpgsqlConnection(Cadena);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<Guid> CrearUsuarioAsync(NpgsqlConnection conn)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand("insert into auth.users (id) values (@u)", conn);
        cmd.Parameters.AddWithValue("u", id);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    // Ejecuta SQL simulando al usuario (claim sub de la sesión, como hace auth_stub.sql).
    private static async Task<object?> EscalarComoAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid usuario, string sql, params (string, object)[] ps)
    {
        await using (var set = new NpgsqlCommand("select set_config('request.jwt.claim.sub', @u, true)", conn, tx))
        {
            set.Parameters.AddWithValue("u", usuario.ToString());
            await set.ExecuteNonQueryAsync();
        }
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteScalarAsync();
    }

    [SkippableFact]
    public async Task CrearHogar_DejaAdminYSembraPerfilesYCategorias()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();

        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user,
            "select public.crear_hogar('Casa seed', 'Ana')"))!;

        Assert.Equal("admin", await EscalarComoAsync(conn, tx, user,
            "select rol from public.miembro where hogar_id = @h", ("h", hogar)));
        Assert.Equal(4L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.perfil_reparto where hogar_id = @h", ("h", hogar)));
        Assert.Equal(6L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.categoria where hogar_id = @h and perfil_reparto_id is not null", ("h", hogar)));
        Assert.Equal(1L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.perfil_reparto where hogar_id = @h and nombre = 'Individual' and modo = 'individual'", ("h", hogar)));
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task RolesDeCliente_NoPuedenEscribirNiLlamarARpcDeAlta()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var tablas = new[]
        {
            "hogar", "miembro", "perfil_reparto", "perfil_reparto_detalle", "categoria", "gasto_recurrente", "gasto",
            "gasto_reparto", "invitacion_hogar", "pago_liquidacion", "aportacion_cuenta", "reembolso_cuenta",
            "retirada_ahorro", "deposito_ahorro",
        };
        foreach (var tabla in tablas)
            foreach (var privilegio in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
                Assert.False(
                    (bool)(await EscalarAsync(conn, $"select has_table_privilege('authenticated', 'public.{tabla}', '{privilegio}')"))!,
                    $"authenticated no debe tener {privilegio} sobre {tabla}");

        // La lectura (acotada por RLS) sí se mantiene para authenticated y se cierra del todo para anon.
        Assert.True((bool)(await EscalarAsync(conn, "select has_table_privilege('authenticated', 'public.gasto', 'SELECT')"))!);
        Assert.False((bool)(await EscalarAsync(conn, "select has_table_privilege('anon', 'public.gasto', 'SELECT')"))!);

        Assert.False((bool)(await EscalarAsync(conn, "select has_function_privilege('authenticated', 'public.crear_hogar(text, text)', 'EXECUTE')"))!);
        Assert.False((bool)(await EscalarAsync(conn, "select has_function_privilege('authenticated', 'public.aceptar_invitacion(text, text)', 'EXECUTE')"))!);
        // Las políticas RLS de lectura las siguen necesitando.
        Assert.True((bool)(await EscalarAsync(conn, "select has_function_privilege('authenticated', 'public.mis_hogares()', 'EXECUTE')"))!);
    }

    [SkippableFact]
    public async Task Ahorro_NoPuedeSuperarLaAportacionNiSerNegativo()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();
        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user, "select public.crear_hogar('Casa ahorro', 'Ana')"))!;
        var miembro = (Guid)(await EscalarComoAsync(conn, tx, user, "select id from public.miembro where hogar_id = @h", ("h", hogar)))!;

        const string insertar =
            "insert into public.aportacion_cuenta (hogar_id, miembro_id, desde, importe, ahorro) values (@h, @m, '2026-01-01', 100, @a) returning 1";
        Assert.Equal(1, await EscalarComoAsync(conn, tx, user, insertar, ("h", hogar), ("m", miembro), ("a", 100m)));

        foreach (var ahorro in new[] { 100.01m, -1m })
        {
            await tx.SaveAsync("antes");
            var ex = await Assert.ThrowsAsync<PostgresException>(() => EscalarComoAsync(
                conn, tx, user, insertar.Replace("2026-01-01", "2026-02-01"), ("h", hogar), ("m", miembro), ("a", ahorro)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            await tx.RollbackAsync("antes");
        }

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Permisos_SiguenLaPlantillaPorDefecto_YElCheckAdmiteElCatalogoEntero()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();
        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user, "select public.crear_hogar('Casa permisos', 'Ana')"))!;
        var miembro = (Guid)(await EscalarComoAsync(conn, tx, user, "select id from public.miembro where hogar_id = @h", ("h", hogar)))!;

        // Sin lista propia el miembro sigue la plantilla de su rol (nulo), y el check de la base admite exactamente las claves del catálogo.
        Assert.True(await EscalarComoAsync(conn, tx, user, "select permisos from public.miembro where id = @m", ("m", miembro)) is null or DBNull);
        const string actualizarCada = "update public.miembro set permisos = array[@p] where id = @m returning 1";
        foreach (var permiso in MiParte.Core.Domain.CatalogoPermisos.Todos)
            Assert.Equal(1, await EscalarComoAsync(conn, tx, user, actualizarCada, ("p", permiso.Clave), ("m", miembro)));

        const string actualizar = "update public.miembro set permisos = array[@p] where id = @m returning 1";
        Assert.Equal(1, await EscalarComoAsync(conn, tx, user, actualizar, ("p", MiParte.Core.Domain.CatalogoPermisos.HistorialVer), ("m", miembro)));
        await tx.SaveAsync("antes");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => EscalarComoAsync(conn, tx, user, actualizar, ("p", "inventado"), ("m", miembro)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        await tx.RollbackAsync("antes");

        await tx.RollbackAsync();
    }

    private static async Task<object?> EscalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    [SkippableFact]
    public async Task AceptarInvitacion_VinculaMiembro_YNoSePuedeReutilizar()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var admin = await CrearUsuarioAsync(conn);
        var invitado = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();

        var hogar = (Guid)(await EscalarComoAsync(conn, tx, admin, "select public.crear_hogar('Casa inv', 'Ana')"))!;
        var beto = Guid.NewGuid();
        await EscalarComoAsync(conn, tx, admin,
            "insert into public.miembro (id, hogar_id, nombre, tipo) values (@m, @h, 'Beto', 'adulto') returning id",
            ("m", beto), ("h", hogar));

        const string token = "token-de-prueba-123";
        await EscalarComoAsync(conn, tx, admin,
            "insert into public.invitacion_hogar (hogar_id, miembro_id, token_hash, caduca_en) " +
            "values (@h, @m, encode(sha256(convert_to(@t, 'UTF8')), 'hex'), now() + interval '1 day') returning id",
            ("h", hogar), ("m", beto), ("t", token));

        var devuelto = (Guid)(await EscalarComoAsync(conn, tx, invitado,
            "select public.aceptar_invitacion(@t, null)", ("t", token)))!;
        Assert.Equal(hogar, devuelto);
        Assert.Equal(invitado, await EscalarComoAsync(conn, tx, invitado,
            "select user_id from public.miembro where id = @m", ("m", beto)));

        await Assert.ThrowsAsync<PostgresException>(() => EscalarComoAsync(conn, tx, invitado,
            "select public.aceptar_invitacion(@t, null)", ("t", token)));
        await tx.RollbackAsync();
    }
}
