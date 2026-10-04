using Microsoft.EntityFrameworkCore;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Tests;

public class AislamientoHogarTests
{
    private static readonly Guid HogarA = Guid.NewGuid();
    private static readonly Guid HogarB = Guid.NewGuid();

    private static MiParteDbContext Crear(string bd, Guid? hogar)
    {
        var options = new DbContextOptionsBuilder<MiParteDbContext>().UseInMemoryDatabase(bd).Options;
        return new MiParteDbContext(options, new HogarActual { HogarId = hogar });
    }

    private static string Sembrar()
    {
        var bd = Guid.NewGuid().ToString();
        using var ctx = Crear(bd, null);
        ctx.Hogares.AddRange(new Hogar { Id = HogarA, Nombre = "A" }, new Hogar { Id = HogarB, Nombre = "B" });
        ctx.Miembros.AddRange(
            new Miembro { Id = Guid.NewGuid(), HogarId = HogarA, Nombre = "Ana", Tipo = TipoMiembro.Adulto },
            new Miembro { Id = Guid.NewGuid(), HogarId = HogarB, Nombre = "Beto", Tipo = TipoMiembro.Adulto });
        ctx.SaveChanges();
        return bd;
    }

    [Fact]
    public void SoloDevuelveDatosDelHogarActual()
    {
        var bd = Sembrar();
        using var ctx = Crear(bd, HogarA);

        var miembros = ctx.Miembros.ToList();
        var hogares = ctx.Hogares.ToList();

        Assert.Equal("Ana", Assert.Single(miembros).Nombre);
        Assert.Equal("A", Assert.Single(hogares).Nombre);
    }

    [Fact]
    public void SinHogarActual_NoDevuelveNada()
    {
        var bd = Sembrar();
        using var ctx = Crear(bd, null);

        Assert.Empty(ctx.Miembros.ToList());
        Assert.Empty(ctx.Hogares.ToList());
    }
}
