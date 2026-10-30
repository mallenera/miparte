using MiParte.Core.Domain;

namespace MiParte.Core.Tests;

public class PendienteNetoCuentaTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Beto = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void PendienteNeto_RestaSoloLoQueIngresoLaPersona()
    {
        var pendientes = new[] { new PendienteMiembro(Ana, 100m) };
        var ingresos = new[] { new IngresoDeCuenta(D("2026-01-15"), 30, Ana), new IngresoDeCuenta(D("2026-01-16"), 20, Beto) };

        Assert.Equal(70m, CuentaComun.PendienteNeto(pendientes, ingresos, Ana, D("2026-01-01")));
        Assert.Equal(-20m, CuentaComun.PendienteNeto(pendientes, ingresos, Beto, D("2026-01-01")));
    }

    [Fact]
    public void PendienteNeto_IgnoraLosIngresosPosterioresAlMes()
    {
        var pendientes = new[] { new PendienteMiembro(Ana, 100m) };
        var ingresos = new[] { new IngresoDeCuenta(D("2026-02-03"), 30, Ana) };

        Assert.Equal(100m, CuentaComun.PendienteNeto(pendientes, ingresos, Ana, D("2026-01-01")));
    }

    [Fact]
    public void PartesPorPersona_DebeNetoTrasElIngresoDeLaPersona()
    {
        var aportaciones = new[] { new AportacionVigente(Ana, D("2026-01-01"), 100m), new AportacionVigente(Beto, D("2026-01-01"), 100m) };
        var gastos = new[] { new GastoDeCuenta(Ana, D("2026-01-05"), 100m) };
        var ingresos = new[] { new IngresoDeCuenta(D("2026-01-10"), 30m, Ana) };
        var estado = CuentaComun.Calcular(D("2026-01-01"), aportaciones, gastos, [], ingresos: ingresos);

        var partes = CuentaComun.PartesPorPersona(D("2026-01-01"), aportaciones, estado, ingresos: ingresos);

        Assert.Equal(70m, partes.Single(p => p.MiembroId == Ana).Pendiente);
        Assert.Equal(0m, partes.Single(p => p.MiembroId == Beto).Pendiente);
    }
}
