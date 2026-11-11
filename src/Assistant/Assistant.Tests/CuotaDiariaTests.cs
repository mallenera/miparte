using MiParte.Assistant.Api.Seguridad;

namespace MiParte.Assistant.Tests;

/// <summary>Tope diario de preguntas al modelo, por usuario y global, con un reloj manual.</summary>
public class CuotaDiariaTests
{
    private readonly RelojManual _reloj = new(new DateTimeOffset(2026, 7, 15, 23, 0, 0, TimeSpan.Zero));

    private CuotaDiaria Cuota(int porUsuario, int global)
        => new(Ayuda.Opciones(o => { o.PreguntasPorDia = porUsuario; o.PreguntasPorDiaGlobal = global; }), _reloj);

    [Fact]
    public void Consumir_AgotaElCupoDelUsuarioSinAfectarAOtros()
    {
        var cuota = Cuota(porUsuario: 2, global: 100);

        Assert.True(cuota.Consumir("ana"));
        Assert.True(cuota.Consumir("ana"));
        Assert.False(cuota.Consumir("ana"));
        Assert.True(cuota.Consumir("marcos"));
    }

    [Fact]
    public void Consumir_ElTopeGlobalFrenaTambienAUsuariosNuevos()
    {
        var cuota = Cuota(porUsuario: 10, global: 3);

        Assert.True(cuota.Consumir("a"));
        Assert.True(cuota.Consumir("b"));
        Assert.True(cuota.Consumir("c"));
        Assert.False(cuota.Consumir("d"));
    }

    [Fact]
    public void Consumir_UnRechazoNoGastaCupoGlobal()
    {
        var cuota = Cuota(porUsuario: 1, global: 2);

        Assert.True(cuota.Consumir("ana"));
        Assert.False(cuota.Consumir("ana"));
        Assert.False(cuota.Consumir("ana"));
        Assert.True(cuota.Consumir("marcos"));
    }

    [Fact]
    public void Consumir_AlCambiarElDiaUtcSeRecuperaElCupo()
    {
        var cuota = Cuota(porUsuario: 1, global: 1);
        Assert.True(cuota.Consumir("ana"));
        Assert.False(cuota.Consumir("ana"));

        _reloj.Ahora += TimeSpan.FromHours(2); // pasa la medianoche UTC

        Assert.True(cuota.Consumir("ana"));
    }

    private sealed class RelojManual(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }
}
