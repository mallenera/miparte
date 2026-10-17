using MiParte.Core.Application;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Casos de uso de gastos recurrentes probados sin HTTP ni base de datos, con repositorios en memoria.</summary>
public class ServicioRecurrentesTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Septiembre = new(2026, 9, 1);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid(), _categoria = Guid.NewGuid();
    private readonly PerfilReparto _igual = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Individual", Modo = ModoReparto.Individual };
    private readonly PerfilReparto _cuenta = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Cuenta", Modo = ModoReparto.CuentaComun };
    private readonly RepositorioGastosFalso _gastos = new();
    private readonly RepositorioRecurrentesFalso _recurrentes;

    public ServicioRecurrentesTests()
    {
        _recurrentes = new RepositorioRecurrentesFalso(_gastos);
        _gastos.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _gastos.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos" });
        _gastos.Categorias.Add(_categoria);
        _gastos.Perfiles.AddRange([_igual, _cuenta]);
    }

    private ServicioRecurrentes Servicio => new(_recurrentes, _gastos);

    private DatosRecurrente Datos(
        decimal importe = 60m, short dia = 5, Guid? pagador = null, PerfilReparto? perfil = null, string? concepto = "Internet")
        => new(importe, _categoria, pagador ?? _ana, (perfil ?? _igual).Id, dia, concepto, true);

    private async Task<GastoRecurrente> Plantilla(DatosRecurrente? datos = null)
        => (await Servicio.CrearAsync(Hogar, datos ?? Datos(), default)).Valor!;

    [Fact]
    public async Task Crear_ConDiaFueraDeRango_EsInvalido()
    {
        var r = await Servicio.CrearAsync(Hogar, Datos(dia: 29), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Empty(_recurrentes.Plantillas);
    }

    [Fact]
    public async Task Crear_ConPagadorQueNoEsAdultoActivo_EsInvalido()
    {
        var r = await Servicio.CrearAsync(Hogar, Datos(pagador: Guid.NewGuid()), default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task Crear_RecortaElConcepto()
    {
        var r = await Servicio.CrearAsync(Hogar, Datos(concepto: "  Luz  "), default);

        Assert.Equal("Luz", r.Valor!.Concepto);
    }

    [Fact]
    public async Task Borrar_ConGastosGenerados_EsConflicto()
    {
        var p = await Plantilla();
        await Servicio.GenerarAsync(Septiembre, default);

        var r = await Servicio.BorrarAsync(p.Id, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Single(_recurrentes.Plantillas);
    }

    [Fact]
    public async Task Borrar_SinGastosGenerados_LaQuita()
    {
        var p = await Plantilla();

        var r = await Servicio.BorrarAsync(p.Id, default);

        Assert.Null(r.Error);
        Assert.Empty(_recurrentes.Plantillas);
    }

    [Fact]
    public async Task Generar_CreaUnGastoPorPlantillaYEsIdempotente()
    {
        await Plantilla();

        var primera = await Servicio.GenerarAsync(Septiembre, default);
        var segunda = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal((1, 0), (primera.Valor!.Creados, primera.Valor.YaExistentes));
        Assert.Equal((0, 1), (segunda.Valor!.Creados, segunda.Valor.YaExistentes));
        var gasto = Assert.Single(_gastos.Gastos);
        Assert.Equal(new DateOnly(2026, 9, 5), gasto.Fecha);
        Assert.Equal(60m, gasto.Repartos.Single(r => r.MiembroId == _ana).ImporteAsumido); // individual: lo asume íntegro quien paga
        Assert.Equal(60m, gasto.Repartos.Sum(r => r.ImporteAsumido));
    }

    [Fact]
    public async Task Generar_NoGeneraLasPlantillasInactivas()
    {
        var p = await Plantilla();
        p.Activo = false;

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal((0, 0), (r.Valor!.Creados, r.Valor.YaExistentes));
    }

    [Fact]
    public async Task Generar_EnMesCerrado_EsConflicto()
    {
        await Plantilla();
        _gastos.MesesCerrados.Add(Septiembre);

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal(Mensajes.MesCerrado, r.Error!.Mensaje);
        Assert.Empty(_gastos.Gastos);
    }

    [Fact]
    public async Task Generar_PlantillaDeCuentaComunConLaCuentaInactiva_EsConflictoYNoGeneraNada()
    {
        await Plantilla();
        await Plantilla(Datos(perfil: _cuenta));

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal(Mensajes.CuentaInactiva, r.Error!.Mensaje);
        Assert.Empty(_gastos.Gastos);
    }

    [Fact]
    public async Task Generar_PlantillaDeCuentaComunConLaCuentaActiva_QuedaACargoDeLaCuenta()
    {
        _gastos.CuentaActiva = true;
        await Plantilla(Datos(perfil: _cuenta));

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Null(r.Error);
        Assert.True(Assert.Single(_gastos.Gastos).ACargoCuentaComun);
    }

    [Fact]
    public async Task Generar_SiUnaPlantillaNoSePuedeRepartir_NoGeneraNinguna()
    {
        var porcentaje = new PerfilReparto { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Vacío", Modo = ModoReparto.Porcentaje };
        _gastos.Perfiles.Add(porcentaje);
        await Plantilla(); // esta sí se podría repartir
        await Plantilla(Datos(perfil: porcentaje, concepto: "Alquiler"));

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Contains("Alquiler", r.Error.Mensaje);
        Assert.Empty(_gastos.Gastos);
    }

    [Fact]
    public async Task Generar_SiOtraPeticionSeAdelanta_ReintentaYNoDuplica()
    {
        await Plantilla();
        _recurrentes.DuplicadosAntesDeGuardar = 1;

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Null(r.Error);
        Assert.Single(_gastos.Gastos);
        Assert.Equal((0, 1), (r.Valor!.Creados, r.Valor.YaExistentes)); // el gasto lo generó la otra petición
    }

    [Fact]
    public async Task Generar_SiElConflictoNoSeResuelve_DevuelveConflictoTrasLosReintentos()
    {
        await Plantilla();
        _recurrentes.DuplicadosAntesDeGuardar = int.MaxValue;
        _recurrentes.SimularOtraPeticion = false;

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(4, _recurrentes.IntentosDeGuardado);
    }

    [Fact]
    public async Task Crear_ConRecurrentesDesactivadas_EsConflictoYNoGuarda()
    {
        _recurrentes.Activas = false;

        var r = await Servicio.CrearAsync(Hogar, Datos(), default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal(Mensajes.RecurrentesInactivas, r.Error.Mensaje);
        Assert.Empty(_recurrentes.Plantillas);
    }

    [Fact]
    public async Task Generar_ConRecurrentesDesactivadas_EsConflictoYNoCreaGastos()
    {
        await Plantilla();
        _recurrentes.Activas = false;

        var r = await Servicio.GenerarAsync(Septiembre, default);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Empty(_gastos.Gastos);
    }

    [Fact]
    public async Task Desactivar_NoBorraLasPlantillas_YReactivarVuelveAGenerar()
    {
        var p = await Plantilla();

        Assert.False(await Servicio.ActivarAsync(false, default));
        Assert.False(_recurrentes.Activas);
        Assert.Single(_recurrentes.Plantillas);
        Assert.Equal(p.Id, _recurrentes.Plantillas[0].Id);

        await Servicio.ActivarAsync(true, default);
        var r = await Servicio.GenerarAsync(Septiembre, default);
        Assert.Equal(1, r.Valor!.Creados);
    }

    /// <summary>Repositorio de plantillas en memoria; comparte la lista de gastos con el repositorio de gastos.</summary>
    private sealed class RepositorioRecurrentesFalso(RepositorioGastosFalso gastos) : IRepositorioRecurrentes
    {
        public List<GastoRecurrente> Plantillas { get; } = [];
        public int DuplicadosAntesDeGuardar { get; set; }
        public bool SimularOtraPeticion { get; set; } = true;
        public int IntentosDeGuardado { get; private set; }
        public bool Activas { get; set; } = true;

        private readonly List<GastoRecurrente> _porAnadir = [];
        private readonly List<GastoRecurrente> _porEliminar = [];

        public Task<IReadOnlyList<GastoRecurrente>> ListarAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GastoRecurrente>>(Plantillas.OrderBy(p => p.DiaMes).ToList());

        public Task<bool> RecurrentesActivasAsync(CancellationToken ct) => Task.FromResult(Activas);

        public Task EstablecerRecurrentesActivasAsync(bool activas, CancellationToken ct)
        {
            Activas = activas;
            return Task.CompletedTask;
        }

        public Task<GastoRecurrente?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Plantillas.FirstOrDefault(p => p.Id == id));

        public Task<GastoRecurrente?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

        public void Anadir(GastoRecurrente plantilla) => _porAnadir.Add(plantilla);

        public void Eliminar(GastoRecurrente plantilla) => _porEliminar.Add(plantilla);

        public Task GuardarAsync(CancellationToken ct)
        {
            Plantillas.AddRange(_porAnadir);
            foreach (var p in _porEliminar) Plantillas.Remove(p);
            _porAnadir.Clear();
            _porEliminar.Clear();
            return Task.CompletedTask;
        }

        public Task<bool> TieneGastosGeneradosAsync(Guid id, CancellationToken ct)
            => Task.FromResult(gastos.Gastos.Any(g => g.GastoRecurrenteId == id));

        public Task<IReadOnlyList<GastoRecurrente>> ActivasAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GastoRecurrente>>(Plantillas.Where(p => p.Activo).OrderBy(p => p.DiaMes).ToList());

        public Task<IReadOnlySet<Guid>> GeneradasEnElMesAsync(DateOnly inicio, CancellationToken ct)
        {
            var fin = inicio.AddMonths(1);
            IReadOnlySet<Guid> ids = gastos.Gastos
                .Where(g => g.GastoRecurrenteId != null && g.Fecha >= inicio && g.Fecha < fin)
                .Select(g => g.GastoRecurrenteId!.Value).ToHashSet();
            return Task.FromResult(ids);
        }

        public Task<IReadOnlyDictionary<Guid, PerfilReparto>> PerfilesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, PerfilReparto>>(gastos.Perfiles.ToDictionary(p => p.Id));

        public Task<ResultadoGuardadoGenerados> GuardarGeneradosAsync(IReadOnlyList<Gasto> nuevos, CancellationToken ct)
        {
            IntentosDeGuardado++;
            if (DuplicadosAntesDeGuardar > 0)
            {
                DuplicadosAntesDeGuardar--;
                // Otra petición generó el primero antes que nosotros: en el reintento la lectura ya lo incluye.
                if (SimularOtraPeticion) gastos.Gastos.Add(nuevos[0]);
                return Task.FromResult(ResultadoGuardadoGenerados.Duplicado);
            }

            gastos.Gastos.AddRange(nuevos);
            return Task.FromResult(ResultadoGuardadoGenerados.Guardado);
        }
    }
}
