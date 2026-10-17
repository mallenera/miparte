using MiParte.Core.Application;
using MiParte.Core.Application.Auditoria;
using MiParte.Core.Application.Hogares;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Casos de uso de hogares probados sin HTTP ni base de datos.</summary>
public class ServicioHogaresTests
{
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly RepositorioFalso _repo = new();

    private ServicioHogares Servicio => new(_repo);

    [Fact]
    public async Task Crear_deja_al_creador_como_adulto_admin_con_cuenta()
    {
        var r = await Servicio.CrearAsync(_usuario, "  Casa ", " Ana ", TipoGrupo.Hogar, default);

        Assert.Null(r.Error);
        Assert.Equal("Casa", r.Valor!.Nombre);
        var creador = Assert.Single(_repo.Miembros);
        Assert.Equal(("Ana", TipoMiembro.Adulto, RolMiembro.Admin, _usuario), (creador.Nombre, creador.Tipo, creador.Rol, creador.UserId));
        Assert.Equal(r.Valor.Id, creador.HogarId);
    }

    [Fact]
    public async Task Crear_guarda_el_tipo_del_grupo_que_se_pide()
    {
        var r = await Servicio.CrearAsync(_usuario, "Piso", "Ana", TipoGrupo.Viaje, default);

        Assert.Equal(TipoGrupo.Viaje, r.Valor!.Tipo);
        Assert.Equal(TipoGrupo.Viaje, Assert.Single(_repo.Hogares).Tipo);
    }

    [Theory]
    [InlineData("", "Ana")]
    [InlineData("Casa", "  ")]
    [InlineData(null, null)]
    public async Task Crear_sin_nombres_es_invalido(string? hogar, string? miembro)
    {
        var r = await Servicio.CrearAsync(_usuario, hogar, miembro, TipoGrupo.Hogar, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_repo.Hogares);
    }

    [Fact]
    public async Task Crear_con_nombre_demasiado_largo_es_invalido()
    {
        var r = await Servicio.CrearAsync(_usuario, new string('x', 101), "Ana", TipoGrupo.Hogar, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_pasado_el_maximo_de_hogares_es_conflicto()
    {
        _repo.Pertenencias = ServicioHogares.MaxHogaresPorUsuario;

        var r = await Servicio.CrearAsync(_usuario, "Casa", "Ana", TipoGrupo.Hogar, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Obtener_un_hogar_ajeno_no_revela_su_existencia()
    {
        var propio = (await Servicio.CrearAsync(_usuario, "Casa", "Ana", TipoGrupo.Hogar, default)).Valor!;

        Assert.NotNull(await Servicio.ObtenerAsync(_usuario, propio.Id, default));
        Assert.Null(await Servicio.ObtenerAsync(Guid.NewGuid(), propio.Id, default));
    }

    [Fact]
    public async Task Eliminar_exige_el_nombre_exacto_y_luego_lo_borra()
    {
        var hogar = (await Servicio.CrearAsync(_usuario, "Casa", "Ana", TipoGrupo.Hogar, default)).Valor!;
        _repo.Actual = hogar;

        var mal = await Servicio.EliminarAsync("casa", default);
        Assert.Equal(TipoError.Invalido, mal.Error!.Tipo);
        Assert.Single(_repo.Hogares);

        var bien = await Servicio.EliminarAsync(" Casa ", default);
        Assert.Null(bien.Error);
        Assert.Empty(_repo.Hogares);
    }

    [Fact]
    public async Task Eliminar_sin_hogar_actual_es_no_encontrado()
        => Assert.Equal(TipoError.NoEncontrado, (await Servicio.EliminarAsync("x", default)).Error!.Tipo);

    private sealed class RepositorioFalso : IRepositorioHogares
    {
        public List<Hogar> Hogares { get; } = [];
        public List<Miembro> Miembros { get; } = [];
        public Hogar? Actual { get; set; }
        public int? Pertenencias { get; set; }

        public Task<IReadOnlyList<Hogar>> DelUsuarioAsync(Guid userId, CancellationToken ct)
        {
            var ids = Miembros.Where(m => m.UserId == userId).Select(m => m.HogarId).ToHashSet();
            return Task.FromResult<IReadOnlyList<Hogar>>(Hogares.Where(h => ids.Contains(h.Id)).OrderBy(h => h.Nombre).ToList());
        }

        public Task<int> ContarPertenenciasAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Pertenencias ?? Miembros.Count(m => m.UserId == userId));

        public Task CrearConSemillaAsync(Hogar hogar, Miembro creador, Guid userId, CancellationToken ct)
        {
            Hogares.Add(hogar);
            Miembros.Add(creador);
            return Task.CompletedTask;
        }

        public Task<Hogar?> ActualAsync(CancellationToken ct) => Task.FromResult(Actual);

        public Task EliminarAsync(Hogar hogar, CancellationToken ct)
        {
            Hogares.Remove(hogar);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Historial probado sin HTTP ni base de datos.</summary>
public class ServicioAuditoriaTests
{
    private sealed class RepositorioFalso : IRepositorioAuditoria
    {
        public int? LimiteRecibido { get; private set; }

        public Task<IReadOnlyList<EventoAuditoria>> ListarAsync(ConsultaAuditoria c, int limite, CancellationToken ct)
        {
            LimiteRecibido = limite;
            return Task.FromResult<IReadOnlyList<EventoAuditoria>>([]);
        }

        public Task<IReadOnlyDictionary<Guid, string>> AutoresAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task Sin_limite_usa_el_de_por_defecto()
    {
        var repo = new RepositorioFalso();

        var r = await new ServicioAuditoria(repo).ListarAsync(new ConsultaAuditoria(null, null, null, null, null), default);

        Assert.Null(r.Error);
        Assert.Equal(ServicioAuditoria.LimitePorDefecto, repo.LimiteRecibido);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    [InlineData(-5)]
    public async Task Un_limite_fuera_de_rango_es_invalido_y_no_consulta(int limite)
    {
        var repo = new RepositorioFalso();

        var r = await new ServicioAuditoria(repo).ListarAsync(new ConsultaAuditoria(null, null, limite, null, null), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Null(repo.LimiteRecibido);
    }
}
