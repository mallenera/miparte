using MiParte.Core.Application;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Perfiles de reparto probados sin HTTP ni base de datos, con un repositorio en memoria.</summary>
public class ServicioPerfilesTests
{
    private static readonly Guid Hogar = Guid.NewGuid();

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid(), _lucia = Guid.NewGuid();
    private readonly RepositorioFalso _repo = new();

    public ServicioPerfilesTests()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos" });
        _repo.Miembros.Add(new Miembro { Id = _lucia, HogarId = Hogar, Nombre = "Lucía", Tipo = TipoMiembro.ACargo, ResponsableId = _ana });
    }

    private ServicioPerfiles Servicio => new(_repo);

    private Task<Resultado<PerfilReparto>> Crear(string nombre, string modo, params (Guid, decimal)[] detalle)
        => Servicio.CrearAsync(Hogar, nombre, modo, detalle.Select(d => new ValorMiembro(d.Item1, d.Item2)).ToList(), false, default);

    [Fact]
    public async Task Crear_PorcentajeQueSuma100_LoGuardaConSuDetalle()
    {
        var r = await Crear("60/40", "porcentaje", (_ana, 60m), (_marcos, 40m));

        Assert.Null(r.Error);
        Assert.Equal(2, r.Valor!.Detalles.Count);
        Assert.Equal(ModoReparto.Porcentaje, r.Valor.Modo);
    }

    [Fact]
    public async Task Crear_PorcentajeQueNoSuma100_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("x", "porcentaje", (_ana, 50m), (_marcos, 40m))).Error!.Tipo);

    [Fact]
    public async Task Crear_PartesTodasACero_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("x", "partes", (_ana, 0m), (_marcos, 0m))).Error!.Tipo);

    [Fact]
    public async Task Crear_ConValoresNegativos_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("x", "partes", (_ana, 3m), (_marcos, -1m))).Error!.Tipo);

    [Fact]
    public async Task Crear_ConMiembrosRepetidos_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("x", "partes", (_ana, 1m), (_ana, 2m))).Error!.Tipo);

    [Fact]
    public async Task Crear_ConUnMiembroACargoDeUnAdulto_EsValido()
        => Assert.Null((await Crear("x", "partes", (_ana, 1m), (_lucia, 1m))).Error);

    [Theory]
    [InlineData("cuenta_comun")]
    [InlineData("individual")]
    public async Task Crear_ModoSinDetalleConDetalle_EsInvalido(string modo)
        => Assert.Equal(TipoError.Invalido, (await Crear("x", modo, (_ana, 1m))).Error!.Tipo);

    [Fact]
    public async Task Crear_ConModoDesconocido_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("x", "al_azar")).Error!.Tipo);

    [Fact]
    public async Task Crear_ConNombreRepetido_EsConflicto()
    {
        await Crear("Casa", "individual");

        var r = await Crear("Casa", "cuenta_comun");

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Eliminar_EnUso_EsConflictoYLoConserva()
    {
        var p = (await Crear("Casa", "individual")).Valor!;
        _repo.EnUso.Add(p.Id);

        var r = await Servicio.EliminarAsync(p.Id, null, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(true, r.Error.Extra!["enUso"]);
        Assert.Single(_repo.Perfiles);
    }

    [Fact]
    public async Task Eliminar_EnUsoConDestino_MueveElUsoYBorra()
    {
        var p = (await Crear("Casa", "individual")).Valor!;
        var destino = (await Crear("Otro", "individual")).Valor!;
        _repo.EnUso.Add(p.Id);

        var r = await Servicio.EliminarAsync(p.Id, destino.Id, default);

        Assert.Null(r.Error);
        Assert.Equal((p.Id, destino.Id), Assert.Single(_repo.Movidos));
        Assert.DoesNotContain(_repo.Perfiles, x => x.Id == p.Id);
    }

    [Fact]
    public async Task Eliminar_EnUsoConDestinoDeOtraNaturaleza_EsInvalido()
    {
        var p = (await Crear("Casa", "individual")).Valor!;
        var cuenta = (await Crear("Cuenta", "cuenta_comun")).Valor!;
        _repo.EnUso.Add(p.Id);

        var r = await Servicio.EliminarAsync(p.Id, cuenta.Id, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_repo.Movidos);
    }

    [Fact]
    public async Task Eliminar_EnUsoConDestinoInexistenteOElMismo_EsInvalido()
    {
        var p = (await Crear("Casa", "individual")).Valor!;
        _repo.EnUso.Add(p.Id);

        Assert.Equal(TipoError.Invalido, (await Servicio.EliminarAsync(p.Id, Guid.NewGuid(), default)).Error!.Tipo);
        Assert.Equal(TipoError.Invalido, (await Servicio.EliminarAsync(p.Id, p.Id, default)).Error!.Tipo);
    }

    [Fact]
    public async Task Actualizar_SustituyeElDetalle()
    {
        var p = (await Crear("Casa", "partes", (_ana, 2m), (_marcos, 1m))).Valor!;

        var r = await Servicio.ActualizarAsync(p.Id, "Casa", "partes", [new ValorMiembro(_marcos, 5m)], false, default);

        Assert.Null(r.Error);
        var d = Assert.Single(r.Valor!.Detalles);
        Assert.Equal((_marcos, 5m), (d.MiembroId, d.Valor));
    }

    [Fact]
    public async Task Crear_MarcadoPredeterminadoParaGastos_LoGuarda()
    {
        var r = await Servicio.CrearAsync(Hogar, "Casa", "individual", null, true, default);

        Assert.Null(r.Error);
        Assert.True(r.Valor!.PredeterminadoGastos);
    }

    [Fact]
    public async Task Actualizar_CambiaLaMarcaPredeterminadoParaGastos()
    {
        var p = (await Crear("Casa", "partes", (_ana, 1m))).Valor!;
        Assert.False(p.PredeterminadoGastos);

        var marcado = await Servicio.ActualizarAsync(p.Id, "Casa", "partes", [new ValorMiembro(_ana, 1m)], true, default);
        Assert.True(marcado.Valor!.PredeterminadoGastos);

        var desmarcado = await Servicio.ActualizarAsync(p.Id, "Casa", "partes", [new ValorMiembro(_ana, 1m)], false, default);
        Assert.False(desmarcado.Valor!.PredeterminadoGastos);
    }

    private sealed class RepositorioFalso : IRepositorioPerfiles
    {
        public List<Miembro> Miembros { get; } = [];
        public List<PerfilReparto> Perfiles { get; } = [];
        public HashSet<Guid> EnUso { get; } = [];
        private readonly List<PerfilReparto> _nuevos = [];
        private readonly List<PerfilReparto> _borrar = [];

        public Task<IReadOnlyList<PerfilReparto>> ListarAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PerfilReparto>>(Perfiles.OrderBy(p => p.Nombre).ToList());

        public Task<PerfilReparto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Perfiles.FirstOrDefault(p => p.Id == id));

        public Task<PerfilReparto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

        public Task<int> ContarParticipantesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            var adultos = Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).Select(m => m.Id).ToHashSet();
            return Task.FromResult(Miembros.Count(m => ids.Contains(m.Id) && m.Activo
                && (m.Tipo == TipoMiembro.Adulto || (m.ResponsableId is { } r && adultos.Contains(r)))));
        }

        public Task<bool> NombreEnUsoAsync(string nombre, Guid? excluirId, CancellationToken ct)
            => Task.FromResult(Perfiles.Any(p => p.Nombre == nombre && p.Id != excluirId));

        public Task<bool> EnUsoAsync(Guid id, CancellationToken ct) => Task.FromResult(EnUso.Contains(id));

        public List<(Guid Origen, Guid Destino)> Movidos { get; } = [];

        public Task ReasignarUsoAsync(Guid origen, Guid destino, CancellationToken ct)
        {
            Movidos.Add((origen, destino));
            return Task.CompletedTask;
        }

        public void Anadir(PerfilReparto perfil) => _nuevos.Add(perfil);

        public void ReemplazarDetalle(PerfilReparto perfil, IReadOnlyList<PerfilRepartoDetalle> nuevo)
        {
            perfil.Detalles.Clear();
            perfil.Detalles.AddRange(nuevo);
        }

        public void Eliminar(PerfilReparto perfil) => _borrar.Add(perfil);

        public Task<bool> GuardarAsync(CancellationToken ct)
        {
            Perfiles.AddRange(_nuevos);
            foreach (var p in _borrar) Perfiles.Remove(p);
            _nuevos.Clear();
            _borrar.Clear();
            return Task.FromResult(true);
        }
    }
}

