using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class GastosLiquidacionTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Escenario(
        HttpClient Cliente, WebApplicationFactory<Program> F, Guid Hogar, Guid Ana, Guid? Beto, Guid Categoria,
        Guid Perfil5050, Guid Perfil6040, Guid PerfilCuentaComun, Guid PerfilIndividual);

    private static async Task<Escenario> Montar(bool conBeto = true)
    {
        var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var hogar = Guid.NewGuid();
        var ana = Guid.NewGuid();
        Guid? beto = conBeto ? Guid.NewGuid() : null;
        var cat = Guid.NewGuid();
        var p5050 = Guid.NewGuid();
        var p6040 = Guid.NewGuid();
        var pIng = Guid.NewGuid();
        var pInd = Guid.NewGuid();

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa" });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = user });
            if (beto is not null)
                db.Miembros.Add(new Miembro { Id = beto.Value, HogarId = hogar, Nombre = "Beto", Tipo = TipoMiembro.Adulto });
            db.Categorias.Add(new Categoria { Id = cat, HogarId = hogar, Nombre = "Comida" });

            PerfilReparto Perfil(Guid id, string n, ModoReparto m, decimal va, decimal vb)
            {
                var p = new PerfilReparto { Id = id, HogarId = hogar, Nombre = n, Modo = m };
                if (m is ModoReparto.Porcentaje or ModoReparto.Partes)
                {
                    p.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = hogar, PerfilId = id, MiembroId = ana, Valor = va });
                    if (beto is not null)
                        p.Detalles.Add(new PerfilRepartoDetalle { Id = Guid.NewGuid(), HogarId = hogar, PerfilId = id, MiembroId = beto.Value, Valor = vb });
                }
                return p;
            }
            db.PerfilesReparto.AddRange(
                Perfil(p5050, "50/50", ModoReparto.Porcentaje, 50, 50),
                Perfil(p6040, "60/40", ModoReparto.Porcentaje, 60, 40),
                Perfil(pIng, "Cuenta común", ModoReparto.CuentaComun, 0, 0),
                Perfil(pInd, "Individual", ModoReparto.Individual, 0, 0));
            await db.SaveChangesAsync();
        }

        return new Escenario(Cliente(f, Token(Hs256(), user)), f, hogar, ana, beto, cat, p5050, p6040, pIng, pInd);
    }

    private static GastoRequest Gasto(Escenario e, decimal importe, Guid perfil, string fecha = "2026-09-10", Guid? pagador = null)
        => new(DateOnly.Parse(fecha), importe, e.Categoria, pagador ?? e.Ana, perfil, "Compra");

    private static async Task<GastoResponse> CrearGasto(Escenario e, GastoRequest r)
    {
        var resp = await e.Cliente.PostAsJsonAsync("/api/gastos", r);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<GastoResponse>(Web))!;
    }

    private static async Task<T> Leer<T>(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<T>(Web))!;
    }

    [Fact]
    public async Task Gasto_GuardaRepartoQueSumaElImporte()
    {
        var e = await Montar();
        var g = await CrearGasto(e, Gasto(e, 33.33m, e.Perfil6040));

        Assert.Equal(2, g.Repartos.Count);
        Assert.Equal(33.33m, g.Repartos.Sum(r => r.ImporteAsumido));
        Assert.Equal(20.00m, g.Repartos.Single(r => r.MiembroId == e.Ana).ImporteAsumido);

        // Queda persistido (GET devuelve lo guardado).
        var leido = await Leer<GastoResponse>(await e.Cliente.GetAsync($"/api/gastos/{g.Id}"));
        Assert.Equal(33.33m, leido.Repartos.Sum(r => r.ImporteAsumido));
    }

    [Fact]
    public async Task Gasto_CuentaComun_NoSeRepartePorPersonasNiGeneraDeuda()
    {
        var e = await Montar();
        var g = await CrearGasto(e, Gasto(e, 100m, e.PerfilCuentaComun)); // lo paga Ana, lo asume la cuenta común

        Assert.True(g.ACargoCuentaComun);
        Assert.Empty(g.Repartos);
        var leido = await Leer<GastoResponse>(await e.Cliente.GetAsync($"/api/gastos/{g.Id}"));
        Assert.True(leido.ACargoCuentaComun);

        var liq = await Leer<LiquidacionResponse>(await e.Cliente.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.All(liq.Saldos, s => Assert.Equal(0m, s.Saldo));
        Assert.Empty(liq.Transferencias);
        var resumen = await Leer<ResumenMensualResponse>(await e.Cliente.GetAsync("/api/resumen?mes=2026-09"));
        Assert.Equal(100m, resumen.GastosTotales);
        Assert.All(resumen.Miembros, m => Assert.Equal((0m, 0m), (m.Pagado, m.Asumido)));
    }

    [Fact]
    public async Task Gasto_CuentaComun_AlEditarAOtroPerfilVuelveARepartirse()
    {
        var e = await Montar();
        var g = await CrearGasto(e, Gasto(e, 100m, e.PerfilCuentaComun));

        var r = await e.Cliente.PutAsJsonAsync($"/api/gastos/{g.Id}", Gasto(e, 100m, e.Perfil6040));
        var editado = await Leer<GastoResponse>(r);

        Assert.False(editado.ACargoCuentaComun);
        Assert.Equal(60m, editado.Repartos.Single(x => x.MiembroId == e.Ana).ImporteAsumido);
    }

    [Fact]
    public async Task HogarDeUnAdulto_TodoParaEl_YSinTransferencias()
    {
        var e = await Montar(conBeto: false);
        var g = await CrearGasto(e, Gasto(e, 10m, e.Perfil6040));

        var parte = Assert.Single(g.Repartos);
        Assert.Equal((e.Ana, 10m), (parte.MiembroId, parte.ImporteAsumido));

        var liq = await Leer<LiquidacionResponse>(await e.Cliente.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.Empty(liq.Transferencias);
        Assert.Equal(0m, Assert.Single(liq.Saldos).Saldo);
    }

    [Fact]
    public async Task EditarPerfil_NoRecalculaElHistorico_PeroPutRecalculaSoloEseGasto()
    {
        var e = await Montar();
        var g1 = await CrearGasto(e, Gasto(e, 100m, e.Perfil6040));
        var g2 = await CrearGasto(e, Gasto(e, 50m, e.Perfil6040));

        using (var scope = e.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            var detalle = await db.PerfilesRepartoDetalle.IgnoreQueryFilters()
                .SingleAsync(d => d.PerfilId == e.Perfil6040 && d.MiembroId == e.Ana);
            detalle.Valor = 10; // el perfil cambia
            await db.SaveChangesAsync();
        }

        var intacto = await Leer<GastoResponse>(await e.Cliente.GetAsync($"/api/gastos/{g1.Id}"));
        Assert.Equal(60m, intacto.Repartos.Single(r => r.MiembroId == e.Ana).ImporteAsumido);

        var put = await e.Cliente.PutAsJsonAsync($"/api/gastos/{g2.Id}", Gasto(e, 50m, e.Perfil6040));
        var recalculado = await Leer<GastoResponse>(put);
        Assert.Equal(10m, recalculado.Repartos.Single(r => r.MiembroId == e.Ana).ImporteAsumido); // 10/(10+40)
        Assert.Equal(50m, recalculado.Repartos.Sum(r => r.ImporteAsumido));

        var otro = await Leer<GastoResponse>(await e.Cliente.GetAsync($"/api/gastos/{g1.Id}"));
        Assert.Equal(60m, otro.Repartos.Single(r => r.MiembroId == e.Ana).ImporteAsumido);
    }

    [Fact]
    public async Task Gasto_Validaciones_400_404()
    {
        var e = await Montar();
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 0m, e.Perfil5050))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 1.234m, e.Perfil5050))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 5m, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 5m, e.Perfil5050) with { CategoriaId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 5m, e.Perfil5050, pagador: Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.GetAsync("/api/gastos?mes=2026-13")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Cliente.PutAsJsonAsync($"/api/gastos/{Guid.NewGuid()}", Gasto(e, 5m, e.Perfil5050))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Cliente.DeleteAsync($"/api/gastos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(e.F, null).GetAsync("/api/gastos")).StatusCode);
    }

    [Fact]
    public async Task Gastos_FiltrosPorMesYCategoria_YBorrado()
    {
        var e = await Montar();
        var sep = await CrearGasto(e, Gasto(e, 10m, e.Perfil5050, "2026-09-05"));
        await CrearGasto(e, Gasto(e, 20m, e.Perfil5050, "2026-10-05"));

        var deSep = await Leer<List<GastoResponse>>(await e.Cliente.GetAsync("/api/gastos?mes=2026-09"));
        Assert.Equal(sep.Id, Assert.Single(deSep).Id);
        Assert.Empty(await Leer<List<GastoResponse>>(await e.Cliente.GetAsync($"/api/gastos?categoriaId={Guid.NewGuid()}")));
        Assert.Equal(2, (await Leer<List<GastoResponse>>(await e.Cliente.GetAsync($"/api/gastos?categoriaId={e.Categoria}"))).Count);

        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/gastos/{sep.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Cliente.GetAsync($"/api/gastos/{sep.Id}")).StatusCode);
        using var scope = e.F.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<MiParteDbContext>().GastosReparto.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Recurrentes_Generar_EsIdempotente_YNoDuplica()
    {
        var e = await Montar();
        var c = e.Cliente;
        var body = new GastoRecurrenteRequest(80m, e.Categoria, e.Ana, e.Perfil5050, 5, "Luz");
        var r = await c.PostAsJsonAsync("/api/gastos-recurrentes", body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var rec = (await r.Content.ReadFromJsonAsync<GastoRecurrenteResponse>(Web))!;
        await c.PostAsJsonAsync("/api/gastos-recurrentes", body with { Activo = false, Concepto = "Inactivo" });

        var primera = await Leer<GenerarRecurrentesResponse>(await c.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null));
        var segunda = await Leer<GenerarRecurrentesResponse>(await c.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null));

        Assert.Equal((1, 0), (primera.Creados, primera.YaExistentes));
        Assert.Equal((0, 1), (segunda.Creados, segunda.YaExistentes));
        var gastos = await Leer<List<GastoResponse>>(await c.GetAsync("/api/gastos?mes=2026-09"));
        var g = Assert.Single(gastos);
        Assert.Equal(rec.Id, g.GastoRecurrenteId);
        Assert.Equal(new DateOnly(2026, 9, 5), g.Fecha);
        Assert.Equal(80m, g.Repartos.Sum(x => x.ImporteAsumido));

        // Otro mes genera uno nuevo.
        Assert.Equal(1, (await Leer<GenerarRecurrentesResponse>(await c.PostAsync("/api/gastos-recurrentes/generar?mes=2026-10", null))).Creados);

        // Con gastos generados no se puede borrar (409); se desactiva.
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/gastos-recurrentes/{rec.Id}")).StatusCode);
    }

    [Fact]
    public async Task Recurrentes_Validaciones()
    {
        var e = await Montar();
        var c = e.Cliente;
        var ok = new GastoRecurrenteRequest(80m, e.Categoria, e.Ana, e.Perfil5050, 5, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/gastos-recurrentes", ok with { DiaMes = 29 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/gastos-recurrentes", ok with { DiaMes = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/gastos-recurrentes", ok with { Importe = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/gastos-recurrentes/generar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/gastos-recurrentes/{Guid.NewGuid()}")).StatusCode);

        var creada = await c.PostAsJsonAsync("/api/gastos-recurrentes", ok);
        var rec = (await creada.Content.ReadFromJsonAsync<GastoRecurrenteResponse>(Web))!;
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/gastos-recurrentes/{rec.Id}")).StatusCode);
    }

    [Fact]
    public async Task Liquidacion_ConPagos_DescuentaYValidaElPago()
    {
        var e = await Montar();
        var c = e.Cliente;
        var beto = e.Beto!.Value;
        await CrearGasto(e, Gasto(e, 100m, e.Perfil5050)); // Ana paga 100, cada uno asume 50

        var liq = await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.Equal(50m, liq.Saldos.Single(s => s.MiembroId == e.Ana).Saldo);
        Assert.Equal(-50m, liq.Saldos.Single(s => s.MiembroId == beto).Saldo);
        var t = Assert.Single(liq.Transferencias);
        Assert.Equal((beto, e.Ana, 50m), (t.De, t.A, t.Importe));

        var mes = new DateOnly(2026, 9, 1);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 20m, null, "Bizum"))).StatusCode);

        liq = await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.Equal(-30m, liq.Saldos.Single(s => s.MiembroId == beto).Saldo);
        Assert.Equal(30m, Assert.Single(liq.Transferencias).Importe);
        Assert.Single(liq.Pagos);

        // Supera la deuda pendiente (30), mes que no es día 1, mismo miembro, importe <= 0.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 30.01m, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(mes, e.Ana, beto, 1m, null, null))).StatusCode); // sentido contrario
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(new DateOnly(2026, 9, 15), beto, e.Ana, 1m, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(mes, beto, beto, 1m, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 0m, null, null))).StatusCode);

        // Saldar el resto deja la liquidación a cero; borrar el pago la reabre.
        var final = await c.PostAsJsonAsync("/api/pagos-liquidacion", new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 30m, null, null));
        Assert.Equal(HttpStatusCode.Created, final.StatusCode);
        var pago = (await final.Content.ReadFromJsonAsync<PagoLiquidacionDto>(Web))!;
        Assert.Empty((await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"))).Transferencias);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pagos-liquidacion/{pago.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/pagos-liquidacion/{pago.Id}")).StatusCode);
        Assert.Equal(30m, Assert.Single((await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"))).Transferencias).Importe);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/liquidacion?mes=2026-9")).StatusCode);
    }

    [Fact]
    public async Task Pago_SinGastosEnElMes_SoloValidaImportePositivo()
    {
        var e = await Montar();
        var r = await e.Cliente.PostAsJsonAsync("/api/pagos-liquidacion",
            new CrearPagoLiquidacionRequest(new DateOnly(2026, 8, 1), e.Beto!.Value, e.Ana, 10m, null, null));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Fact]
    public async Task Resumen_TotalesPorMiembroYCategoria()
    {
        var e = await Montar();
        var c = e.Cliente;
        await CrearGasto(e, Gasto(e, 100m, e.Perfil6040)); // Ana paga; 60/40
        await CrearGasto(e, Gasto(e, 50m, e.Perfil5050, pagador: e.Beto.Value));
        await CrearGasto(e, Gasto(e, 999m, e.Perfil5050, "2026-10-01")); // otro mes

        var r = await Leer<ResumenMensualResponse>(await c.GetAsync("/api/resumen?mes=2026-09"));

        Assert.Equal(150m, r.GastosTotales);
        var ana = r.Miembros.Single(m => m.MiembroId == e.Ana);
        Assert.Equal((100m, 85m), (ana.Pagado, ana.Asumido));
        var beto = r.Miembros.Single(m => m.MiembroId == e.Beto.Value);
        Assert.Equal((50m, 65m), (beto.Pagado, beto.Asumido));
        var cat = Assert.Single(r.Categorias);
        Assert.Equal((e.Categoria, 150m), (cat.CategoriaId, cat.Total));
        Assert.Equal(150m, cat.PorMiembro.Sum(x => x.Importe));
        Assert.Equal(85m, cat.PorMiembro.Single(x => x.MiembroId == e.Ana).Importe);
    }

    [Fact]
    public async Task AislamientoEntreHogares_NoVeGastosAjenos()
    {
        var e = await Montar();
        var g = await CrearGasto(e, Gasto(e, 10m, e.Perfil5050));
        var otro = await Montar();

        Assert.Equal(HttpStatusCode.NotFound, (await otro.Cliente.GetAsync($"/api/gastos/{g.Id}")).StatusCode);
    }
}
