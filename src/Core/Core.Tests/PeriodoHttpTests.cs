using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Cableado HTTP de la frecuencia de aportaciones y del cierre de periodo: códigos de estado, permisos y bloqueo de gastos
/// dentro de un periodo cerrado. La liquidación final con gastos pendientes necesita PostgreSQL y va en
/// <see cref="PeriodoPostgresTests"/>.
/// </summary>
public class PeriodoHttpTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Marzo1 = new(2020, 3, 1);

    private sealed record Escenario(HttpClient Admin, HttpClient Miembro, Guid Ana, Guid Categoria, Guid Perfil, WebApplicationFactory<Program> F);

    /// <summary>Hogar con Ana (admin) y Beto (miembro con solo <c>gastos.crear</c>). El periodo se fija con <paramref name="periodo"/>.</summary>
    private static async Task<Escenario> Montar(Action<Hogar> periodo)
    {
        var f = Crear(Secreto);
        var (userAna, userBeto) = (Guid.NewGuid(), Guid.NewGuid());
        var hogar = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var cat = Guid.NewGuid();
        var perfil = Guid.NewGuid();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            var h = new Hogar { Id = hogar, Nombre = "Viaje a Lisboa" };
            periodo(h);
            db.Hogares.Add(h);
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = userAna, Rol = RolMiembro.Admin });
            db.Miembros.Add(new Miembro
            {
                Id = Guid.NewGuid(), HogarId = hogar, Nombre = "Beto", Tipo = TipoMiembro.Adulto, UserId = userBeto,
                Rol = RolMiembro.Miembro, Permisos = [CatalogoPermisos.GastosCrear],
            });
            db.Categorias.Add(new Categoria { Id = cat, HogarId = hogar, Nombre = "Comida" });
            db.PerfilesReparto.Add(new PerfilReparto { Id = perfil, HogarId = hogar, Nombre = "Individual", Modo = ModoReparto.Individual });
            await db.SaveChangesAsync();
        }

        return new Escenario(Cliente(f, Token(Hs256(), userAna)), Cliente(f, Token(Hs256(), userBeto)), ana, cat, perfil, f);
    }

    private static async Task<string?> ErrorDe(HttpResponseMessage r)
    {
        var cuerpo = await r.Content.ReadFromJsonAsync<Dictionary<string, string>>(Web);
        return cuerpo is not null && cuerpo.TryGetValue("error", out var error) ? error : null;
    }

    private static GastoRequest Gasto(Escenario e, DateOnly fecha) => new(fecha, 10m, e.Categoria, e.Ana, e.Perfil, "Compra");

    private static async Task<GastoResponse> CrearGasto(Escenario e, DateOnly fecha)
    {
        var r = await e.Admin.PostAsJsonAsync("/api/gastos", Gasto(e, fecha));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<GastoResponse>(Web))!;
    }

    /// <summary>Marca el periodo del hogar como cerrado directamente en la base, como lo deja el cierre.</summary>
    private static void CerrarEnBaseDeDatos(Escenario e)
    {
        using var scope = e.F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        var h = db.Hogares.IgnoreQueryFilters().Single();
        h.PeriodoCerradoEn = DateTimeOffset.UtcNow;
        db.SaveChanges();
    }

    [Fact]
    public async Task FrecuenciaDeAportaciones_SeGuardaComoAdmin()
    {
        var e = await Montar(_ => { });

        var r = await e.Admin.PutAsJsonAsync("/api/hogar/aportaciones", new FijarFrecuenciaAportacionRequest("bimestral"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("bimestral", (await r.Content.ReadFromJsonAsync<FrecuenciaAportacionResponse>(Web))!.Frecuencia);
        var hogar = await e.Admin.GetFromJsonAsync<HogarResumen>($"/api/hogares/{await IdHogar(e)}", Web);
        Assert.Equal("bimestral", hogar!.AportacionFrecuencia);
    }

    [Theory]
    [InlineData("quincenal")]
    [InlineData("")]
    public async Task FrecuenciaDeAportaciones_RechazaValoresNoValidos(string valor)
    {
        var e = await Montar(_ => { });

        var r = await e.Admin.PutAsJsonAsync("/api/hogar/aportaciones", new FijarFrecuenciaAportacionRequest(valor));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task FrecuenciaDeAportaciones_SinPermisoHogarFunciones_Responde403()
    {
        var e = await Montar(_ => { });

        var r = await e.Miembro.PutAsJsonAsync("/api/hogar/aportaciones", new FijarFrecuenciaAportacionRequest("anual"));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task CerrarPeriodo_SinPeriodoPropio_EsBadRequest()
    {
        var e = await Montar(_ => { });

        var r = await e.Admin.PostAsync("/api/hogar/cerrar-periodo", null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("El hogar no tiene un periodo propio que cerrar.", await ErrorDe(r));
    }

    [Fact]
    public async Task CerrarPeriodo_QueNoHaEmpezado_EsBadRequest()
    {
        var e = await Montar(h => h.PeriodoPropio = Periodo.Abierto(new DateOnly(2999, 1, 1)));

        var r = await e.Admin.PostAsync("/api/hogar/cerrar-periodo", null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task CerrarPeriodo_SinPermisoMesCerrar_Responde403()
    {
        var e = await Montar(h => h.PeriodoPropio = Periodo.Abierto(new DateOnly(2020, 3, 1)));

        var r = await e.Miembro.PostAsync("/api/hogar/cerrar-periodo", null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task CerrarPeriodoYaCerrado_EsConflicto()
    {
        var e = await Montar(h =>
        {
            h.PeriodoPropio = Periodo.Rango(Marzo1, new DateOnly(2020, 3, 20));
            h.PeriodoCerradoEn = DateTimeOffset.UtcNow;
        });

        var r = await e.Admin.PostAsync("/api/hogar/cerrar-periodo", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("El periodo ya está cerrado.", await ErrorDe(r));
    }

    [Fact]
    public async Task ReabrirPeriodoQueNoEstabaCerrado_EsNoEncontrado()
    {
        var e = await Montar(h => h.PeriodoPropio = Periodo.Abierto(Marzo1));

        var r = await e.Admin.PostAsync("/api/hogar/reabrir-periodo", null);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task ReabrirPeriodo_SinPermisoHogarFunciones_Responde403()
    {
        var e = await Montar(h => h.PeriodoPropio = Periodo.Rango(Marzo1, new DateOnly(2020, 3, 20)));

        var r = await e.Miembro.PostAsync("/api/hogar/reabrir-periodo", null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task GastoDentroDeUnPeriodoCerrado_NoSeCrea()
    {
        var e = await Montar(h => h.PeriodoPropio = Periodo.Rango(Marzo1, new DateOnly(2020, 3, 20)));
        CerrarEnBaseDeDatos(e);

        var dentro = await e.Admin.PostAsJsonAsync("/api/gastos", Gasto(e, new DateOnly(2020, 3, 10)));
        var fuera = await e.Admin.PostAsJsonAsync("/api/gastos", Gasto(e, new DateOnly(2020, 4, 2)));

        Assert.Equal(HttpStatusCode.Conflict, dentro.StatusCode);
        Assert.Equal("El periodo está cerrado.", await ErrorDe(dentro));
        Assert.Equal(HttpStatusCode.Created, fuera.StatusCode);
    }

    [Fact]
    public async Task GastoDeUnPeriodoCerrado_NoSeEditaNiSeBorra()
    {
        var e = await Montar(h => h.PeriodoPropio = Periodo.Rango(Marzo1, new DateOnly(2020, 3, 20)));
        var gasto = await CrearGasto(e, new DateOnly(2020, 3, 10));
        CerrarEnBaseDeDatos(e);

        var editar = await e.Admin.PutAsJsonAsync($"/api/gastos/{gasto.Id}", Gasto(e, new DateOnly(2020, 3, 11)));
        var borrar = await e.Admin.DeleteAsync($"/api/gastos/{gasto.Id}");

        Assert.Equal(HttpStatusCode.Conflict, editar.StatusCode);
        Assert.Equal("El periodo está cerrado.", await ErrorDe(editar));
        Assert.Equal(HttpStatusCode.Conflict, borrar.StatusCode);
    }

    private static async Task<Guid> IdHogar(Escenario e)
    {
        using var scope = e.F.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MiParteDbContext>().Hogares.IgnoreQueryFilters().Single().Id;
    }
}
