using MiParte.Core.Application;
using MiParte.Core.Application.Periodos;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

/// <summary>Cierre y reapertura del periodo propio, con repositorios en memoria.</summary>
public class ServicioPeriodosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid HogarId = Guid.NewGuid();

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class PeriodosFalso : IRepositorioPeriodos
    {
        public Hogar? Hogar { get; set; }
        public int Guardados { get; private set; }

        public Task<Hogar?> HogarAsync(CancellationToken ct) => Task.FromResult(Hogar);

        public Task GuardarAsync(CancellationToken ct)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    private static (ServicioPeriodos Servicio, PeriodosFalso Periodos, ServicioLiquidacionTests.RepositorioFalso Liquidacion) Montar(Hogar? hogar)
    {
        var periodos = new PeriodosFalso { Hogar = hogar };
        var liquidacion = new ServicioLiquidacionTests.RepositorioFalso();
        return (new ServicioPeriodos(periodos, liquidacion, new RelojFijo(Ahora)), periodos, liquidacion);
    }

    private static Hogar HogarConPeriodo(Periodo? periodo) => new() { Id = HogarId, Nombre = "Viaje", PeriodoPropio = periodo };

    private static Miembro Yo() => new() { Id = Guid.NewGuid(), HogarId = HogarId, Nombre = "Ana", Tipo = TipoMiembro.Adulto };

    private static Gasto Pendiente(decimal importe, DateOnly fecha) => new() { Id = Guid.NewGuid(), HogarId = HogarId, Importe = importe, Fecha = fecha };

    [Fact]
    public async Task CerrarAbierto_CongelaElPeriodoYLiquidaLosPendientesEnUnCiclo()
    {
        var (servicio, periodos, liquidacion) = Montar(HogarConPeriodo(Periodo.Abierto(new DateOnly(2026, 3, 1))));
        liquidacion.Gastos.Add(Pendiente(100m, new DateOnly(2026, 3, 2)));
        liquidacion.Gastos.Add(Pendiente(40m, new DateOnly(2026, 3, 9)));

        var r = await servicio.CerrarAsync(Yo(), CancellationToken.None);

        Assert.Null(r.Error);
        Assert.Equal("2026-03-01/2026-03-15", r.Valor!.Periodo.ATexto());
        Assert.Equal(Ahora, periodos.Hogar!.PeriodoCerradoEn);
        Assert.Equal(140m, r.Valor.Ciclo!.Ciclo.Total);
        Assert.Equal(1, r.Valor.Ciclo.Ciclo.Numero);
        Assert.All(liquidacion.Gastos, g => Assert.NotNull(g.CicloLiquidacionId));
    }

    [Fact]
    public async Task CerrarRango_ConservaSuFinYSinPendientesNoCreaCiclo()
    {
        var (servicio, periodos, liquidacion) = Montar(HogarConPeriodo(Periodo.Rango(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))));

        var r = await servicio.CerrarAsync(Yo(), CancellationToken.None);

        Assert.Null(r.Error);
        Assert.Equal("2026-03-01/2026-03-31", r.Valor!.Periodo.ATexto());
        Assert.Null(r.Valor.Ciclo);
        Assert.Empty(liquidacion.Ciclos);
        Assert.Equal(1, periodos.Guardados);
    }

    [Fact]
    public async Task CerrarDosVeces_LaSegundaEsConflicto()
    {
        var (servicio, _, _) = Montar(HogarConPeriodo(Periodo.Abierto(new DateOnly(2026, 3, 1))));
        Assert.Null((await servicio.CerrarAsync(Yo(), CancellationToken.None)).Error);

        var segunda = await servicio.CerrarAsync(Yo(), CancellationToken.None);

        Assert.Equal(TipoError.Conflicto, segunda.Error!.Tipo);
        Assert.Equal(Mensajes.PeriodoYaCerrado, segunda.Error.Mensaje);
    }

    [Fact]
    public async Task SinPeriodoPropio_EsInvalido()
    {
        var (servicio, _, _) = Montar(HogarConPeriodo(null));

        var r = await servicio.CerrarAsync(Yo(), CancellationToken.None);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
    }

    [Fact]
    public async Task PeriodoQueNoHaEmpezado_EsInvalidoYNoCambiaElHogar()
    {
        var (servicio, periodos, _) = Montar(HogarConPeriodo(Periodo.Abierto(new DateOnly(2026, 4, 1))));

        var r = await servicio.CerrarAsync(Yo(), CancellationToken.None);

        Assert.Equal(TipoError.Invalido, r.Error!.Tipo);
        Assert.Null(periodos.Hogar!.PeriodoCerradoEn);
    }

    [Fact]
    public async Task ReabrirUnPeriodoCerrado_LoDejaEditable()
    {
        var hogar = HogarConPeriodo(Periodo.Rango(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15)));
        hogar.PeriodoCerradoEn = Ahora;
        var (servicio, periodos, _) = Montar(hogar);

        var r = await servicio.ReabrirAsync(CancellationToken.None);

        Assert.Null(r.Error);
        Assert.Null(periodos.Hogar!.PeriodoCerradoEn);
        Assert.Equal("2026-03-01/2026-03-15", periodos.Hogar.PeriodoPropio!.ATexto());
    }

    [Fact]
    public async Task ReabrirUnPeriodoAbierto_NoEncontrado()
    {
        var (servicio, _, _) = Montar(HogarConPeriodo(Periodo.Abierto(new DateOnly(2026, 3, 1))));

        var r = await servicio.ReabrirAsync(CancellationToken.None);

        Assert.Equal(TipoError.NoEncontrado, r.Error!.Tipo);
    }
}
