using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

/// <summary>La frecuencia del hogar decide en qué meses cobra cada aportación; el saldo y el ahorro siguen el mismo calendario.</summary>
public class FrecuenciaCuentaComunTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateOnly Enero = new(2026, 1, 1);

    private static AportacionVigente Aportacion(decimal importe, FrecuenciaAportacion frecuencia, decimal ahorro = 0m)
        => new(Ana, Enero, importe, ahorro, frecuencia);

    [Fact]
    public void Mensual_ReproduceElComportamientoDeSiempre()
    {
        var aportaciones = new[] { Aportacion(100m, FrecuenciaAportacion.Mensual) };

        var febrero = CuentaComun.Calcular(new DateOnly(2026, 2, 1), aportaciones, [], []);

        Assert.Equal(100m, febrero.AportadoMes);
        Assert.Equal(200m, febrero.Aportado);
    }

    [Fact]
    public void Bimestral_CobraSoloEnElPrimerMesDelPeriodo()
    {
        var aportaciones = new[] { Aportacion(100m, FrecuenciaAportacion.Bimestral) };

        Assert.Equal(100m, CuentaComun.Calcular(Enero, aportaciones, [], []).AportadoMes);
        var febrero = CuentaComun.Calcular(new DateOnly(2026, 2, 1), aportaciones, [], []);
        Assert.Equal(0m, febrero.AportadoMes);
        Assert.Equal(100m, febrero.Aportado);
        Assert.Equal(200m, CuentaComun.Calcular(new DateOnly(2026, 4, 1), aportaciones, [], []).Aportado);
    }

    [Fact]
    public void Unica_CobraUnaVezAunquePasenMeses()
    {
        var aportaciones = new[] { Aportacion(300m, FrecuenciaAportacion.Unica) };

        var junio = CuentaComun.Calcular(new DateOnly(2026, 6, 1), aportaciones, [], []);

        Assert.Equal(300m, junio.Aportado);
        Assert.Equal(300m, junio.Saldo);
        Assert.Equal(0m, CuentaComun.Calcular(new DateOnly(2026, 2, 1), aportaciones, [], []).AportadoMes);
    }

    [Fact]
    public void Bimestral_ApartaAhorroSoloEnElMesDeCobro()
    {
        var aportaciones = new[] { Aportacion(100m, FrecuenciaAportacion.Bimestral, ahorro: 40m) };

        var enero = CuentaComun.Calcular(Enero, aportaciones, [], []);

        Assert.Equal(40m, enero.AhorroAcumulado);
        Assert.Equal(60m, enero.Saldo);
        Assert.Equal(40m, CuentaComun.Calcular(new DateOnly(2026, 2, 1), aportaciones, [], []).AhorroAcumulado);
    }

    [Fact]
    public void Extracto_UnaAportacionBimestralNoEntraEnElMesQueNoCobra()
    {
        var aportaciones = new[] { Aportacion(100m, FrecuenciaAportacion.Bimestral) };
        var febrero = new DateOnly(2026, 2, 1);
        var estado = CuentaComun.Calcular(febrero, aportaciones, [], []);

        var extracto = CuentaComun.Extracto(febrero, aportaciones, [], [], null, estado);

        Assert.Equal(0m, extracto.Aportaciones);
        Assert.Equal(100m, extracto.EfectivoInicial);
    }
}
