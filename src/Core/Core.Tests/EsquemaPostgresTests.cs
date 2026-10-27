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
    public async Task PredeterminadoParaGastos_SeGuardaYSeLeeEnPerfilYCategoria()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        var hogar = Guid.NewGuid();
        var perfil = Guid.NewGuid();
        var categoria = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(Cadena))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "insert into hogar (id, nombre) values (@h, 'Predeterminados');";
            cmd.Parameters.AddWithValue("h", hogar);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var ctx = Crear(hogar))
        {
            ctx.PerfilesReparto.Add(new PerfilReparto
            {
                Id = perfil, HogarId = hogar, Nombre = "Casa", Modo = ModoReparto.Individual, PredeterminadoGastos = true,
            });
            ctx.Categorias.Add(new Categoria { Id = categoria, HogarId = hogar, Nombre = "Super", PredeterminadoGastos = true });
            await ctx.SaveChangesAsync();
        }

        await using var lectura = Crear(hogar);
        Assert.True((await lectura.PerfilesReparto.SingleAsync(p => p.Id == perfil)).PredeterminadoGastos);
        Assert.True((await lectura.Categorias.SingleAsync(c => c.Id == categoria)).PredeterminadoGastos);
    }

    [SkippableFact]
    public async Task PredeterminadoParaGastos_SoloUnoPorHogar_LoImpideElIndiceUnico()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        var hogar = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(Cadena))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "insert into hogar (id, nombre) values (@h, 'Un predeterminado');";
            cmd.Parameters.AddWithValue("h", hogar);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var ctx = Crear(hogar))
        {
            ctx.Categorias.Add(new Categoria { Id = Guid.NewGuid(), HogarId = hogar, Nombre = "Super", PredeterminadoGastos = true });
            await ctx.SaveChangesAsync();
        }

        await using var segundo = Crear(hogar);
        segundo.Categorias.Add(new Categoria { Id = Guid.NewGuid(), HogarId = hogar, Nombre = "Farmacia", PredeterminadoGastos = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => segundo.SaveChangesAsync());
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
        Assert.Equal(5L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.perfil_reparto where hogar_id = @h", ("h", hogar)));
        Assert.Equal(1L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.perfil_reparto where hogar_id = @h and modo = 'cuenta_comun_ahorro'", ("h", hogar)));
        Assert.Equal(6L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.categoria where hogar_id = @h and perfil_reparto_id is not null", ("h", hogar)));
        Assert.Equal(1L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from public.perfil_reparto where hogar_id = @h and nombre = 'Individual' and modo = 'individual'", ("h", hogar)));
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task HogarNuevo_TipoPorDefectoEsHogar_YLaBaseTieneLaRestriccionDeValores()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();

        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user,
            "select public.crear_hogar('Casa tipo', 'Ana')"))!;

        Assert.Equal("hogar", await EscalarComoAsync(conn, tx, user,
            "select tipo from public.hogar where id = @h", ("h", hogar)));
        Assert.Equal(1L, await EscalarComoAsync(conn, tx, user,
            "select count(*) from pg_constraint where conname = 'hogar_tipo_check' and conrelid = 'public.hogar'::regclass"));
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
            "retirada_ahorro", "deposito_ahorro", "suscripcion_push", "deuda_cuenta", "producto_compra", "enlace_lista_compra",
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

        // Ninguna función de public es invocable sin sesión por /rest/v1/rpc, y con sesión solo las que usa la RLS. El auth stub
        // imita los privilegios por defecto de Supabase, que conceden EXECUTE explícito a anon y authenticated en cada función nueva.
        Assert.Equal("", await EscalarAsync(conn,
            "select coalesce(string_agg(proname, ', ' order by proname), '') from pg_proc " +
            "where pronamespace = 'public'::regnamespace and has_function_privilege('anon', oid, 'EXECUTE')"));
        Assert.Equal("es_admin_hogar, hogar_tiene_otro_admin, mi_miembro_id, mis_hogares", await EscalarAsync(conn,
            "select coalesce(string_agg(proname, ', ' order by proname), '') from pg_proc " +
            "where pronamespace = 'public'::regnamespace and has_function_privilege('authenticated', oid, 'EXECUTE')"));
    }

    [SkippableFact]
    public async Task RolDeLaApi_SoloLeeYEscribeFilasDePublic()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        // Sin atributos de administración ni pertenencia a otro rol (SET ROLE postgres le devolvería todo).
        Assert.Equal(1L, await EscalarAsync(conn, """
            select count(*) from pg_roles where rolname = 'miparte_api' and rolcanlogin
              and not (rolsuper or rolcreatedb or rolcreaterole or rolreplication or rolbypassrls)
            """));
        Assert.Equal(0L, await EscalarAsync(conn,
            "select count(*) from pg_auth_members am join pg_roles r on r.oid = am.member where r.rolname = 'miparte_api'"));

        // Cada tabla de public (también las futuras): RLS con su política, filas sí, TRUNCATE no; la auditoría solo se añade.
        var tablas = new List<string>();
        await using (var cmd = new NpgsqlCommand("select tablename from pg_tables where schemaname = 'public' order by 1", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) tablas.Add(r.GetString(0));
        Assert.NotEmpty(tablas);
        foreach (var tabla in tablas)
        {
            Assert.Equal(1L, await EscalarAsync(conn,
                $"select count(*) from pg_policies where schemaname = 'public' and tablename = '{tabla}' and 'miparte_api' = any(roles)"));
            var esperados = tabla == "auditoria"
                ? new Dictionary<string, bool> { ["SELECT"] = true, ["INSERT"] = true, ["UPDATE"] = false, ["DELETE"] = false }
                : new Dictionary<string, bool> { ["SELECT"] = true, ["INSERT"] = true, ["UPDATE"] = true, ["DELETE"] = true };
            esperados["TRUNCATE"] = false;
            esperados["REFERENCES"] = false;
            esperados["TRIGGER"] = false;
            foreach (var (privilegio, esperado) in esperados)
                Assert.True(esperado == (bool)(await EscalarAsync(conn,
                        $"select has_table_privilege('miparte_api', 'public.{tabla}', '{privilegio}')"))!,
                    $"miparte_api {(esperado ? "debe" : "no debe")} tener {privilegio} sobre {tabla}");
        }

        // Ni DDL en public ni acceso a auth (usuarios de Supabase) ni al journal de migraciones ni a las RPC de alta.
        Assert.False((bool)(await EscalarAsync(conn, "select has_schema_privilege('miparte_api', 'public', 'CREATE')"))!);
        Assert.False((bool)(await EscalarAsync(conn, "select has_schema_privilege('miparte_api', 'auth', 'USAGE')"))!);
        Assert.False((bool)(await EscalarAsync(conn, "select has_schema_privilege('miparte_api', 'miparte_migraciones', 'USAGE')"))!);
        Assert.False((bool)(await EscalarAsync(conn, "select has_function_privilege('miparte_api', 'public.crear_hogar(text, text)', 'EXECUTE')"))!);
        Assert.False((bool)(await EscalarAsync(conn, "select has_database_privilege('miparte_api', current_database(), 'CREATE')"))!);

        // Y en la práctica: escribe una fila (la RLS le deja), pero no puede quitar la RLS ni borrar tablas.
        await using var tx = await conn.BeginTransactionAsync();
        await using (var rol = new NpgsqlCommand("set local role miparte_api", conn, tx))
            await rol.ExecuteNonQueryAsync();
        await using (var ins = new NpgsqlCommand("insert into public.hogar (nombre) values ('Casa rol api') returning 1", conn, tx))
            Assert.Equal(1, await ins.ExecuteScalarAsync());
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var ddl = new NpgsqlCommand("alter table public.gasto disable row level security", conn, tx);
            await ddl.ExecuteNonQueryAsync();
        });
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        await tx.RollbackAsync();
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
    public async Task DeudaCuenta_RestringeTipoImporteConceptoYMiembroDelHogar()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        var otroUser = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();
        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user, "select public.crear_hogar('Casa deuda', 'Ana')"))!;
        var miembro = (Guid)(await EscalarComoAsync(conn, tx, user, "select id from public.miembro where hogar_id = @h", ("h", hogar)))!;
        var otroHogar = (Guid)(await EscalarComoAsync(conn, tx, otroUser, "select public.crear_hogar('Otra casa', 'Eva')"))!;
        var ajeno = (Guid)(await EscalarComoAsync(conn, tx, otroUser, "select id from public.miembro where hogar_id = @h", ("h", otroHogar)))!;

        const string insertar =
            "insert into public.deuda_cuenta (hogar_id, miembro_id, fecha, importe, tipo, concepto) values (@h, @m, '2026-10-01', @i, @t, @c) returning 1";
        object[] valido = [hogar, miembro, 10m, "adelanto", "Fontanero"];
        Assert.Equal(1, await EscalarComoAsync(conn, tx, user, insertar, Parametros(valido)));
        foreach (var tipo in new[] { "devolucion", "compensacion" })
            Assert.Equal(1, await EscalarComoAsync(conn, tx, user, insertar, Parametros([hogar, miembro, 10m, tipo, "x"])));

        // Tipo desconocido, importe no positivo y concepto de más de 200 caracteres: restricciones CHECK.
        foreach (object[] malo in new[]
        {
            new object[] { hogar, miembro, 10m, "prestamo", "x" },
            new object[] { hogar, miembro, 0m, "adelanto", "x" },
            new object[] { hogar, miembro, -3m, "adelanto", "x" },
            new object[] { hogar, miembro, 10m, "adelanto", new string('a', 201) },
        })
        {
            await tx.SaveAsync("antes");
            var ex = await Assert.ThrowsAsync<PostgresException>(() => EscalarComoAsync(conn, tx, user, insertar, Parametros(malo)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            await tx.RollbackAsync("antes");
        }

        // Un miembro de otro hogar rompe la clave foránea compuesta (hogar_id, miembro_id).
        await tx.SaveAsync("antes");
        var fk = await Assert.ThrowsAsync<PostgresException>(() => EscalarComoAsync(conn, tx, user, insertar, Parametros([hogar, ajeno, 10m, "adelanto", "x"])));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, fk.SqlState);
        await tx.RollbackAsync("antes");
        await tx.RollbackAsync();

        static (string, object)[] Parametros(object[] v) => [("h", v[0]), ("m", v[1]), ("i", v[2]), ("t", v[3]), ("c", v[4])];
    }

    [SkippableFact]
    public async Task ListaCompra_RestringeNombreCantidadYTokenUnico()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();
        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user, "select public.crear_hogar('Casa compra', 'Ana')"))!;
        var miembro = (Guid)(await EscalarComoAsync(conn, tx, user, "select id from public.miembro where hogar_id = @h", ("h", hogar)))!;

        Assert.Equal(false, await EscalarComoAsync(conn, tx, user, "select lista_compra_activa from public.hogar where id = @h", ("h", hogar)));

        const string producto = "insert into public.producto_compra (hogar_id, nombre, cantidad) values (@h, @n, @c) returning cantidad";
        Assert.Equal(1, await EscalarComoAsync(conn, tx, user, "insert into public.producto_compra (hogar_id, nombre) values (@h, 'Leche') returning cantidad", ("h", hogar)));
        Assert.Equal(999, await EscalarComoAsync(conn, tx, user, producto, ("h", hogar), ("n", "Agua"), ("c", 999)));

        foreach (var (nombre, cantidad) in new[] { ("Leche", 0), ("Leche", 1000), ("   ", 1), ("", 1), (new string('a', 101), 1) })
        {
            await tx.SaveAsync("antes");
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                EscalarComoAsync(conn, tx, user, producto, ("h", hogar), ("n", nombre), ("c", cantidad)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            await tx.RollbackAsync("antes");
        }

        // El token solo se guarda como hash y no se repite: dos enlaces no pueden compartirlo.
        const string enlace = "insert into public.enlace_lista_compra (hogar_id, token_hash, creado_por) values (@h, @t, @m) returning 1";
        Assert.Equal(1, await EscalarComoAsync(conn, tx, user, enlace, ("h", hogar), ("t", "hash-1"), ("m", miembro)));
        await tx.SaveAsync("antes");
        var duplicado = await Assert.ThrowsAsync<PostgresException>(() =>
            EscalarComoAsync(conn, tx, user, enlace, ("h", hogar), ("t", "hash-1"), ("m", miembro)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicado.SqlState);
        await tx.RollbackAsync("antes");

        Assert.Equal(true, await EscalarComoAsync(conn, tx, user,
            "select activo from public.enlace_lista_compra where token_hash = 'hash-1'"));
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task TablasDeDeudaYListaCompra_TienenRlsPoliticaYSoloLecturaParaAuthenticated()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        foreach (var tabla in new[] { "deuda_cuenta", "producto_compra", "enlace_lista_compra" })
        {
            Assert.True((bool)(await EscalarAsync(conn, $"select relrowsecurity from pg_class where oid = 'public.{tabla}'::regclass"))!, $"{tabla} debe tener RLS");
            Assert.Equal(1L, await EscalarAsync(conn, $"select count(*) from pg_policies where schemaname = 'public' and tablename = '{tabla}' and 'authenticated' = any(roles)"));
            foreach (var privilegio in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
                Assert.False((bool)(await EscalarAsync(conn, $"select has_table_privilege('authenticated', 'public.{tabla}', '{privilegio}')"))!,
                    $"authenticated no debe tener {privilegio} sobre {tabla}");
            Assert.True((bool)(await EscalarAsync(conn, $"select has_table_privilege('authenticated', 'public.{tabla}', 'SELECT')"))!);
            Assert.False((bool)(await EscalarAsync(conn, $"select has_table_privilege('anon', 'public.{tabla}', 'SELECT')"))!);
        }
    }

    [SkippableFact]
    public async Task PermisoListaCompra_LoAdmiteElCheckDeMiembro_YUnoInventadoNo()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        var user = await CrearUsuarioAsync(conn);
        await using var tx = await conn.BeginTransactionAsync();
        var hogar = (Guid)(await EscalarComoAsync(conn, tx, user, "select public.crear_hogar('Casa permisos', 'Ana')"))!;

        Assert.Equal(1, await EscalarComoAsync(conn, tx, user,
            "update public.miembro set permisos = array['lista.compra', 'gastos.crear'] where hogar_id = @h returning 1", ("h", hogar)));
        await tx.SaveAsync("antes");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => EscalarComoAsync(conn, tx, user,
            "update public.miembro set permisos = array['lista.inventada'] where hogar_id = @h returning 1", ("h", hogar)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        await tx.RollbackAsync("antes");
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Indices_ParcialesDeCuentaComunYAhorro_ExistenYElRedundanteDeInvitacionesNo()
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");

        await using var conn = await AbrirAsync();
        const string existe = "select count(*) from pg_indexes where schemaname = 'public' and indexname = @n";
        foreach (var (nombre, esperado) in new[] { ("gasto_cuenta_comun_idx", 1L), ("gasto_ahorro_idx", 1L), ("invitacion_hogar_idx", 0L) })
        {
            await using var cmd = new NpgsqlCommand(existe, conn);
            cmd.Parameters.AddWithValue("n", nombre);
            Assert.Equal(esperado, (long)(await cmd.ExecuteScalarAsync())!);
        }
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
