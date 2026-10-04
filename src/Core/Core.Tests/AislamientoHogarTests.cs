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
        var miembroA = Guid.NewGuid();
        var miembroA2 = Guid.NewGuid();
        var miembroB = Guid.NewGuid();
        var miembroB2 = Guid.NewGuid();
        ctx.PagosLiquidacion.AddRange(
            new PagoLiquidacionRegistro { Id = Guid.NewGuid(), HogarId = HogarA, Mes = new DateOnly(2026, 9, 1), DeMiembroId = miembroA, AMiembroId = miembroA2, Importe = 10m, Fecha = new DateOnly(2026, 10, 1), Concepto = "pago A" },
            new PagoLiquidacionRegistro { Id = Guid.NewGuid(), HogarId = HogarB, Mes = new DateOnly(2026, 9, 1), DeMiembroId = miembroB, AMiembroId = miembroB2, Importe = 20m, Fecha = new DateOnly(2026, 10, 1), Concepto = "pago B" });
        ctx.Invitaciones.AddRange(
            new InvitacionHogar { Id = Guid.NewGuid(), HogarId = HogarA, TokenHash = new string('a', 64), CreadaPor = Guid.NewGuid(), CreadaEn = DateTimeOffset.UtcNow, CaducaEn = DateTimeOffset.UtcNow.AddDays(7) },
            new InvitacionHogar { Id = Guid.NewGuid(), HogarId = HogarB, TokenHash = new string('b', 64), CreadaPor = Guid.NewGuid(), CreadaEn = DateTimeOffset.UtcNow, CaducaEn = DateTimeOffset.UtcNow.AddDays(7) });
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
        Assert.Empty(ctx.PagosLiquidacion.ToList());
        Assert.Empty(ctx.Invitaciones.ToList());
    }

    [Fact]
    public void PagosEInvitaciones_SoloDelHogarActual()
    {
        var bd = Sembrar();
        using var ctx = Crear(bd, HogarA);

        Assert.Equal("pago A", Assert.Single(ctx.PagosLiquidacion.ToList()).Concepto);
        Assert.Equal(new string('a', 64), Assert.Single(ctx.Invitaciones.ToList()).TokenHash);
    }

    [Fact]
    public void TodaEntidadConHogarId_TieneQueryFilter()
    {
        using var ctx = Crear(Guid.NewGuid().ToString(), null);
        var sinFiltro = ctx.Model.GetEntityTypes()
            .Where(t => t.FindProperty("HogarId") is not null && t.GetQueryFilter() is null)
            .Select(t => t.ClrType.Name)
            .ToList();
        Assert.Empty(sinFiltro);
        Assert.Contains(ctx.Model.GetEntityTypes(), t => t.ClrType == typeof(PagoLiquidacionRegistro));
        Assert.Contains(ctx.Model.GetEntityTypes(), t => t.ClrType == typeof(InvitacionHogar));
    }

    [Fact]
    public void RolMiembro_SeGuardaComoTexto()
    {
        using var ctx = Crear(Guid.NewGuid().ToString(), null);
        var prop = ctx.Model.FindEntityType(typeof(Miembro))!.FindProperty(nameof(Miembro.Rol))!;
        var conv = prop.GetValueConverter()!;
        Assert.Equal("admin", conv.ConvertToProvider(RolMiembro.Admin));
        Assert.Equal("miembro", conv.ConvertToProvider(RolMiembro.Miembro));
        Assert.Equal(RolMiembro.Admin, conv.ConvertFromProvider("admin"));
    }
}