/// <summary>Categorías probadas sin HTTP ni base de datos, con un repositorio en memoria.</summary>
public class ServicioCategoriasTests
{
    private static readonly Guid Hogar = Guid.NewGuid();

    private readonly PerfilReparto _cuenta = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Cuenta", Modo = ModoReparto.CuentaComun };
    private readonly PerfilReparto _individual = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Cada uno", Modo = ModoReparto.Individual };
    private readonly RepositorioFalso _repo = new();
    private readonly RepositorioGastosFalso _gastos = new() { CuentaActiva = true };

    public ServicioCategoriasTests() => _repo.Perfiles.AddRange([_cuenta, _individual]);

    private ServicioCategorias Servicio => new(_repo, _gastos);

    private Task<Resultado<Categoria>> Crear(string nombre, Guid? padre = null, Guid? perfil = null, bool aCargo = false)
        => Servicio.CrearAsync(Hogar, new DatosCategoria(nombre, padre, perfil, aCargo), default);

    [Fact]
    public async Task Crear_PredeterminadaParaGastos_LaGuardaYActualizarLaCambia()
    {
        var c = (await Servicio.CrearAsync(Hogar, new DatosCategoria("Super", null, null, false, true), default)).Valor!;
        Assert.True(c.PredeterminadoGastos);

        var r = await Servicio.ActualizarAsync(c.Id, new DatosCategoria("Super", null, null, false, false), default);

        Assert.False(r.Valor!.PredeterminadoGastos);
    }

