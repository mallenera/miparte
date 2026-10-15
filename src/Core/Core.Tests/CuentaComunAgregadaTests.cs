using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using System.Net;
using Xunit;
using static MiParte.Core.Tests.ApiPostgresTests;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// La cuenta común y el resumen leen el histórico ya agrupado en una sola consulta. Estas pruebas lo comparan con el cálculo
/// de siempre sobre todas las filas, para que agrupar no cambie ningún importe (también con datos de otros meses y de otro hogar).
/// </summary>
public class CuentaComunAgregadaTests
{
    private static DateOnly D(int a, int m, int d) => new(a, m, d);

    private sealed record Datos(
        WebApplicationFactory<Program> F, Guid Hogar, Guid Usuario, Guid Ana, Guid Beto,
        List<AportacionVigente> Aportaciones, List<GastoDeCuenta> Gastos, List<ReembolsoDeCuenta> Reembolsos,
        List<RetiradaDeAhorro> Retiradas, List<DepositoDeAhorro> Depositos);

    private static async Task<Datos> SembrarAsync()
    {
        var f = AutenticacionTests.Crear(Secreto);
        var hogar = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        var otroHogar = Guid.NewGuid();
        var cat = Guid.NewGuid();
        var perfil = Guid.NewGuid();

        var aportaciones = new List<AportacionVigente>
        {
            new(ana, D(2026, 1, 1), 500m, 100m), new(ana, D(2026, 4, 1), 600m, 50m),
            new(beto, D(2026, 2, 1), 300m, 0m), new(beto, D(2026, 9, 1), 400m, 40m),
        };
        // Un gasto de cada clase: adelantado, de la propia cuenta, desde el ahorro, y uno posterior al mes consultado.
        var gastos = new List<GastoDeCuenta>
        {
            new(ana, D(2026, 2, 10), 80m), new(ana, D(2026, 2, 11), 20.50m), new(beto, D(2026, 3, 5), 45m),
            new(null, D(2026, 3, 20), 30m), new(null, D(2026, 5, 2), 200m, true), new(ana, D(2026, 8, 30), 999m),
        };
        var reembolsos = new List<ReembolsoDeCuenta> { new(ana, D(2026, 3, 1), 60m), new(beto, D(2026, 3, 15), 10m), new(ana, D(2026, 9, 1), 5m) };
        var retiradas = new List<RetiradaDeAhorro> { new(D(2026, 5, 3), 70m), new(D(2026, 5, 20), 30m), new(D(2026, 10, 1), 15m) };
        var depositos = new List<DepositoDeAhorro>
        {
            new(D(2026, 1, 15), 1000m, ana), new(D(2026, 3, 2), 250m, beto), new(D(2026, 3, 9), 50m, ana), new(D(2026, 11, 1), 77m, ana),
        };

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.AuditoriaActiva = false;
            foreach (var h in new[] { hogar, otroHogar }) db.Hogares.Add(new Hogar { Id = h, Nombre = "Casa", CuentaComunActiva = true, AhorroActivo = true });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = usuario, Rol = RolMiembro.Admin });
            db.Miembros.Add(new Miembro { Id = beto, HogarId = hogar, Nombre = "Beto", Tipo = TipoMiembro.Adulto });
            foreach (var a in aportaciones)
                db.AportacionesCuenta.Add(new AportacionCuenta { Id = Guid.NewGuid(), HogarId = hogar, MiembroId = a.MiembroId, Desde = a.Desde, Importe = a.Importe, Ahorro = a.Ahorro });
            foreach (var g in gastos)
                db.Gastos.Add(new Gasto
                {
                    Id = Guid.NewGuid(), HogarId = hogar, Fecha = g.Fecha, Importe = g.Importe, CategoriaId = cat, PerfilRepartoId = perfil,
                    PagadoPor = g.PagadoPor, ACargoCuentaComun = true, PagadoDesdeAhorro = g.DesdeAhorro,
                });
            foreach (var r in reembolsos)
                db.ReembolsosCuenta.Add(new ReembolsoCuenta { Id = Guid.NewGuid(), HogarId = hogar, MiembroId = r.MiembroId, Fecha = r.Fecha, Importe = r.Importe });
            foreach (var r in retiradas)
                db.RetiradasAhorro.Add(new RetiradaAhorro { Id = Guid.NewGuid(), HogarId = hogar, MiembroId = ana, Fecha = r.Fecha, Importe = r.Importe });
            foreach (var d in depositos)
                db.DepositosAhorro.Add(new DepositoAhorro { Id = Guid.NewGuid(), HogarId = hogar, MiembroId = d.MiembroId!.Value, Fecha = d.Fecha, Importe = d.Importe });
            // Ruido de otro hogar: no debe entrar en ninguna suma.
            db.ReembolsosCuenta.Add(new ReembolsoCuenta { Id = Guid.NewGuid(), HogarId = otroHogar, MiembroId = ana, Fecha = D(2026, 3, 1), Importe = 12345m });
            db.Gastos.Add(new Gasto
            {
                Id = Guid.NewGuid(), HogarId = otroHogar, Fecha = D(2026, 3, 1), Importe = 777m, CategoriaId = cat, PerfilRepartoId = perfil,
                PagadoPor = ana, ACargoCuentaComun = true,
            });
            await db.SaveChangesAsync();
        }

        return new Datos(f, hogar, usuario, ana, beto, aportaciones, gastos, reembolsos, retiradas, depositos);
    }

    [Theory]
    [InlineData("2026-01")]
    [InlineData("2026-03")]
    [InlineData("2026-05")]
    [InlineData("2026-09")]
    [InlineData("2026-12")]
    public async Task La_cuenta_comun_agrupada_da_los_mismos_importes_que_calcular_sobre_todas_las_filas(string mes)
    {
        var d = await SembrarAsync();
        using var _ = d.F;
        var inicio = DateOnly.ParseExact(mes + "-01", "yyyy-MM-dd");
        var fin = inicio.AddMonths(1);
        var esperado = CuentaComun.Calcular(inicio, d.Aportaciones, d.Gastos, d.Reembolsos, d.Retiradas, d.Depositos);
        var partes = CuentaComun.PartesPorPersona(inicio, d.Aportaciones, esperado, d.Depositos);
        var cliente = Cliente(d.F, Token(Hs256(), d.Usuario), d.Hogar);

        var r = (await cliente.GetFromJsonAsync<CuentaComunResponse>($"/api/cuenta-comun?mes={mes}"))!;

        Assert.Equal(esperado.AportadoMes, r.AportadoMes);
        Assert.Equal(esperado.Aportado, r.Aportado);
        Assert.Equal(esperado.Gastado, r.Gastado);
        Assert.Equal(esperado.Saldo, r.Saldo);
        Assert.Equal(esperado.Efectivo, r.Efectivo);
        Assert.Equal(esperado.AhorroMes, r.AhorroMes);
        Assert.Equal(esperado.AhorroAcumulado, r.AhorroAcumulado);
        Assert.Equal(esperado.AhorroRetirado, r.AhorroRetirado);
        Assert.Equal(esperado.AhorroDisponible, r.AhorroDisponible);
        Assert.Equal(esperado.AhorroDepositado, r.AhorroDepositado);
        Assert.Equal(esperado.AhorroGastado, r.AhorroGastado);
        Assert.Equal(
            esperado.Pendientes.Select(p => (p.MiembroId, p.Importe)).OrderBy(p => p.MiembroId),
            r.Pendientes.Select(p => (p.MiembroId, p.Importe)).OrderBy(p => p.MiembroId));
        Assert.Equal(
            partes.Select(p => (p.MiembroId, p.Aportado, p.Ahorrado, p.ParteSaldo, p.ParteAhorro, p.Pendiente)),
            r.Partes!.Select(p => (p.MiembroId, p.Aportado, p.Ahorrado, p.ParteSaldo, p.ParteAhorro, p.Pendiente)));
        // Las aportaciones llegan todas; el detalle de reembolsos, retiradas y depósitos, solo el del mes.
        Assert.Equal(d.Aportaciones.Count, r.Aportaciones.Count);
        Assert.Equal(d.Reembolsos.Count(x => x.Fecha >= inicio && x.Fecha < fin), r.Reembolsos.Count);
        Assert.Equal(d.Retiradas.Count(x => x.Fecha >= inicio && x.Fecha < fin), r.RetiradasAhorro!.Count);
        Assert.Equal(d.Depositos.Count(x => x.Fecha >= inicio && x.Fecha < fin), r.DepositosAhorro!.Count);
        Assert.True(r.Activa);
        Assert.True(r.AhorroActivo);
    }

    [Theory]
    [InlineData("2026-03")]
    [InlineData("2026-05")]
    [InlineData("2026-12")]
    public async Task El_resumen_agrupado_da_los_mismos_importes_de_cuenta_que_calcular_sobre_todas_las_filas(string mes)
    {
        var d = await SembrarAsync();
        using var _ = d.F;
        var inicio = DateOnly.ParseExact(mes + "-01", "yyyy-MM-dd");
        var fin = inicio.AddMonths(1);
        var estado = CuentaComun.Calcular(inicio, d.Aportaciones, d.Gastos, d.Reembolsos, d.Retiradas, d.Depositos);
        var cliente = Cliente(d.F, Token(Hs256(), d.Usuario), d.Hogar);

        var r = (await cliente.GetFromJsonAsync<ResumenMensualResponse>($"/api/resumen?mes={mes}"))!;

        var cuenta = r.CuentaComun!;
        Assert.Equal(estado.AportadoMes, cuenta.AportadoMes);
        Assert.Equal(d.Gastos.Where(g => g.Fecha >= inicio && g.Fecha < fin).Sum(g => g.Importe), cuenta.GastadoMes);
        Assert.Equal(estado.Saldo, cuenta.Saldo);
        Assert.Equal(estado.Efectivo, cuenta.Efectivo);
        Assert.Equal(estado.Pendientes.Where(p => p.Importe > 0m).Sum(p => p.Importe), cuenta.Pendiente);
        Assert.Equal(estado.AhorroDisponible, cuenta.AhorroDisponible);
        Assert.Equal(estado.AhorroMes, cuenta.AhorroMes);
    }

    /// <summary>La misma comparación contra PostgreSQL real: ahí la consulta agrupada se traduce a una única UNION ALL que InMemory no ejerce.</summary>
    [SkippableTheory]
    [InlineData("2027-01")]
    [InlineData("2027-03")]
    [InlineData("2027-05")]
    [InlineData("2027-12")]
    public async Task En_postgres_la_cuenta_y_el_resumen_agrupados_dan_los_mismos_importes_que_calcular_sobre_todas_las_filas(string mes)
    {
        Skip.If(string.IsNullOrEmpty(Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var user = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(user), "Casa agregada", "Ana");
        var c = e.Cliente(user, hogar.Id);
        async Task Ok(Task<HttpResponseMessage> t) { var r = await t; Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); }

        await Ok(c.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true)));
        await Ok(c.PutAsJsonAsync("/api/cuenta-comun/ahorro/activacion", new ActivarAhorroRequest(true)));
        var ana = (await Leer<List<MiembroDto>>(await c.GetAsync("/api/miembros"))).Single().Id;
        var beto = (await Leer<MiembroDto>(await c.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Beto", "adulto")), HttpStatusCode.Created)).Id;
        var perfil = (await Leer<List<PerfilRepartoDto>>(await c.GetAsync("/api/perfiles"))).Single(p => p.Modo == "cuenta_comun").Id;
        var cat = (await Leer<CategoriaDto>(await c.PostAsJsonAsync("/api/categorias", new GuardarCategoriaRequest("Comunidad", null, null, true)), HttpStatusCode.Created)).Id;

        var aportaciones = new List<AportacionVigente>
        {
            new(ana, D(2027, 1, 1), 500m, 100m), new(ana, D(2027, 3, 1), 600m, 50m), new(beto, D(2027, 2, 1), 300m, 0m),
        };
        foreach (var a in aportaciones)
            await Ok(c.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(a.MiembroId, a.Desde, a.Importe, a.Ahorro)));

        var depositos = new List<DepositoDeAhorro> { new(D(2027, 1, 15), 1000m, ana), new(D(2027, 3, 2), 250m, beto), new(D(2027, 7, 1), 77m, ana) };
        foreach (var d in depositos)
            await Ok(c.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(d.MiembroId!.Value, d.Importe, d.Fecha, null)));

        var gastos = new List<GastoDeCuenta>
        {
            new(ana, D(2027, 2, 10), 80m), new(ana, D(2027, 2, 11), 20.50m), new(beto, D(2027, 3, 5), 45m),
            new(null, D(2027, 3, 20), 30m), new(null, D(2027, 5, 2), 200m, true), new(ana, D(2027, 6, 2), 999m),
        };
        foreach (var g in gastos)
            await Ok(c.PostAsJsonAsync("/api/gastos", new GastoRequest(g.Fecha, g.Importe, cat, g.PagadoPor, perfil, null, g.DesdeAhorro)));

        var reembolsos = new List<ReembolsoDeCuenta> { new(ana, D(2027, 3, 10), 60m), new(beto, D(2027, 3, 15), 10m) };
        foreach (var r in reembolsos)
            await Ok(c.PostAsJsonAsync("/api/cuenta-comun/reembolsos", new CrearReembolsoRequest(r.MiembroId, r.Importe, r.Fecha, null)));

        var retiradas = new List<RetiradaDeAhorro> { new(D(2027, 3, 12), 70m), new(D(2027, 8, 1), 15m) };
        foreach (var r in retiradas)
            await Ok(c.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro", new CrearRetiradaAhorroRequest(ana, r.Importe, r.Fecha, null)));

        var inicio = DateOnly.ParseExact(mes + "-01", "yyyy-MM-dd");
        var fin = inicio.AddMonths(1);
        var esperado = CuentaComun.Calcular(inicio, aportaciones, gastos, reembolsos, retiradas, depositos);
        var partes = CuentaComun.PartesPorPersona(inicio, aportaciones, esperado, depositos);

        var r2 = await Leer<CuentaComunResponse>(await c.GetAsync($"/api/cuenta-comun?mes={mes}"));
        Assert.Equal(esperado.AportadoMes, r2.AportadoMes);
        Assert.Equal(esperado.Aportado, r2.Aportado);
        Assert.Equal(esperado.Gastado, r2.Gastado);
        Assert.Equal(esperado.Saldo, r2.Saldo);
        Assert.Equal(esperado.Efectivo, r2.Efectivo);
        Assert.Equal(esperado.AhorroMes, r2.AhorroMes);
        Assert.Equal(esperado.AhorroAcumulado, r2.AhorroAcumulado);
        Assert.Equal(esperado.AhorroRetirado, r2.AhorroRetirado);
        Assert.Equal(esperado.AhorroDisponible, r2.AhorroDisponible);
        Assert.Equal(esperado.AhorroDepositado, r2.AhorroDepositado);
        Assert.Equal(esperado.AhorroGastado, r2.AhorroGastado);
        Assert.Equal(
            esperado.Pendientes.Select(p => (p.MiembroId, p.Importe)).OrderBy(p => p.MiembroId),
            r2.Pendientes.Select(p => (p.MiembroId, p.Importe)).OrderBy(p => p.MiembroId));
        Assert.Equal(
            partes.Select(p => (p.MiembroId, p.Aportado, p.Ahorrado, p.ParteSaldo, p.ParteAhorro, p.Pendiente)),
            r2.Partes!.Select(p => (p.MiembroId, p.Aportado, p.Ahorrado, p.ParteSaldo, p.ParteAhorro, p.Pendiente)));
        Assert.Equal(aportaciones.Count, r2.Aportaciones.Count);
        Assert.Equal(reembolsos.Count(x => x.Fecha >= inicio && x.Fecha < fin), r2.Reembolsos.Count);
        Assert.Equal(retiradas.Count(x => x.Fecha >= inicio && x.Fecha < fin), r2.RetiradasAhorro!.Count);
        Assert.Equal(depositos.Count(x => x.Fecha >= inicio && x.Fecha < fin), r2.DepositosAhorro!.Count);

        var resumen = (await Leer<ResumenMensualResponse>(await c.GetAsync($"/api/resumen?mes={mes}"))).CuentaComun!;
        Assert.Equal(esperado.Saldo, resumen.Saldo);
        Assert.Equal(esperado.Efectivo, resumen.Efectivo);
        Assert.Equal(esperado.AportadoMes, resumen.AportadoMes);
        Assert.Equal(gastos.Where(g => g.Fecha >= inicio && g.Fecha < fin).Sum(g => g.Importe), resumen.GastadoMes);
        Assert.Equal(esperado.Pendientes.Where(p => p.Importe > 0m).Sum(p => p.Importe), resumen.Pendiente);
        Assert.Equal(esperado.AhorroDisponible, resumen.AhorroDisponible);

        // El endpoint que une resumen y liquidación (lo que usa la pestaña Resumen) da lo mismo.
        var juntas = await Leer<ResumenYLiquidacionResponse>(await c.GetAsync($"/api/resumen-liquidacion?mes={mes}"));
        Assert.Equal(resumen, juntas.Resumen.CuentaComun);
        Assert.Equal(inicio.ToString("yyyy-MM"), juntas.Liquidacion.Mes);
    }
}
