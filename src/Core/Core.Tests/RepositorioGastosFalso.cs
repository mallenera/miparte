using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Repositorio en memoria: cada colección es lo que «hay» en el hogar.</summary>
internal sealed class RepositorioGastosFalso : IRepositorioGastos
{
    public List<Gasto> Gastos { get; } = [];
    public List<Miembro> Miembros { get; } = [];
    public List<Guid> Categorias { get; } = [];
    public List<PerfilReparto> Perfiles { get; } = [];
    public List<DateOnly> MesesCerrados { get; } = [];
    public bool CuentaActiva { get; set; }
    public bool AhorroOn { get; set; }
    public DatosAhorro Ahorro { get; set; } = new([], [], 0m, 0m);
    public bool RechazarAlGuardar { get; set; }

    private readonly List<Gasto> _porAnadir = [];
    private readonly List<Gasto> _porEliminar = [];

    public Task<IReadOnlyList<Gasto>> ListarAsync(FiltroGastos filtro, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Gasto>>(Gastos.ToList());

    public Task<Gasto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(Gastos.FirstOrDefault(g => g.Id == id));

    public Task<Gasto?> ObtenerParaEditarAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, ct);

    public Task<bool> CategoriaExisteAsync(Guid id, CancellationToken ct) => Task.FromResult(Categorias.Contains(id));

    public Task<bool> EsAdultoActivoAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Miembros.Any(m => m.Id == id && m.Activo && m.Tipo == TipoMiembro.Adulto));

    public Task<PerfilReparto?> ObtenerPerfilAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Perfiles.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Miembro>> AdultosActivosAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Miembro>>(Miembros.OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToList());

    public Task<bool> CuentaComunActivaAsync(CancellationToken ct) => Task.FromResult(CuentaActiva);

    public Task<bool> AhorroActivoAsync(CancellationToken ct) => Task.FromResult(AhorroOn);

    public Task<DatosAhorro> DatosAhorroAsync(Guid? excluirGasto, CancellationToken ct) => Task.FromResult(Ahorro);

    public Task<bool> MesCerradoAsync(DateOnly fecha, CancellationToken ct)
        => Task.FromResult(MesesCerrados.Contains(new DateOnly(fecha.Year, fecha.Month, 1)));

    public void Anadir(Gasto gasto) => _porAnadir.Add(gasto);

    public void Eliminar(Gasto gasto) => _porEliminar.Add(gasto);

    public Task<bool> GuardarAsync(CancellationToken ct)
    {
        var pendientesAnadir = _porAnadir.ToList();
        var pendientesEliminar = _porEliminar.ToList();
        _porAnadir.Clear();
        _porEliminar.Clear();
        if (RechazarAlGuardar) return Task.FromResult(false);
        Gastos.AddRange(pendientesAnadir);
        foreach (var g in pendientesEliminar) Gastos.Remove(g);
        return Task.FromResult(true);
    }
}
