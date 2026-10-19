using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Tests;

public class RepartoConDependientesTests
{
    private static readonly Guid Ana = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid Nuria = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Lorien = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    private static readonly Guid Truca = Guid.Parse("a0000000-0000-4000-8000-000000000004");

    private static Miembro Adulto(Guid id, string nombre) => new() { Id = id, Nombre = nombre, Tipo = TipoMiembro.Adulto };

    private static Miembro ACargo(Guid id, string nombre, Guid responsable) =>
        new() { Id = id, Nombre = nombre, Tipo = TipoMiembro.ACargo, ResponsableId = responsable };

    private static PerfilReparto Perfil(ModoReparto modo, params (Guid miembro, decimal valor)[] valores) => new()
    {
        Nombre = "Perfil de prueba",
        Modo = modo,
        Detalles = valores.Select(v => new PerfilRepartoDetalle { MiembroId = v.miembro, Valor = v.valor }).ToList(),
    };

    private static decimal Asume(ResultadoReparto r, Guid adulto) => r.PorAdulto.Single(p => p.MiembroId == adulto).Importe;

    [Fact]
    public void Dependiente_CuentaComoParte_PeroSuResponsableAsumeLaSuya()
    {
        // Ana 1, Nuria 1, Lorien (a cargo de Nuria) 1: cada uno asume un tercio de 90 €.
        var miembros = new[] { Adulto(Ana, "Ana"), Adulto(Nuria, "Nuria"), ACargo(Lorien, "Lorien", Nuria) };
        var perfil = Perfil(ModoReparto.Partes, (Ana, 1m), (Nuria, 1m), (Lorien, 1m));

        var r = RepartoGasto.RepartirConDependientes(perfil, miembros, 90m, Ana, out var error)!;

        Assert.Null(error);
        Assert.Equal(30m, Asume(r, Ana));
        Assert.Equal(60m, Asume(r, Nuria));
        var parte = Assert.Single(r.ACargo);
        Assert.Equal(Lorien, parte.MiembroId);
        Assert.Equal(Nuria, parte.ResponsableId);
        Assert.Equal(30m, parte.Importe);
    }

    [Fact]
    public void LaSumaDeLosAdultosSiempreEsElImporte()
    {
        var miembros = new[] { Adulto(Ana, "Ana"), Adulto(Nuria, "Nuria"), ACargo(Lorien, "Lorien", Nuria), ACargo(Truca, "Truca", Ana) };
        var perfil = Perfil(ModoReparto.Partes, (Ana, 1m), (Nuria, 1m), (Lorien, 1m), (Truca, 0.5m));

        var r = RepartoGasto.RepartirConDependientes(perfil, miembros, 57.31m, Nuria, out _)!;

        Assert.Equal(57.31m, r.PorAdulto.Sum(p => p.Importe));
    }

    [Fact]
    public void ConPorcentajes_ElDependienteSeSumaAlResponsable()
    {
        var miembros = new[] { Adulto(Ana, "Ana"), Adulto(Nuria, "Nuria"), ACargo(Lorien, "Lorien", Nuria) };
        var perfil = Perfil(ModoReparto.Porcentaje, (Ana, 60m), (Nuria, 20m), (Lorien, 20m));

        var r = RepartoGasto.RepartirConDependientes(perfil, miembros, 100m, Ana, out _)!;

        Assert.Equal(60m, Asume(r, Ana));
        Assert.Equal(40m, Asume(r, Nuria));
        Assert.Equal(20m, r.ACargo.Single().Importe);
    }

    [Fact]
    public void DependienteSinPeso_NoGeneraDesglose()
    {
        var miembros = new[] { Adulto(Ana, "Ana"), Adulto(Nuria, "Nuria"), ACargo(Lorien, "Lorien", Nuria) };
        var perfil = Perfil(ModoReparto.Partes, (Ana, 1m), (Nuria, 1m));

        var r = RepartoGasto.RepartirConDependientes(perfil, miembros, 80m, Ana, out _)!;

        Assert.Empty(r.ACargo);
        Assert.Equal(40m, Asume(r, Ana));
        Assert.Equal(40m, Asume(r, Nuria));
    }

    [Fact]
    public void DependienteSinResponsableAdultoActivo_Falla()
    {
        var miembros = new[] { Adulto(Ana, "Ana"), ACargo(Lorien, "Lorien", Nuria) };
        var perfil = Perfil(ModoReparto.Partes, (Ana, 1m), (Lorien, 1m));

        var r = RepartoGasto.RepartirConDependientes(perfil, miembros, 10m, Ana, out var error);

        Assert.Null(r);
        Assert.Contains("responsable adulto", error);
    }

    [Fact]
    public void CuentaComun_NoRepartePersonas()
    {
        var miembros = new[] { Adulto(Ana, "Ana"), ACargo(Lorien, "Lorien", Ana) };

        var r = RepartoGasto.RepartirConDependientes(Perfil(ModoReparto.CuentaComun), miembros, 10m, Ana, out _)!;

        Assert.Empty(r.PorAdulto);
        Assert.Empty(r.ACargo);
    }

    [Fact]
    public void RepartirClasico_SigueDandoSoloAdultos()
    {
        var adultos = new[] { Adulto(Ana, "Ana"), Adulto(Nuria, "Nuria") };
        var perfil = Perfil(ModoReparto.Porcentaje, (Ana, 70m), (Nuria, 30m));

        var partes = RepartoGasto.Repartir(perfil, adultos, 100m, Ana, out _)!;

        Assert.Equal(70m, partes.Single(p => p.MiembroId == Ana).Importe);
        Assert.Equal(30m, partes.Single(p => p.MiembroId == Nuria).Importe);
    }
}
