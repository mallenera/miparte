using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Demo;

namespace MiParte.Web.Tests;

/// <summary>El servidor en memoria del modo demo debe comportarse como Core.Api para el <see cref="CoreApiClient"/>.</summary>
public class ServidorDemoTests
{
    private sealed class Puente(ServidorDemo servidor) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            servidor.ResponderAsync(request, ct);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static CoreApiClient Cliente(DateTimeOffset? ahora = null) =>
        new(new HttpClient(new Puente(new ServidorDemo(new RelojFijo(ahora ?? new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)))))
        {
            BaseAddress = new Uri("http://localhost:5001/"),
        });

    [Fact]
    public async Task Hay_un_hogar_con_miembros_y_gastos_de_ejemplo()
    {
        var api = Cliente();
        var yo = await api.YoAsync();
        Assert.Equal(yo.Hogares.Single(), yo.HogarActual);
        var miembros = await api.ListarMiembrosAsync();
        Assert.Single(miembros, m => m.EsYo);
        Assert.NotEmpty(await api.ListarGastosAsync("2026-10"));
        Assert.NotEmpty(await api.ListarGastosAsync("2026-09"));
    }

    [Fact]
    public async Task Un_gasto_nuevo_se_reparte_y_suma_su_importe()
    {
        var api = Cliente();
        var miembros = await api.ListarMiembrosAsync();
        var perfil = (await api.ListarPerfilesAsync()).Single(p => p.Modo == "partes");
        var categoria = (await api.ListarCategoriasAsync()).First();

        var gasto = await api.CrearGastoAsync(new GastoRequest(
            new DateOnly(2026, 10, 5), 100.01m, categoria.Id, miembros.First(m => m.EsYo).Id, perfil.Id, "Prueba"));

        Assert.Equal(100.01m, gasto.Repartos.Sum(r => r.ImporteAsumido));
        Assert.Contains(await api.ListarGastosAsync("2026-10"), g => g.Id == gasto.Id);
    }

    [Fact]
    public async Task Los_errores_llegan_como_ApiException_con_el_mensaje_de_Core_Api()
    {
        var api = Cliente();
        var e = await Assert.ThrowsAsync<ApiException>(() => api.CrearGastoAsync(new GastoRequest(
            new DateOnly(2026, 10, 5), -3m, Guid.NewGuid(), null, Guid.NewGuid(), null)));
        Assert.Equal("El importe debe ser mayor que cero.", e.Message);
    }

    [Fact]
    public async Task La_liquidacion_deja_saldos_que_suman_cero_y_un_pago_los_reduce()
    {
        var api = Cliente();
        var liq = await api.ObtenerLiquidacionAsync("2026-09");
        Assert.Equal(0m, liq.Saldos.Sum(s => s.Saldo));
        var t = Assert.Single(liq.Transferencias);

        await api.CrearPagoLiquidacionAsync(new CrearPagoLiquidacionRequest(new DateOnly(2026, 9, 1), t.De, t.A, t.Importe, null, null));

        var despues = await api.ObtenerLiquidacionAsync("2026-09");
        Assert.Empty(despues.Transferencias);
        Assert.Single(despues.Pagos);
    }

    [Fact]
    public async Task Generar_recurrentes_es_idempotente()
    {
        var api = Cliente();
        var primera = await api.GenerarRecurrentesAsync("2026-11");
        var segunda = await api.GenerarRecurrentesAsync("2026-11");
        Assert.Equal(2, primera.Creados);
        Assert.Equal(0, segunda.Creados);
        Assert.Equal(2, segunda.YaExistentes);
    }

    [Fact]
    public async Task La_cuenta_comun_tiene_saldo_y_un_reembolso_pendiente_que_se_puede_saldar()
    {
        var api = Cliente();
        var cuenta = await api.ObtenerCuentaComunAsync("2026-10");
        var pendiente = Assert.Single(cuenta.Pendientes);
        Assert.Equal(95m, pendiente.Importe);

        await api.CrearReembolsoAsync(new CrearReembolsoRequest(pendiente.MiembroId, 95m, null, null));
        Assert.Empty((await api.ObtenerCuentaComunAsync("2026-10")).Pendientes);
    }

    [Fact]
    public async Task No_se_puede_borrar_un_perfil_en_uso_ni_dejar_al_hogar_sin_admin()
    {
        var api = Cliente();
        var perfil = (await api.ListarPerfilesAsync()).First(p => p.Modo == "partes");
        await Assert.ThrowsAsync<ApiException>(() => api.EliminarPerfilAsync(perfil.Id));

        var yo = (await api.ListarMiembrosAsync()).Single(m => m.EsYo);
        await Assert.ThrowsAsync<ApiException>(() => api.ActualizarMiembroAsync(yo.Id, new ActualizarMiembroRequest(Rol: "miembro")));
    }
}
