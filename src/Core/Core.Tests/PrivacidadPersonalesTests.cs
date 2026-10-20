using MiParte.Core.Application;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Privacidad de los gastos personales sin HTTP ni base de datos: la regla y quién puede anotarlos.</summary>
public class PrivacidadPersonalesTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 10, 20);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid(), _categoria = Guid.NewGuid();
    private readonly PerfilReparto _partes = new() { Id = Guid.NewGuid(), HogarId = Hogar, Nombre = "Partes", Modo = ModoReparto.Partes };
    private readonly RepositorioGastosFalso _repo = new();

    public PrivacidadPersonalesTests()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana", Rol = RolMiembro.Admin });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos", Rol = RolMiembro.Admin });
        _repo.Categorias.Add(_categoria);
        _partes.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = Hogar, PerfilId = _partes.Id, MiembroId = _ana, Valor = 1m });
        _partes.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = Hogar, PerfilId = _partes.Id, MiembroId = _marcos, Valor = 1m });
        _repo.Perfiles.Add(_partes);
    }

    [Fact]
    public void LaRegla_UnGastoNoPersonalLoVeTodoElHogar()
    {
        Assert.True(ReglasPrivacidad.EsVisible(false, _ana, _marcos));
        Assert.True(ReglasPrivacidad.EsVisible(false, _ana, null));
    }

    [Fact]
    public void LaRegla_UnPersonalSoloLoVeQuienPaga_NiUnAdminNiOtroMiembro()
    {
        Assert.True(ReglasPrivacidad.EsVisible(true, _ana, _ana));
        Assert.False(ReglasPrivacidad.EsVisible(true, _ana, _marcos));
        Assert.False(ReglasPrivacidad.EsVisible(true, _ana, null)); // sin miembro activo no se ve ninguno
    }

    [Fact]
    public async Task Crear_UnPersonalDeOtro_EsInvalidoCuandoHayQuienLoAnota()
    {
        var r = await new ServicioGastos(_repo).CrearAsync(Hogar, Datos(pagador: _ana, personal: true), default, quien: _marcos);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Equal(ServicioGastos.PersonalDeOtro, r.Error.Mensaje);
        Assert.Empty(_repo.Gastos);
    }

    [Fact]
    public async Task Crear_UnPersonalPropio_SeGuarda()
    {
        var r = await new ServicioGastos(_repo).CrearAsync(Hogar, Datos(pagador: _ana, personal: true), default, quien: _ana);

        Assert.Null(r.Error);
        Assert.True(Assert.Single(_repo.Gastos).EsPersonal);
    }

    [Fact]
    public async Task Editar_CambiarElPagadorDeUnPersonalAOtro_EsInvalido()
    {
        var servicio = new ServicioGastos(_repo);
        var creado = (await servicio.CrearAsync(Hogar, Datos(pagador: _ana, personal: true), default, quien: _ana)).Valor!;

        var r = await servicio.EditarAsync(creado.Id, Datos(pagador: _marcos, personal: true), default, quien: _ana);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    private DatosGasto Datos(Guid pagador, bool personal) => new(Hoy, 40m, _categoria, pagador, _partes.Id, "Gimnasio", false, personal);
}
