using MiParte.Core.Application;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Application.Notificaciones;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>
/// Avisos del navegador al cerrar una liquidación y al registrar un pago. Se prueban con el repositorio en memoria de
/// <see cref="ServicioLiquidacionTests"/> y un publicador que guarda los avisos.
/// </summary>
public class ServicioAvisosLiquidacionTests
{
    private static readonly Guid Hogar = Guid.NewGuid();
    private static readonly DateOnly Mes = new(2026, 9, 1);

    private readonly Guid _ana = Guid.NewGuid(), _marcos = Guid.NewGuid();
    private readonly ServicioLiquidacionTests.RepositorioFalso _repo = new();
    private readonly PublicadorFalso _publicador = new();

    public ServicioAvisosLiquidacionTests()
    {
        _repo.Miembros.Add(new Miembro { Id = _ana, HogarId = Hogar, Nombre = "Ana" });
        _repo.Miembros.Add(new Miembro { Id = _marcos, HogarId = Hogar, Nombre = "Marcos" });
    }

    private ServicioLiquidacion Servicio => new(_repo, TimeProvider.System, _publicador);

    private Miembro Yo => _repo.Miembros.First(m => m.Id == _ana);

    /// <summary>Gasto de Ana de <paramref name="importe"/> repartido a partes iguales con Marcos, dentro del mes de septiembre.</summary>
    private void GastoDeAna(decimal importe, DateOnly fecha)
    {
        var mitad = importe / 2;
        var g = new Gasto { Id = Guid.NewGuid(), HogarId = Hogar, Fecha = fecha, Importe = importe, PagadoPor = _ana };
        g.Repartos.Add(new GastoReparto { GastoId = g.Id, HogarId = Hogar, MiembroId = _ana, ImporteAsumido = mitad });
        g.Repartos.Add(new GastoReparto { GastoId = g.Id, HogarId = Hogar, MiembroId = _marcos, ImporteAsumido = mitad });
        _repo.Gastos.Add(g);
    }

    [Fact]
    public async Task CerrarCiclo_AvisaAlOtroMiembroConTotalSinQuienLoCerro()
    {
        _repo.Modo = ModoLiquidacion.Ciclos;
        GastoDeAna(100, new DateOnly(2026, 10, 3));
        GastoDeAna(50, new DateOnly(2026, 10, 5));

        var r = await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        Assert.Null(r.Error);
        var aviso = Assert.Single(_publicador.Publicadas);
        Assert.Equal((Hogar, _ana, "/"), (aviso.HogarId, aviso.ExcluirMiembroId, aviso.Ruta));
        Assert.Equal("Liquidación cerrada", aviso.Titulo);
        Assert.Equal("Ciclo 1 · 150,00 €", aviso.Cuerpo);
    }

    [Fact]
    public async Task CerrarCicloSinGastosPendientes_NoAvisa()
    {
        _repo.Modo = ModoLiquidacion.Ciclos;

        var r = await Servicio.CerrarCicloAsync(Yo, CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Empty(_publicador.Publicadas);
    }

    [Fact]
    public async Task RegistrarPago_AvisaConNombresYSinExcluirA_Nadie()
    {
        GastoDeAna(100, Mes.AddDays(3));
        var pago = new RegistrarPago(Hogar, Mes, _marcos, _ana, 20m, null, null);

        var r = await Servicio.RegistrarPagoAsync(pago, CancellationToken.None);

        Assert.Null(r.Error);
        var aviso = Assert.Single(_publicador.Publicadas);
        Assert.Equal((Hogar, (Guid?)null, "/"), (aviso.HogarId, aviso.ExcluirMiembroId, aviso.Ruta));
        Assert.Equal("Pago registrado", aviso.Titulo);
        Assert.Equal("Marcos pagó 20,00 € a Ana", aviso.Cuerpo);
    }

    [Fact]
    public async Task RegistrarPagoRechazado_NoAvisa()
    {
        GastoDeAna(100, Mes.AddDays(3));
        // Marcos solo debe 50 a Ana: 80 supera la deuda y es conflicto.
        var pago = new RegistrarPago(Hogar, Mes, _marcos, _ana, 80m, null, null);

        var r = await Servicio.RegistrarPagoAsync(pago, CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Empty(_publicador.Publicadas);
    }

    /// <summary>Publicador que guarda los avisos para comprobarlos.</summary>
    private sealed class PublicadorFalso : IPublicadorNotificaciones
    {
        public List<NotificacionPush> Publicadas { get; } = [];

        public void Publicar(NotificacionPush notificacion) => Publicadas.Add(notificacion);
    }
}