    [Fact]
    public async Task Crear_ACargoSinPerfil_UsaElPerfilDeCuentaComun()
    {
        var r = await Crear("Comunidad", aCargo: true);

        Assert.Null(r.Error);
        Assert.Equal(_cuenta.Id, r.Valor!.PerfilRepartoId);
        Assert.True(r.Valor.ACargoCuentaComun);
    }

    [Fact]
    public async Task Crear_ConPerfilDeCuentaComun_QuedaACargoDeLaCuenta()
    {
        var r = await Crear("Comunidad", perfil: _cuenta.Id);

        Assert.True(r.Valor!.ACargoCuentaComun);
    }

    [Fact]
    public async Task Crear_ACargoConUnPerfilQueNoEsDeCuentaComun_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("X", perfil: _individual.Id, aCargo: true)).Error!.Tipo);

    [Fact]
    public async Task Crear_ACargoConLaCuentaInactiva_EsConflicto()
    {
        _gastos.CuentaActiva = false;

        var r = await Crear("Comunidad", aCargo: true);

        Assert.Equal(Mensajes.CuentaInactiva, r.Error!.Mensaje);
    }

    [Fact]
    public async Task Actualizar_UnaYaMarcadaConLaCuentaInactiva_NoExigeLaCuenta()
    {
        var c = (await Crear("Comunidad", aCargo: true)).Valor!;
        _repo.Marcadas.Add(c.Id);
        _gastos.CuentaActiva = false;

        var r = await Servicio.ActualizarAsync(c.Id, new DatosCategoria("Comunidad 2", null, null, true), default);

        Assert.Null(r.Error);
    }

    [Fact]
    public async Task Crear_ConElMismoNombreEnElMismoNivel_EsConflicto()
    {
        await Crear("Casa");

        Assert.Equal(TipoError.Conflicto, (await Crear("Casa")).Error!.Tipo);
    }

    [Fact]
    public async Task Crear_ConElMismoNombreBajoOtroPadre_Funciona()
    {
        var casa = (await Crear("Casa")).Valor!;
        var ocio = (await Crear("Ocio")).Valor!;
        await Crear("Varios", casa.Id);

        Assert.Null((await Crear("Varios", ocio.Id)).Error);
    }

    [Fact]
    public async Task Crear_ConPadreInexistente_EsInvalido()
        => Assert.Equal(TipoError.Invalido, (await Crear("X", Guid.NewGuid())).Error!.Tipo);

    [Fact]
    public async Task Actualizar_ComoPadreDeSiMisma_EsInvalido()
    {
        var c = (await Crear("Casa")).Valor!;

        var r = await Servicio.ActualizarAsync(c.Id, new DatosCategoria("Casa", c.Id, null, false), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Actualizar_BajoUnaDescendiente_EsInvalidoPorCiclo()
    {
        var abuela = (await Crear("Casa")).Valor!;
        var hija = (await Crear("Hogar", abuela.Id)).Valor!;
        var nieta = (await Crear("Cocina", hija.Id)).Valor!;

        var r = await Servicio.ActualizarAsync(abuela.Id, new DatosCategoria("Casa", nieta.Id, null, false), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Contains("ciclo", r.Error.Mensaje);
    }

    [Fact]
    public async Task Eliminar_ConSubcategorias_EsConflicto()
    {
        var casa = (await Crear("Casa")).Valor!;
        await Crear("Cocina", casa.Id);

        var r = await Servicio.EliminarAsync(casa.Id, null, default);

        Assert.Equal("La categoría tiene subcategorías.", r.Error!.Mensaje);
    }

    [Fact]
    public async Task Eliminar_ConGastos_EsConflicto()
    {
        var casa = (await Crear("Casa")).Valor!;
        _repo.ConGastos.Add(casa.Id);

        var r = await Servicio.EliminarAsync(casa.Id, null, default);

        Assert.Equal("La categoría tiene gastos asociados.", r.Error!.Mensaje);
        Assert.Equal(true, r.Error.Extra!["enUso"]);
    }

    [Fact]
    public async Task Eliminar_ConGastosYDestino_LosMueveYBorra()
    {
        var casa = (await Crear("Casa")).Valor!;
        var ocio = (await Crear("Ocio")).Valor!;
        _repo.ConGastos.Add(casa.Id);

        var r = await Servicio.EliminarAsync(casa.Id, ocio.Id, default);

        Assert.Null(r.Error);
        Assert.Equal((casa.Id, ocio.Id), Assert.Single(_repo.Movidos));
        Assert.DoesNotContain(_repo.Categorias, c => c.Id == casa.Id);
    }

    [Fact]
    public async Task Eliminar_ConGastosYDestinoInexistente_EsInvalidoYNoMueve()
    {
        var casa = (await Crear("Casa")).Valor!;
        _repo.ConGastos.Add(casa.Id);

        var r = await Servicio.EliminarAsync(casa.Id, Guid.NewGuid(), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_repo.Movidos);
    }

    private sealed class RepositorioFalso : IRepositorioCategorias
    {
        public List<PerfilReparto> Perfiles { get; } = [];
        public List<Categoria> Categorias { get; } = [];
        public HashSet<Guid> Marcadas { get; } = [];
        public HashSet<Guid> ConGastos { get; } = [];
        private readonly List<Categoria> _nuevas = [];
        private readonly List<Categoria> _borrar = [];

        public Task<IReadOnlyList<Categoria>> ListarAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Categoria>>(Categorias.OrderBy(c => c.Nombre).ToList());

        public Task<Categoria?> ObtenerParaEditarAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Categorias.FirstOrDefault(c => c.Id == id));

        public Task<PerfilReparto?> PerfilAsync(Guid id, CancellationToken ct) => Task.FromResult(Perfiles.FirstOrDefault(p => p.Id == id));

        public Task<PerfilReparto?> PerfilDeCuentaComunAsync(CancellationToken ct)
            => Task.FromResult(Perfiles.FirstOrDefault(p => p.Modo == ModoReparto.CuentaComun));

        public Task<bool> EstaACargoDeLaCuentaAsync(Guid id, CancellationToken ct) => Task.FromResult(Marcadas.Contains(id));

        public Task<IReadOnlyDictionary<Guid, Guid?>> PadresAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(Categorias.ToDictionary(c => c.Id, c => c.CategoriaPadreId));

        public Task<bool> NombreEnNivelAsync(Guid? padreId, string nombre, Guid? excluirId, CancellationToken ct)
            => Task.FromResult(Categorias.Any(c => c.CategoriaPadreId == padreId && c.Nombre == nombre && c.Id != excluirId));

        public Task<bool> TieneSubcategoriasAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Categorias.Any(c => c.CategoriaPadreId == id));

        public Task<bool> TieneGastosAsync(Guid id, CancellationToken ct) => Task.FromResult(ConGastos.Contains(id));

        public List<(Guid Origen, Guid Destino)> Movidos { get; } = [];

        public Task ReasignarGastosAsync(Guid origen, Guid destino, CancellationToken ct)
        {
            Movidos.Add((origen, destino));
            return Task.CompletedTask;
        }

        public void Anadir(Categoria categoria) => _nuevas.Add(categoria);

        public void Eliminar(Categoria categoria) => _borrar.Add(categoria);

        public Task<bool> GuardarAsync(CancellationToken ct)
        {
            Categorias.AddRange(_nuevas);
            foreach (var c in _borrar) Categorias.Remove(c);
            _nuevas.Clear();
            _borrar.Clear();
            return Task.FromResult(true);
        }
    }
}

