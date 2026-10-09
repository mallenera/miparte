using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

public class PermisosTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Escenario(HttpClient Admin, HttpClient Miembro, Guid AdminId, Guid MiembroId, Guid Categoria, Guid Perfil);

    /// <summary>Hogar con un admin (Ana) y un miembro sin permisos concedidos salvo los indicados (Beto).</summary>
    private static async Task<Escenario> Montar(params string[] permisosDeBeto)
    {
        var f = Crear(Secreto);
        var (userAna, userBeto) = (Guid.NewGuid(), Guid.NewGuid());
        var (hogar, ana, beto, cat, perfil) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa", CuentaComunActiva = true, AhorroActivo = true });
            db.Miembros.Add(new Miembro { Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = userAna, Rol = RolMiembro.Admin });
            db.Miembros.Add(new Miembro
            {
                Id = beto, HogarId = hogar, Nombre = "Beto", Tipo = TipoMiembro.Adulto, UserId = userBeto, Rol = RolMiembro.Miembro,
                Permisos = [.. permisosDeBeto],
            });
            db.Categorias.Add(new Categoria { Id = cat, HogarId = hogar, Nombre = "Comida" });
            db.PerfilesReparto.Add(new PerfilReparto { Id = perfil, HogarId = hogar, Nombre = "Individual", Modo = ModoReparto.Individual });
            await db.SaveChangesAsync();
        }
        return new Escenario(Cliente(f, Token(Hs256(), userAna)), Cliente(f, Token(Hs256(), userBeto)), ana, beto, cat, perfil);
    }

    private static GastoRequest Gasto(Escenario e) => new(new DateOnly(2026, 9, 10), 10m, e.Categoria, e.MiembroId, e.Perfil, "Compra");

    [Fact]
    public void Dominio_PorDefectoReproduceLoQueHaciaUnMiembro()
    {
        Assert.DoesNotContain(CatalogoPermisos.MesReabrir, CatalogoPermisos.PorDefecto);
        Assert.DoesNotContain(CatalogoPermisos.HistorialVer, CatalogoPermisos.PorDefecto);
        Assert.Equal(CatalogoPermisos.Todos.Count - 2, CatalogoPermisos.PorDefecto.Count);
        Assert.Equal(CatalogoPermisos.Todos.Count, CatalogoPermisos.Todos.Select(p => p.Clave).Distinct().Count());
    }

    [Fact]
    public void Dominio_EfectivosSegunPerfil()
    {
        var admin = new Miembro { Tipo = TipoMiembro.Adulto, UserId = Guid.NewGuid(), Rol = RolMiembro.Admin, Permisos = [] };
        var miembro = new Miembro { Tipo = TipoMiembro.Adulto, UserId = Guid.NewGuid(), Permisos = [CatalogoPermisos.GastosCrear, "inventado"] };
        var sinCuenta = new Miembro { Tipo = TipoMiembro.Adulto };
        var aCargo = new Miembro { Tipo = TipoMiembro.ACargo, UserId = Guid.NewGuid() };

        Assert.Equal(CatalogoPermisos.Todos.Count, CatalogoPermisos.Efectivos(admin).Count);
        Assert.Equal([CatalogoPermisos.GastosCrear], CatalogoPermisos.Efectivos(miembro));
        Assert.Empty(CatalogoPermisos.Efectivos(sinCuenta));
        Assert.Empty(CatalogoPermisos.Efectivos(aCargo));
    }

    [Fact]
    public async Task Gastos_SinPermisoDevuelve403_ConPermisoFunciona()
    {
        var e = await Montar(CatalogoPermisos.GastosCrear);

        var creado = await e.Miembro.PostAsJsonAsync("/api/gastos", Gasto(e));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var gasto = (await creado.Content.ReadFromJsonAsync<GastoResponse>(Web))!;

        var editar = await e.Miembro.PutAsJsonAsync($"/api/gastos/{gasto.Id}", Gasto(e));
        Assert.Equal(HttpStatusCode.Forbidden, editar.StatusCode);
        Assert.Contains("No tienes permiso", await editar.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.DeleteAsync($"/api/gastos/{gasto.Id}")).StatusCode);

        // El admin lo tiene todo aunque no tenga nada guardado, y las lecturas siguen abiertas.
        Assert.Equal(HttpStatusCode.OK, (await e.Admin.PutAsJsonAsync($"/api/gastos/{gasto.Id}", Gasto(e))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.GetAsync("/api/gastos?mes=2026-09")).StatusCode);
    }

    [Fact]
    public async Task EscriturasProtegidas_Devuelven403SinPermiso()
    {
        var e = await Montar(); // sin ningún permiso
        var mes = new DateOnly(2026, 9, 1);

        var respuestas = new[]
        {
            await e.Miembro.PostAsJsonAsync("/api/gastos", Gasto(e)),
            await e.Miembro.PostAsJsonAsync("/api/gastos-recurrentes", new GastoRecurrenteRequest(30m, e.Categoria, e.MiembroId, e.Perfil, 5, "Internet")),
            await e.Miembro.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null),
            await e.Miembro.PostAsJsonAsync("/api/cierres-mes", new CerrarMesRequest("2020-03")),
            await e.Miembro.DeleteAsync("/api/cierres-mes/2020-03"),
            await e.Miembro.PutAsJsonAsync("/api/cuenta-comun/aportaciones", new FijarAportacionRequest(e.MiembroId, mes, 100m)),
            await e.Miembro.PostAsJsonAsync("/api/cuenta-comun/depositos-ahorro", new CrearDepositoAhorroRequest(e.MiembroId, 10m, mes, null)),
            await e.Miembro.PostAsJsonAsync("/api/categorias", new GuardarCategoriaRequest("Ocio", null, null)),
            await e.Miembro.PostAsJsonAsync("/api/perfiles", new GuardarPerfilRequest("Nuevo", "individual", [])),
            await e.Miembro.GetAsync("/api/auditoria"),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, (await e.Admin.GetAsync("/api/auditoria")).StatusCode);
    }

    [Fact]
    public async Task Historial_ConElPermisoLoVeUnMiembro()
    {
        var e = await Montar(CatalogoPermisos.HistorialVer);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.GetAsync("/api/auditoria")).StatusCode);
    }

    [Fact]
    public async Task Reabrir_ConElPermisoLoHaceUnMiembro()
    {
        var e = await Montar(CatalogoPermisos.MesCerrar, CatalogoPermisos.MesReabrir);
        Assert.Equal(HttpStatusCode.Created, (await e.Miembro.PostAsJsonAsync("/api/cierres-mes", new CerrarMesRequest("2020-03"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Miembro.DeleteAsync("/api/cierres-mes/2020-03")).StatusCode);
    }

    [Fact]
    public async Task EditarPermisos_SoloAdmin_ValidaClavesYSeReflejanEnElMiembro()
    {
        var e = await Montar();
        var cuerpo = new ActualizarMiembroRequest(Permisos: [CatalogoPermisos.GastosCrear, CatalogoPermisos.HistorialVer]);

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Admin.PutAsJsonAsync(
            $"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Permisos: ["inventado"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Admin.PutAsJsonAsync($"/api/miembros/{e.AdminId}", cuerpo)).StatusCode);

        var ok = await e.Admin.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", cuerpo);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal([CatalogoPermisos.GastosCrear, CatalogoPermisos.HistorialVer], (await ok.Content.ReadFromJsonAsync<MiembroDto>(Web))!.Permisos);

        var lista = (await e.Admin.GetFromJsonAsync<List<MiembroDto>>("/api/miembros", Web))!;
        Assert.Equal(CatalogoPermisos.Todos.Count, lista.Single(m => m.Id == e.AdminId).Permisos!.Count);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.GetAsync("/api/auditoria")).StatusCode); // ya lo tiene
    }

    [Fact]
    public async Task EditarPermisos_AdultoSinCuenta_Devuelve409()
    {
        var e = await Montar();
            // Un adulto sin cuenta no tiene permisos propios: se comprueba creándolo con la API.
        var creado = await e.Admin.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Cris", "adulto"));
        var cris = (await creado.Content.ReadFromJsonAsync<MiembroDto>(Web))!;

        var r = await e.Admin.PutAsJsonAsync($"/api/miembros/{cris.Id}", new ActualizarMiembroRequest(Permisos: [CatalogoPermisos.GastosCrear]));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Empty(cris.Permisos!);
    }
}
