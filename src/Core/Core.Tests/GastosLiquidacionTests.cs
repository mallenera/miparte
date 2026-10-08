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

    private static async Task<Escenario> Montar(bool conBeto = true, bool cuentaActiva = true, bool admin = true)
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
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa", CuentaComunActiva = cuentaActiva });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = user, Rol = admin ? RolMiembro.Admin : RolMiembro.Miembro });
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
        // Ana adelantó los 100 €: figura como pagado y la cuenta común se los debe.
        var ana = resumen.Miembros.Single(m => m.Pagado > 0);
        Assert.Equal((100m, 0m, 100m), (ana.Pagado, ana.Asumido, ana.DebeCuentaComun));
        Assert.All(resumen.Miembros.Where(m => m != ana), m => Assert.Equal((0m, 0m, 0m), (m.Pagado, m.Asumido, m.DebeCuentaComun)));
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
    public async Task Gastos_FiltroPorMiembro_ConsideraPagadorYReparto()
    {
        var e = await Montar();
        var beto = e.Beto!.Value;
        var compartido = await CrearGasto(e, Gasto(e, 10m, e.Perfil5050)); // Ana paga, ambos asumen
        var soloAna = await CrearGasto(e, Gasto(e, 20m, e.PerfilIndividual)); // Ana paga y asume todo
        var pagaBeto = await CrearGasto(e, Gasto(e, 30m, e.Perfil5050, pagador: beto));

        var deBeto = await Leer<List<GastoResponse>>(await e.Cliente.GetAsync($"/api/gastos?miembroId={beto}"));
        Assert.Equal(new[] { compartido.Id, pagaBeto.Id }.Order(), deBeto.Select(g => g.Id).Order());
        var deAna = await Leer<List<GastoResponse>>(await e.Cliente.GetAsync($"/api/gastos?miembroId={e.Ana}"));
        Assert.Equal(3, deAna.Count);
        Assert.Contains(deAna, g => g.Id == soloAna.Id);
        Assert.Empty(await Leer<List<GastoResponse>>(await e.Cliente.GetAsync($"/api/gastos?miembroId={Guid.NewGuid()}")));
    }

    [Fact]
    public async Task Gastos_FiltroPorTextoDelConcepto_IgnoraMayusculas_Y_SeCombinaConOtrosFiltros()
    {
        var e = await Montar();
        await CrearGasto(e, Gasto(e, 10m, e.Perfil5050) with { Concepto = "Factura de la LUZ" });
        await CrearGasto(e, Gasto(e, 20m, e.Perfil5050, "2026-10-02") with { Concepto = "Luz octubre" });
        await CrearGasto(e, Gasto(e, 30m, e.Perfil5050) with { Concepto = "Supermercado" });

        Assert.Equal(2, (await Leer<List<GastoResponse>>(await e.Cliente.GetAsync("/api/gastos?buscar=luz"))).Count);
        var soloSep = await Leer<List<GastoResponse>>(await e.Cliente.GetAsync("/api/gastos?buscar=luz&mes=2026-09"));
        Assert.Equal(10m, Assert.Single(soloSep).Importe);
        Assert.Equal(3, (await Leer<List<GastoResponse>>(await e.Cliente.GetAsync("/api/gastos?buscar=%20"))).Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.GetAsync("/api/gastos?buscar=" + new string('a', 201))).StatusCode);
    }

    [Fact]
    public async Task Liquidacion_PagosParciales_RecalculanLoPendienteHastaSaldar()
    {
        var e = await Montar();
        var c = e.Cliente;
        var beto = e.Beto!.Value;
        var mes = new DateOnly(2026, 9, 1);
        await CrearGasto(e, Gasto(e, 100m, e.Perfil5050)); // Beto debe 50 a Ana

        async Task<decimal> Pendiente() =>
            (await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"))).Transferencias.SingleOrDefault()?.Importe ?? 0m;

        // Importes distintos del sugerido (50): 12,34 y luego 20,01; cada uno recalcula lo pendiente.
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/pagos-liquidacion", new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 12.34m, null, null))).StatusCode);
        Assert.Equal(37.66m, await Pendiente());
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/pagos-liquidacion", new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 20.01m, null, null))).StatusCode);
        Assert.Equal(17.65m, await Pendiente());

        // Más de 2 decimales se rechaza; el resto exacto salda el mes.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/pagos-liquidacion", new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 1.005m, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/pagos-liquidacion", new CrearPagoLiquidacionRequest(mes, beto, e.Ana, 17.65m, null, null))).StatusCode);
        Assert.Equal(0m, await Pendiente());
        Assert.Equal(3, (await Leer<LiquidacionResponse>(await c.GetAsync("/api/liquidacion?mes=2026-09"))).Pagos.Count);
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

    [Fact]
    public async Task CuentaComun_SinActivar_BloqueaEscriturasYGastosAsuCargo_PeroLaConsultaFunciona()
    {
        var e = await Montar(cuentaActiva: false);
        var mes = new DateOnly(2026, 9, 1);

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.False(estado.Activa);

        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 100m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PostAsJsonAsync(
            "/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(e.Ana, 10m, mes, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 50m, e.PerfilCuentaComun))).StatusCode);

        // Los gastos que no van a cargo de la cuenta siguen funcionando.
        Assert.Equal(HttpStatusCode.Created, (await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 50m, e.Perfil5050))).StatusCode);
    }

    [Fact]
    public async Task CuentaComun_Activacion_SoloAdmin_YDesbloqueaLaCuenta()
    {
        var noAdmin = await Montar(cuentaActiva: false, admin: false);
        var prohibido = await noAdmin.Cliente.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true));
        Assert.Equal(HttpStatusCode.Forbidden, prohibido.StatusCode);
        Assert.False((await Leer<CuentaComunResponse>(await noAdmin.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"))).Activa);

        var e = await Montar(cuentaActiva: false);
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true))).StatusCode);
        Assert.True((await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"))).Activa);
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 100m))).StatusCode);

        // Desactivar no borra nada: la aportación sigue en el estado.
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(false));
        var tras = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.False(tras.Activa);
        Assert.Equal(100m, tras.Aportado);
    }

    [Fact]
    public async Task CuentaComun_RecurrenteAsuCargo_NoSeGeneraSinActivarLaCuenta()
    {
        var e = await Montar(cuentaActiva: false);
        var plantilla = new GastoRecurrenteRequest(30m, e.Categoria, e.Ana, e.PerfilCuentaComun, 5, "Internet");
        Assert.Equal(HttpStatusCode.Created, (await e.Cliente.PostAsJsonAsync("/api/gastos-recurrentes", plantilla)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null)).StatusCode);
        Assert.Empty(await Leer<List<GastoResponse>>(await e.Cliente.GetAsync("/api/gastos?mes=2026-09")));

        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true));
        var generado = await Leer<GenerarRecurrentesResponse>(await e.Cliente.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null));
        Assert.Equal(1, generado.Creados);
    }

    [Fact]
    public async Task CuentaComun_SuParte_RepartePorLoAportadoYSumaElSaldoYElAhorro()
    {
        var e = await Montar();
        var mes = new DateOnly(2026, 9, 1);
        // Ana: 600 al mes con 200 de ahorro (400 para gastos); Beto: 400 con 0 de ahorro (400 para gastos). Más un ingreso de ahorro de Beto de 100.
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 600m, 200m));
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Beto!.Value, mes, 400m));
        await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(e.Beto.Value, 100m, mes, null));
        await CrearGasto(e, Gasto(e, 101m, e.PerfilCuentaComun)); // saldo: 800 - 101 = 699

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        var partes = estado.Partes!;
        Assert.Equal(2, partes.Count);
        Assert.Equal(estado.Saldo, partes.Sum(p => p.ParteSaldo));
        Assert.Equal(estado.AhorroDisponible, partes.Sum(p => p.ParteAhorro));

        var ana = partes.Single(p => p.MiembroId == e.Ana);
        var beto = partes.Single(p => p.MiembroId == e.Beto.Value);
        Assert.Equal((400m, 200m, 50m), (ana.Aportado, ana.Ahorrado, ana.PorcentajeGastos));
        Assert.Equal((400m, 100m), (beto.Aportado, beto.Ahorrado));
        Assert.Equal("Ana", ana.Nombre);
        Assert.Equal(101m, ana.Pendiente); // lo adelantó Ana
        Assert.Equal((200m, 100m), (ana.ParteAhorro, beto.ParteAhorro)); // 300 de ahorro: 2/3 para Ana y 1/3 para Beto
        Assert.Equal((349.50m, 349.50m), (ana.ParteSaldo, beto.ParteSaldo));
    }

    [Fact]
    public async Task Resumen_IncluyeLosSaldosDeLaCuentaSoloSiEstaActivada()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 500m, 100m));
        await CrearGasto(e, Gasto(e, 120m, e.PerfilCuentaComun));

        var resumen = await Leer<ResumenMensualResponse>(await e.Cliente.GetAsync("/api/resumen?mes=2026-09"));
        var cuenta = resumen.CuentaComun!;
        Assert.Equal((500m, 120m, 280m, 400m, 120m), (cuenta.AportadoMes, cuenta.GastadoMes, cuenta.Saldo, cuenta.Efectivo, cuenta.Pendiente));
        Assert.Equal((100m, 100m), (cuenta.AhorroMes, cuenta.AhorroDisponible));

        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(false));
        Assert.Null((await Leer<ResumenMensualResponse>(await e.Cliente.GetAsync("/api/resumen?mes=2026-09"))).CuentaComun);
    }

    [Fact]
    public async Task CuentaComun_AportacionesGastoYReembolso_DanSaldoYEfectivo()
    {
        var e = await Montar();
        foreach (var (miembro, importe) in new[] { (e.Ana, 600m), (e.Beto!.Value, 400m) })
            Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync(
                "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(miembro, new DateOnly(2026, 9, 1), importe))).StatusCode);
        await CrearGasto(e, Gasto(e, 900m, e.PerfilCuentaComun)); // lo adelanta Ana

        var antes = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((1000m, 1000m, 900m, 100m, 1000m), (antes.AportadoMes, antes.Aportado, antes.Gastado, antes.Saldo, antes.Efectivo));
        Assert.Equal(900m, Assert.Single(antes.Pendientes).Importe);

        var r = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/reembolsos",
            new CrearReembolsoRequest(e.Ana, 400m, new DateOnly(2026, 9, 20), null));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var despues = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((100m, 600m), (despues.Saldo, despues.Efectivo));
        Assert.Equal(500m, Assert.Single(despues.Pendientes).Importe);
        Assert.Single(despues.Reembolsos);
    }

    [Fact]
    public async Task CuentaComun_Reembolso_NoPuedeSuperarLoPendiente()
    {
        var e = await Montar();
        await CrearGasto(e, Gasto(e, 100m, e.PerfilCuentaComun));

        var r = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/reembolsos",
            new CrearReembolsoRequest(e.Ana, 100.01m, null, null));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task CuentaComun_Aportacion_SustituyeLaDelMismoMesYValidaDatos()
    {
        var e = await Montar();
        var mes = new DateOnly(2026, 9, 1);
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 500m));
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 0m));

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal(0m, Assert.Single(estado.Aportaciones).Importe);

        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 2), 10m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, -1m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(Guid.NewGuid(), mes, 10m))).StatusCode);
    }

    [Fact]
    public async Task CuentaComun_Ahorro_ApartaDineroDelSaldoYValidaLimites()
    {
        var e = await Montar();
        var mes = new DateOnly(2026, 9, 1);
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 600m, 150m))).StatusCode);
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Beto!.Value, mes, 400m));

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((1000m, 850m, 150m, 150m), (estado.Aportado, estado.Saldo, estado.AhorroMes, estado.AhorroDisponible));
        Assert.Equal(150m, estado.Aportaciones.Single(a => a.MiembroId == e.Ana).Ahorro);

        foreach (var ahorro in new[] { -1m, 600.01m, 10.005m })
            Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PutAsJsonAsync(
                "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 600m, ahorro))).StatusCode);
    }

    [Fact]
    public async Task CuentaComun_RetiradaDeAhorro_BajaElDisponibleYNoPuedeSuperarloAunqueSeAcumuleEnVarias()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 500m, 200m));

        var ok = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(e.Ana, 150m, new DateOnly(2026, 9, 20), "Vacaciones"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var excede = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(e.Ana, 50.01m, new DateOnly(2026, 9, 21), null));
        Assert.Equal(HttpStatusCode.Conflict, excede.StatusCode);

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((200m, 150m, 50m, 300m), (estado.AhorroAcumulado, estado.AhorroRetirado, estado.AhorroDisponible, estado.Saldo));
        var retirada = Assert.Single(estado.RetiradasAhorro!);
        Assert.Equal("Vacaciones", retirada.Concepto);

        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/retiradas-ahorro/{retirada.Id}")).StatusCode);
        estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal(200m, estado.AhorroDisponible);
    }

    [Fact]
    public async Task CuentaComun_RetiradaDeAhorro_ValidaImporteYMiembro()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 500m, 200m));

        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(e.Ana, 0m, new DateOnly(2026, 9, 20), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(Guid.NewGuid(), 10m, new DateOnly(2026, 9, 20), null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/retiradas-ahorro/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task CuentaComun_DepositoDeAhorro_SumaAlDisponibleYPermiteRetirarSinAportaciones()
    {
        var e = await Montar();

        var d = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro",
            new CrearDepositoAhorroRequest(e.Ana, 1000m, new DateOnly(2026, 9, 1), "Ahorro inicial"));
        Assert.Equal(HttpStatusCode.Created, d.StatusCode);
        var deposito = (await d.Content.ReadFromJsonAsync<DepositoAhorroDto>(Web))!;

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((1000m, 1000m, 1000m, 0m), (estado.AhorroMes, estado.AhorroAcumulado, estado.AhorroDisponible, estado.Saldo));
        Assert.Equal("Ahorro inicial", Assert.Single(estado.DepositosAhorro!).Concepto);

        var r = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(e.Ana, 1000m, new DateOnly(2026, 9, 2), null));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var retirada = (await r.Content.ReadFromJsonAsync<RetiradaAhorroDto>(Web))!;

        // Ya retirado, el ingreso no se puede borrar; al deshacer la retirada, sí.
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/depositos-ahorro/{deposito.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/retiradas-ahorro/{retirada.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/depositos-ahorro/{deposito.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/depositos-ahorro/{deposito.Id}")).StatusCode);
    }

    [Fact]
    public async Task CuentaComun_DepositoDeAhorro_ValidaImporteYMiembro()
    {
        var e = await Montar();

        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro",
            new CrearDepositoAhorroRequest(e.Ana, 0m, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro",
            new CrearDepositoAhorroRequest(Guid.NewGuid(), 10m, null, null))).StatusCode);
    }

    [Fact]
    public async Task Gasto_PagadoDesdeAhorro_DescuentaDelAhorroYNoDelSaldo()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 500m, 200m));
        await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(e.Ana, 1000m, new DateOnly(2026, 9, 1), null));

        var g = await CrearGasto(e, Gasto(e, 300m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });

        Assert.True(g.PagadoDesdeAhorro);
        Assert.True(g.ACargoCuentaComun);
        Assert.Empty(g.Repartos);
        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((300m, 900m, 300m), (estado.AhorroGastado, estado.AhorroDisponible, estado.Saldo)); // 1200 ahorrado - 300; el saldo es 500 - 200
        Assert.Equal(0m, estado.Gastado);
        Assert.Empty(estado.Pendientes);
        var liq = await Leer<LiquidacionResponse>(await e.Cliente.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.Empty(liq.Transferencias);

        // Al editarlo, su propio importe no cuenta contra el ahorro disponible: se puede subir hasta lo que haya (900 + 300).
        var sube = await e.Cliente.PutAsJsonAsync($"/api/gastos/{g.Id}", Gasto(e, 1200m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });
        Assert.Equal(HttpStatusCode.OK, sube.StatusCode);
        var pasa = await e.Cliente.PutAsJsonAsync($"/api/gastos/{g.Id}", Gasto(e, 1200.01m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });
        Assert.Equal(HttpStatusCode.Conflict, pasa.StatusCode);
    }

    [Fact]
    public async Task Gasto_PagadoDesdeAhorro_NoPuedeSuperarElAhorroNiTenerPagador()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 500m, 100m));

        var sinAhorro = await e.Cliente.PostAsJsonAsync("/api/gastos",
            Gasto(e, 100.01m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });
        Assert.Equal(HttpStatusCode.Conflict, sinAhorro.StatusCode);

        var conPagador = await e.Cliente.PostAsJsonAsync("/api/gastos",
            Gasto(e, 50m, e.PerfilCuentaComun) with { PagadoDesdeAhorro = true });
        Assert.Equal(HttpStatusCode.BadRequest, conPagador.StatusCode);
    }

    [Fact]
    public async Task CuentaComun_NoSePuedeRebajarElAhorroSiYaSeRetiroOGastoDesdeEl()
    {
        var e = await Montar();
        var mes = new DateOnly(2026, 9, 1);
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 500m, 200m));
        await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro", new CrearRetiradaAhorroRequest(e.Ana, 120m, new DateOnly(2026, 9, 10), null));
        await CrearGasto(e, Gasto(e, 50m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });

        // Queda respaldo para 170 (120 + 50): bajar a 169,99 no se permite, a 170 sí.
        var rechazada = await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 500m, 169.99m));
        Assert.Equal(HttpStatusCode.Conflict, rechazada.StatusCode);
        Assert.Contains("0.01", await rechazada.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 500m, 170m))).StatusCode);

        // Subirlo, o cambiar solo el importe de gastos, siempre se puede.
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync(
            "/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, mes, 300m, 250m))).StatusCode);

        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal(80m, estado.AhorroDisponible); // 250 - 120 - 50
    }

    [Fact]
    public async Task CuentaComun_LaRebajaDelAhorroSeComprueba_EnCadaMesConUsos_NoSoloEnElUltimo()
    {
        var e = await Montar();
        var enero = new DateOnly(2026, 1, 1);
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, enero, 500m, 200m));
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 2, 1), 500m, 200m));
        // Retirada de enero (cubierta por los 200 de enero) y un gasto pequeño desde el ahorro en junio.
        Assert.Equal(HttpStatusCode.Created, (await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(e.Ana, 200m, new DateOnly(2026, 1, 15), null))).StatusCode);
        await CrearGasto(e, Gasto(e, 10m, e.PerfilCuentaComun) with { Fecha = new DateOnly(2026, 6, 10), PagadoPor = null, PagadoDesdeAhorro = true });

        // Junio seguiría cubierto (1000 ahorrados desde febrero frente a 210), pero la retirada de enero se quedaría sin respaldo.
        var rechazada = await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, enero, 500m, 0m));

        Assert.Equal(HttpStatusCode.Conflict, rechazada.StatusCode);
        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-01"));
        Assert.Equal(0m, estado.AhorroDisponible);
    }

    [Fact]
    public async Task CuentaComun_NoSePuedeBorrarUnIngresoDeAhorroQueYaSeUso()
    {
        var e = await Montar();
        var d = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro",
            new CrearDepositoAhorroRequest(e.Ana, 500m, new DateOnly(2026, 9, 1), "Lotería"));
        var deposito = (await d.Content.ReadFromJsonAsync<DepositoAhorroDto>(Web))!;
        await CrearGasto(e, Gasto(e, 300m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });

        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/depositos-ahorro/{deposito.Id}")).StatusCode);

        // Con otro ingreso que lo respalde, ya se puede borrar el primero.
        await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(e.Ana, 300m, new DateOnly(2026, 9, 2), null));
        Assert.Equal(HttpStatusCode.NoContent, (await e.Cliente.DeleteAsync($"/api/cuenta-comun/depositos-ahorro/{deposito.Id}")).StatusCode);
    }

    [Fact]
    public async Task CuentaComun_Retirada_TambienDescuentaLoGastadoDesdeElAhorro()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 500m, 100m));
        await CrearGasto(e, Gasto(e, 70m, e.PerfilCuentaComun) with { PagadoPor = null, PagadoDesdeAhorro = true });

        var excede = await e.Cliente.PostAsJsonAsync("/api/cuenta-comun/retiradas-ahorro",
            new CrearRetiradaAhorroRequest(e.Ana, 30.01m, new DateOnly(2026, 9, 20), null));

        Assert.Equal(HttpStatusCode.Conflict, excede.StatusCode);
    }

    [Fact]
    public async Task CuentaComun_GastoPagadoPorLaCuenta_BajaElEfectivoYNoDejaPendiente()
    {
        var e = await Montar();
        await e.Cliente.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.Ana, new DateOnly(2026, 9, 1), 1000m));

        var g = await CrearGasto(e, Gasto(e, 300m, e.PerfilCuentaComun) with { PagadoPor = null });

        Assert.Null(g.PagadoPor);
        Assert.True(g.ACargoCuentaComun);
        var estado = await Leer<CuentaComunResponse>(await e.Cliente.GetAsync("/api/cuenta-comun?mes=2026-09"));
        Assert.Equal((700m, 700m), (estado.Saldo, estado.Efectivo));
        Assert.Empty(estado.Pendientes);
        var liq = await Leer<LiquidacionResponse>(await e.Cliente.GetAsync("/api/liquidacion?mes=2026-09"));
        Assert.Empty(liq.Transferencias);
        var resumen = await Leer<ResumenMensualResponse>(await e.Cliente.GetAsync("/api/resumen?mes=2026-09"));
        Assert.Equal(300m, resumen.GastosTotales);
    }

    [Fact]
    public async Task Gasto_PagadoPorLaCuenta_ExigeElPerfilDeCuentaComun()
    {
        var e = await Montar();

        var r = await e.Cliente.PostAsJsonAsync("/api/gastos", Gasto(e, 50m, e.Perfil6040) with { PagadoPor = null });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
