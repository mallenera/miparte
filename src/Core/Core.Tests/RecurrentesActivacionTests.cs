using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Contracts;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;
using static MiParte.Core.Tests.AutenticacionTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Interruptor de gastos recurrentes del hogar: apagado no se crean plantillas ni se generan gastos, pero las plantillas existentes
/// se conservan (pausadas) y se siguen listando.
/// </summary>
public class RecurrentesActivacionTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string MensajeInactivas = "Las recurrentes no están activadas en este hogar.";

    private sealed record Escenario(HttpClient Cliente, WebApplicationFactory<Program> F, Guid Categoria, Guid Ana, Guid Perfil);

    private static async Task<Escenario> Montar(bool recurrentesActivas, bool admin = true)
    {
        var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var hogar = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var cat = Guid.NewGuid();
        var perfil = Guid.NewGuid();

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
            db.Hogares.Add(new Hogar { Id = hogar, Nombre = "Casa", RecurrentesActivas = recurrentesActivas });
            db.Miembros.Add(new Miembro
            {
                Id = ana, HogarId = hogar, Nombre = "Ana", Tipo = TipoMiembro.Adulto, UserId = user,
                Rol = admin ? RolMiembro.Admin : RolMiembro.Miembro,
            });
            db.Categorias.Add(new Categoria { Id = cat, HogarId = hogar, Nombre = "Vivienda" });
            db.PerfilesReparto.Add(new PerfilReparto { Id = perfil, HogarId = hogar, Nombre = "Individual", Modo = ModoReparto.Individual });
            await db.SaveChangesAsync();
        }

        return new Escenario(Cliente(f, Token(Hs256(), user)), f, cat, ana, perfil);
    }

    private static GastoRecurrenteRequest Plantilla(Escenario e)
        => new(60m, e.Categoria, e.Ana, e.Perfil, 5, "Internet");

    private static async Task<string?> ErrorDe(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("error").GetString();

    [Fact]
    public async Task Desactivadas_BloqueanCrearYGenerar_conConflicto()
    {
        var e = await Montar(recurrentesActivas: false);

        var crear = await e.Cliente.PostAsJsonAsync("/api/gastos-recurrentes", Plantilla(e));
        Assert.Equal(HttpStatusCode.Conflict, crear.StatusCode);
        Assert.Equal(MensajeInactivas, await ErrorDe(crear));

        var generar = await e.Cliente.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null);
        Assert.Equal(HttpStatusCode.Conflict, generar.StatusCode);
        Assert.Equal(MensajeInactivas, await ErrorDe(generar));

        var yo = await e.Cliente.GetFromJsonAsync<YoResponse>("/api/yo", Web);
        Assert.False(yo!.HogarActual!.RecurrentesActivas);
    }

    [Fact]
    public async Task Desactivar_NoBorraLasPlantillas_yReactivarDesbloqueaLaGeneracion()
    {
        var e = await Montar(recurrentesActivas: true);
        Assert.Equal(HttpStatusCode.Created, (await e.Cliente.PostAsJsonAsync("/api/gastos-recurrentes", Plantilla(e))).StatusCode);

        var apagar = await e.Cliente.PutAsJsonAsync("/api/hogar/recurrentes", new ActivarRecurrentesRequest(false));
        Assert.Equal(HttpStatusCode.OK, apagar.StatusCode);

        // Las consultas siguen funcionando y la plantilla no se pierde.
        var lista = await e.Cliente.GetFromJsonAsync<List<GastoRecurrenteResponse>>("/api/gastos-recurrentes", Web);
        Assert.Single(lista!);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Cliente.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await e.Cliente.PutAsJsonAsync("/api/hogar/recurrentes", new ActivarRecurrentesRequest(true))).StatusCode);
        var generar = await e.Cliente.PostAsync("/api/gastos-recurrentes/generar?mes=2026-09", null);
        Assert.Equal(HttpStatusCode.OK, generar.StatusCode);
        Assert.Equal(1, (await generar.Content.ReadFromJsonAsync<GenerarRecurrentesResponse>(Web))!.Creados);
    }

    [Fact]
    public async Task Activar_soloConPermisoHogarFunciones()
    {
        var noAdmin = await Montar(recurrentesActivas: false, admin: false);

        var r = await noAdmin.Cliente.PutAsJsonAsync("/api/hogar/recurrentes", new ActivarRecurrentesRequest(true));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.False((await noAdmin.Cliente.GetFromJsonAsync<YoResponse>("/api/yo", Web))!.HogarActual!.RecurrentesActivas);
    }
}
