using MiParte.Contracts;
using MiParte.Web.Reparto;

namespace MiParte.Web.Tests;

/// <summary>Lógica de presentación extraída de las vistas, probada sin renderizar componentes.</summary>
public class EntradaImporteTests
{
    [Theory]
    [InlineData("12", 12)]
    [InlineData("12,5", 12.5)]
    [InlineData("12.50", 12.5)]
    [InlineData("  0,01 ", 0.01)]
    public void TryParse_AceptaComaOPuntoConComoMucho2Decimales(string texto, double esperado)
    {
        Assert.True(EntradaImporte.TryParse(texto, out var valor));
        Assert.Equal((decimal)esperado, valor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10,123")]
    public void TryParse_RechazaVacioNoNumericoCeroNegativoOMasDe2Decimales(string texto)
        => Assert.False(EntradaImporte.TryParse(texto, out _));

    [Fact]
    public void TryParse_ConPermitirCero_AceptaCeroPeroNoNegativos()
    {
        Assert.True(EntradaImporte.TryParse("0", permitirCero: true, out var v));
        Assert.Equal(0m, v);
        Assert.False(EntradaImporte.TryParse("-1", permitirCero: true, out _));
    }

    [Fact]
    public void TryParse_Nulo_EsInvalido() => Assert.False(EntradaImporte.TryParse(null, out _));
}

public class ListaGastosTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Marcos = Guid.NewGuid(), Casa = Guid.NewGuid(), Ocio = Guid.NewGuid();

    private static GastoResponse Gasto(
        decimal importe, Guid categoria, Guid? paga, DateOnly? fecha = null, bool personal = false)
        => new(Guid.NewGuid(), fecha ?? new DateOnly(2026, 9, 1), importe, categoria, paga, Guid.NewGuid(), null, null, [], false, false, personal);

    [Fact]
    public void Filtrar_PorCategoriasIncluyeSusDescendientes()
    {
        var hija = Guid.NewGuid();
        var gastos = new[] { Gasto(10, Casa, Ana), Gasto(20, hija, Ana), Gasto(30, Ocio, Ana) };

        var r = ListaGastos.Filtrar(gastos, new FiltroLista(Categorias: new HashSet<Guid> { Casa, hija }));

        Assert.Equal([10m, 20m], r.Select(g => g.Importe).Order());
    }

    [Fact]
    public void Filtrar_PorPagadorNoIncluyeLoQueSoloAsume()
    {
        var soloAsume = Gasto(10, Casa, Marcos) with { Repartos = [new RepartoGastoDto(Ana, 5m)] };
        var gastos = new[] { Gasto(20, Casa, Ana), soloAsume };

        var r = ListaGastos.Filtrar(gastos, new FiltroLista(Paga: Ana));

        Assert.Equal(20m, Assert.Single(r).Importe);
    }

    [Fact]
    public void Filtrar_SoloCuenta_DejaLosQueNoTienenPagador()
    {
        var gastos = new[] { Gasto(10, Casa, null), Gasto(20, Casa, Ana) };

        var r = ListaGastos.Filtrar(gastos, new FiltroLista(SoloCuenta: true));

        Assert.Equal(10m, Assert.Single(r).Importe);
    }

    [Fact]
    public void Filtrar_OrdenaDelMasRecienteAlMasAntiguo()
    {
        var viejo = Gasto(1, Casa, Ana, new DateOnly(2026, 9, 1));
        var nuevo = Gasto(2, Casa, Ana, new DateOnly(2026, 9, 20));

        var r = ListaGastos.Filtrar([viejo, nuevo], new FiltroLista());

        Assert.Equal([nuevo.Id, viejo.Id], r.Select(g => g.Id));
    }

    [Fact]
    public void Totales_SeparanLosPersonalesDelTotalDelHogar()
    {
        var t = ListaGastos.Totales([Gasto(10, Casa, Ana), Gasto(5, Casa, Ana), Gasto(7, Casa, Ana, personal: true)]);

        Assert.Equal((15m, 2, 7m, true), (t.TotalHogar, t.CuantosHogar, t.TotalPersonales, t.HayPersonales));
    }

    [Fact]
    public void Totales_SinGastos_EstanAcero()
    {
        var t = ListaGastos.Totales([]);

        Assert.Equal((0m, 0, 0m, false), (t.TotalHogar, t.CuantosHogar, t.TotalPersonales, t.HayPersonales));
    }

    [Fact]
    public void PerfilesOfrecidos_SinCuentaActiva_OcultaElDeCuentaComun()
    {
        var cuenta = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta", ModosReparto.CuentaComun, []);
        var partes = new PerfilRepartoDto(Guid.NewGuid(), "Partes", ModosReparto.Partes, []);

        var r = ListaGastos.PerfilesOfrecidos([cuenta, partes], cuentaActiva: false, perfilEnEdicion: null);

        Assert.Equal([partes], r);
    }

    [Fact]
    public void PerfilesOfrecidos_AlEditarUnGastoConPerfilNoOfrecido_LoMantieneComoOpcion()
    {
        var cuenta = new PerfilRepartoDto(Guid.NewGuid(), "Cuenta", ModosReparto.CuentaComun, []);
        var partes = new PerfilRepartoDto(Guid.NewGuid(), "Partes", ModosReparto.Partes, []);

        var r = ListaGastos.PerfilesOfrecidos([cuenta, partes], cuentaActiva: false, perfilEnEdicion: cuenta.Id);

        Assert.Contains(cuenta, r);
    }
}

public class GraficoGastosTests
{
    private static GastoPorCategoria C(string nombre, decimal importe, int gastos = 1) => new(Guid.NewGuid(), nombre, importe, gastos);

    [Fact]
    public void AgruparResto_ConPocasCategorias_NoCambiaNada()
    {
        var todas = new[] { C("A", 3), C("B", 2) };

        Assert.Same(todas, GraficoGastos.AgruparResto(todas, 7, null));
    }

    [Fact]
    public void AgruparResto_AgrupaElSobranteEnOtrasCategorias()
    {
        var todas = Enumerable.Range(1, 10).Select(i => C($"C{i}", 100 - i, gastos: 2)).ToList();

        var r = GraficoGastos.AgruparResto(todas, 7, null);

        Assert.Equal(7, r.Count);
        var otras = r[^1];
        Assert.Equal((Guid.Empty, GraficoGastos.EtiquetaOtras), (otras.CategoriaId, otras.Nombre));
        Assert.Equal(todas.Skip(6).Sum(c => c.Importe), otras.Importe);
        Assert.Equal(8, otras.Gastos);
        Assert.Equal(todas.Sum(c => c.Importe), r.Sum(c => c.Importe));
    }

    [Fact]
    public void AgruparResto_LaCategoriaElegidaSiempreSeVe()
    {
        var todas = Enumerable.Range(1, 10).Select(i => C($"C{i}", 100 - i)).ToList();
        var elegida = todas[9];

        var r = GraficoGastos.AgruparResto(todas, 7, elegida.CategoriaId);

        Assert.Contains(elegida, r);
        Assert.Equal(todas.Sum(c => c.Importe), r.Sum(c => c.Importe));
    }

    [Theory]
    [InlineData(25, 100, 25)]
    [InlineData(1, 3, 33.33)]
    [InlineData(5, 0, 0)]
    public void PorCiento_DevuelveElPorcentajeOCeroSinTotal(double parte, double total, double esperado)
        => Assert.Equal((decimal)esperado, Math.Round(GraficoGastos.PorCiento((decimal)parte, (decimal)total), 2));
}
