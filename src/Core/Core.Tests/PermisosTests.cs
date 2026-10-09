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

    /// <summary>Hogar con un admin (Ana, plantilla de admin) y un miembro (Beto) con exactamente los permisos indicados.</summary>
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
        string[] soloConAsignacion =
        [
            CatalogoPermisos.MesReabrir, CatalogoPermisos.HistorialVer, CatalogoPermisos.MiembrosGestionar,
            CatalogoPermisos.InvitacionesCrear, CatalogoPermisos.HogarFunciones, CatalogoPermisos.PermisosGestionar, CatalogoPermisos.HogarEliminar,
        ];
        Assert.All(soloConAsignacion, c => Assert.DoesNotContain(c, CatalogoPermisos.PorDefecto));
        Assert.Equal(CatalogoPermisos.Todos.Count - soloConAsignacion.Length, CatalogoPermisos.PorDefecto.Count);
        Assert.Equal(CatalogoPermisos.Todos.Count, CatalogoPermisos.Todos.Select(p => p.Clave).Distinct().Count());
    }

    [Fact]
    public void Dominio_EfectivosSegunPlantillaDelRolOListaPropia()
    {
        var adminPlantilla = new Miembro { Tipo = TipoMiembro.Adulto, UserId = Guid.NewGuid(), Rol = RolMiembro.Admin };
        var adminRestringido = new Miembro { Tipo = TipoMiembro.Adulto, UserId = Guid.NewGuid(), Rol = RolMiembro.Admin, Permisos = [CatalogoPermisos.GastosCrear] };
        var miembroPlantilla = new Miembro { Tipo = TipoMiembro.Adulto, UserId = Guid.NewGuid() };
        var miembroPropio = new Miembro { Tipo = TipoMiembro.Adulto, UserId = Guid.NewGuid(), Permisos = [CatalogoPermisos.PermisosGestionar, "inventado"] };
        var sinCuenta = new Miembro { Tipo = TipoMiembro.Adulto };
        var aCargo = new Miembro { Tipo = TipoMiembro.ACargo, UserId = Guid.NewGuid() };

        Assert.Equal(CatalogoPermisos.Todos.Count, CatalogoPermisos.Efectivos(adminPlantilla).Count);
        Assert.Equal([CatalogoPermisos.GastosCrear], CatalogoPermisos.Efectivos(adminRestringido));
        Assert.Equal(CatalogoPermisos.PorDefecto.Order(), CatalogoPermisos.Efectivos(miembroPlantilla).Order());
        Assert.Equal([CatalogoPermisos.PermisosGestionar], CatalogoPermisos.Efectivos(miembroPropio));
        Assert.Empty(CatalogoPermisos.Efectivos(sinCuenta));
        Assert.Empty(CatalogoPermisos.Efectivos(aCargo));
    }

    [Fact]
    public void Dominio_PlantillasSoloUsanClavesDelCatalogo()
    {
        Assert.All(CatalogoPermisos.Plantillas, p => Assert.All(p.Claves, c => Assert.True(CatalogoPermisos.Existe(c))));
        Assert.Empty(CatalogoPermisos.Plantillas.Single(p => p.Nombre == "Solo lectura").Claves);
        Assert.Equal(CatalogoPermisos.Todos.Count, CatalogoPermisos.Plantillas.Single(p => p.Nombre == "Administrador").Claves.Count);
        var gestor = CatalogoPermisos.Plantillas.Single(p => p.Nombre == "Gestor").Claves;
        Assert.DoesNotContain(CatalogoPermisos.PermisosGestionar, gestor);
        Assert.DoesNotContain(CatalogoPermisos.HogarEliminar, gestor);
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
    public async Task EditarPermisos_RequierePermisosGestionar_ValidaClavesYSeReflejanEnElMiembro()
    {
        var e = await Montar();
        var cuerpo = new ActualizarMiembroRequest(Permisos: [CatalogoPermisos.GastosCrear, CatalogoPermisos.HistorialVer]);

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Admin.PutAsJsonAsync(
            $"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Permisos: ["inventado"]))).StatusCode);

        var ok = await e.Admin.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", cuerpo);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal([CatalogoPermisos.GastosCrear, CatalogoPermisos.HistorialVer], (await ok.Content.ReadFromJsonAsync<MiembroDto>(Web))!.Permisos);

        var lista = (await e.Admin.GetFromJsonAsync<List<MiembroDto>>("/api/miembros", Web))!;
        Assert.Equal(CatalogoPermisos.Todos.Count, lista.Single(m => m.Id == e.AdminId).Permisos!.Count);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.GetAsync("/api/auditoria")).StatusCode); // ya lo tiene
    }

    [Fact]
    public async Task Permisos_SeAsignanTambienAUnAdmin_YSiempreQuedaQuienPuedaGestionarlos()
    {
        var e = await Montar();
        // Con Beto sin el permiso, Ana es la única que puede cambiar permisos: no se lo puede quitar.
        var sinGestionar = new ActualizarMiembroRequest(Permisos: [CatalogoPermisos.GastosCrear]);
        var rechazo = await e.Admin.PutAsJsonAsync($"/api/miembros/{e.AdminId}", sinGestionar);
        Assert.Equal(HttpStatusCode.Conflict, rechazo.StatusCode);
        Assert.Contains("cambiar los permisos", await rechazo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Admin.PutAsJsonAsync($"/api/miembros/{e.AdminId}", new ActualizarMiembroRequest(Activo: false))).StatusCode);

        // Beto recibe el permiso: ahora Ana sí puede quedarse con menos (aunque siga siendo rol admin).
        Assert.Equal(HttpStatusCode.OK, (await e.Admin.PutAsJsonAsync(
            $"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Permisos: [CatalogoPermisos.PermisosGestionar]))).StatusCode);
        var ana = await e.Admin.PutAsJsonAsync($"/api/miembros/{e.AdminId}", sinGestionar);
        Assert.Equal(HttpStatusCode.OK, ana.StatusCode);
        var dto = (await ana.Content.ReadFromJsonAsync<MiembroDto>(Web))!;
        Assert.Equal("admin", dto.Rol);
        Assert.Equal([CatalogoPermisos.GastosCrear], dto.Permisos);

        // Y ya no puede hacer lo que perdió, mientras que Beto lo hace por ella.
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Admin.PutAsJsonAsync(
            $"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Permisos: []))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.PutAsJsonAsync(
            $"/api/miembros/{e.AdminId}", new ActualizarMiembroRequest(Permisos: [CatalogoPermisos.PermisosGestionar]))).StatusCode);
    }

    [Fact]
    public async Task CambiarElRol_VuelveALaPlantillaDelRolNuevo()
    {
        var e = await Montar(CatalogoPermisos.GastosCrear);

        var promo = await e.Admin.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Rol: "admin"));
        Assert.Equal(CatalogoPermisos.Todos.Count, (await promo.Content.ReadFromJsonAsync<MiembroDto>(Web))!.Permisos!.Count);

        var baja = await e.Admin.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Rol: "miembro"));
        Assert.Equal(CatalogoPermisos.PorDefecto.Order(), (await baja.Content.ReadFromJsonAsync<MiembroDto>(Web))!.Permisos!.Order());
    }

    [Fact]
    public async Task GestionDeMiembrosInvitacionesYFunciones_SeAsignanPorPermiso()
    {
        var e = await Montar(); // Beto sin ninguna de estas capacidades
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Cris", "adulto"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync("/api/cuenta-comun/ahorro/activacion", new ActivarAhorroRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync($"/api/miembros/{e.AdminId}", new ActualizarMiembroRequest(Nombre: "X"))).StatusCode);
        // Renombrarse a sí mismo lo puede cualquiera.
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Nombre: "Beto B"))).StatusCode);

        await e.Admin.PutAsJsonAsync($"/api/miembros/{e.MiembroId}", new ActualizarMiembroRequest(Permisos:
            [CatalogoPermisos.MiembrosGestionar, CatalogoPermisos.InvitacionesCrear, CatalogoPermisos.HogarFunciones]));

        Assert.Equal(HttpStatusCode.Created, (await e.Miembro.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Cris", "adulto"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await e.Miembro.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.PutAsJsonAsync("/api/cuenta-comun/activacion", new ActivarCuentaComunRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Miembro.PutAsJsonAsync($"/api/miembros/{e.AdminId}", new ActualizarMiembroRequest(Nombre: "Ana A"))).StatusCode);
        // Gestionar miembros no incluye cambiar permisos.
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.PutAsJsonAsync($"/api/miembros/{e.AdminId}", new ActualizarMiembroRequest(Permisos: []))).StatusCode);
    }

    [Fact]
    public async Task EliminarHogar_ExigePermisoYElNombreExacto_YDejaAlUsuarioSinHogar()
    {
        var e = await Montar(CatalogoPermisos.PermisosGestionar); // Beto puede gestionar permisos, pero no eliminar

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Miembro.DeleteAsync("/api/hogar?nombre=Casa")).StatusCode);
        var sinNombre = await e.Admin.DeleteAsync("/api/hogar");
        Assert.Equal(HttpStatusCode.BadRequest, sinNombre.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Admin.DeleteAsync("/api/hogar?nombre=casa")).StatusCode); // distingue mayúsculas
        Assert.Single((await e.Admin.GetFromJsonAsync<List<HogarResumen>>("/api/hogares", Web))!); // sigue ahí

        Assert.Equal(HttpStatusCode.NoContent, (await e.Admin.DeleteAsync("/api/hogar?nombre=Casa")).StatusCode);
        Assert.Empty((await e.Admin.GetFromJsonAsync<List<HogarResumen>>("/api/hogares", Web))!);
    }

    [Fact]
    public async Task EliminarHogar_SePuedeAsignarAUnMiembro()
    {
        var e = await Montar(CatalogoPermisos.HogarEliminar);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Miembro.DeleteAsync("/api/hogar?nombre=Casa")).StatusCode);
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