/// <summary>Cierre de mes probado sin HTTP ni base de datos, con un repositorio en memoria y un reloj fijo.</summary>
public class ServicioCierresTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Agosto = new(2026, 8, 1);

    private readonly Miembro _ana = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Ana", UserId = Guid.NewGuid() };
    private readonly RepositorioFalso _repo = new();

    private ServicioCierres Servicio => new(_repo, new RelojFijo(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Cerrar_UnMesTerminado_LoGuardaConQuienLoCerro()
    {
        var r = await Servicio.CerrarAsync(Hogar, Agosto, _ana, default);

        Assert.Null(r.Error);
        Assert.Equal("Ana", r.Valor!.Autor);
        Assert.Equal(_ana.UserId, Assert.Single(_repo.Cierres).CerradoPor);
    }

    [Fact]
    public async Task Cerrar_UnMesQueNoHaTerminado_EsInvalido()
    {
        var r = await Servicio.CerrarAsync(Hogar, new DateOnly(2026, 9, 1), _ana, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_repo.Cierres);
    }

    [Fact]
    public async Task Cerrar_UnMesYaCerrado_EsConflicto()
    {
        await Servicio.CerrarAsync(Hogar, Agosto, _ana, default);

        var r = await Servicio.CerrarAsync(Hogar, Agosto, _ana, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Cerrar_SiOtroCierreSimultaneoSeAdelanta_EsConflicto()
    {
        _repo.RechazarAlGuardar = true;

        var r = await Servicio.CerrarAsync(Hogar, Agosto, _ana, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
    }

    [Fact]
    public async Task Reabrir_UnMesNoCerrado_EsNoEncontrado()
        => Assert.Equal(TipoError.NoEncontrado, (await Servicio.ReabrirAsync(Agosto, default)).Error!.Tipo);

    [Fact]
    public async Task Reabrir_UnMesCerrado_LoQuita()
    {
        await Servicio.CerrarAsync(Hogar, Agosto, _ana, default);

        var r = await Servicio.ReabrirAsync(Agosto, default);

        Assert.Null(r.Error);
        Assert.Empty(_repo.Cierres);
    }

    [Fact]
    public async Task Listar_IncluyeElNombreDeQuienCerro()
    {
        _repo.Nombres[_ana.UserId!.Value] = "Ana";
        await Servicio.CerrarAsync(Hogar, Agosto, _ana, default);

        var l = await Servicio.ListarAsync(default);

        Assert.Equal("Ana", Assert.Single(l).Autor);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class RepositorioFalso : IRepositorioCierres
    {
        public List<MesCerrado> Cierres { get; } = [];
        public Dictionary<Guid, string> Nombres { get; } = [];
        public bool RechazarAlGuardar { get; set; }

        public Task<IReadOnlyList<MesCerrado>> ListarAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MesCerrado>>(Cierres.OrderByDescending(c => c.Mes).ToList());

        public Task<IReadOnlyDictionary<Guid, string>> NombresPorUsuarioAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(Nombres);

        public Task<bool> EstaCerradoAsync(DateOnly inicio, CancellationToken ct) => Task.FromResult(Cierres.Any(c => c.Mes == inicio));

        public Task<bool> GuardarAsync(MesCerrado cierre, CancellationToken ct)
        {
            if (RechazarAlGuardar) return Task.FromResult(false);
            Cierres.Add(cierre);
            return Task.FromResult(true);
        }

        public Task<bool> EliminarAsync(DateOnly inicio, CancellationToken ct) => Task.FromResult(Cierres.RemoveAll(c => c.Mes == inicio) > 0);
    }
}
