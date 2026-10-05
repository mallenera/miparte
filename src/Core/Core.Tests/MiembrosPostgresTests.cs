using System.Net;
using System.Net.Http.Json;
using MiParte.Contracts;
using static MiParte.Core.Tests.ApiPostgresTests;

namespace MiParte.Core.Tests;

/// <summary>
/// Concurrencia de miembros e invitaciones contra PostgreSQL real (InMemory no serializa transacciones).
/// Se saltan sin MIPARTE_TEST_DB; en CI siempre se ejecutan.
/// </summary>
public class MiembrosPostgresTests
{
    [SkippableFact]
    public async Task DosAdminsSeDegradanAlaVez_ElHogarConservaUnAdmin_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(ApiPostgresTests.Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var ana = await e.CrearUsuarioAsync();
        var beto = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(ana), "Casa admins", "Ana");

        // Beto entra por invitación y se promociona a admin.
        var inv = await Leer<InvitacionCreada>(
            await e.Cliente(ana, hogar.Id).PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(null)),
            HttpStatusCode.Created);
        await Leer<HogarResumen>(await e.Cliente(beto).PostAsJsonAsync(
            "/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv.Token, "Beto")));
        var miembros = await Leer<List<MiembroDto>>(await e.Cliente(ana, hogar.Id).GetAsync("/api/miembros"));
        var anaId = miembros.Single(m => m.Nombre == "Ana").Id;
        var betoId = miembros.Single(m => m.Nombre == "Beto").Id;
        await Leer<MiembroDto>(await e.Cliente(ana, hogar.Id).PutAsJsonAsync(
            $"/api/miembros/{betoId}", new ActualizarMiembroRequest(Rol: "admin")));

        // Cada admin degrada al otro a la vez (sin la serialización ambos verían un admin restante).
        var rs = await Task.WhenAll(
            e.Cliente(ana, hogar.Id).PutAsJsonAsync($"/api/miembros/{betoId}", new ActualizarMiembroRequest(Rol: "miembro")),
            e.Cliente(beto, hogar.Id).PutAsJsonAsync($"/api/miembros/{anaId}", new ActualizarMiembroRequest(Rol: "miembro")));

        // Normalmente 409 (la comprobación ve el cambio del otro); 403 si la petición de uno llegó cuando
        // ya lo habían degradado. Lo que nunca puede pasar es que ambas tengan éxito.
        Assert.Contains(rs, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
        Assert.DoesNotContain(rs, r => r.StatusCode != HttpStatusCode.OK
            && r.StatusCode != HttpStatusCode.Conflict && r.StatusCode != HttpStatusCode.Forbidden);
        var final = await Leer<List<MiembroDto>>(await e.Cliente(ana, hogar.Id).GetAsync("/api/miembros"));
        Assert.True(final.Count(m => m.Rol == "admin" && m.Activo && m.Vinculado) >= 1);
    }

    [SkippableFact]
    public async Task DosInvitacionesAlMismoMiembro_SoloUnaSeAcepta_EnPostgres()
    {
        Skip.If(string.IsNullOrEmpty(ApiPostgresTests.Cadena), "MIPARTE_TEST_DB no definida");
        await using var e = new Entorno();
        var admin = await e.CrearUsuarioAsync();
        var u1 = await e.CrearUsuarioAsync();
        var u2 = await e.CrearUsuarioAsync();
        var hogar = await CrearHogar(e.Cliente(admin), "Casa vínculo", "Ana");
        var ca = e.Cliente(admin, hogar.Id);

        var miembro = await Leer<MiembroDto>(
            await ca.PostAsJsonAsync("/api/miembros", new CrearMiembroRequest("Pendiente", "adulto")),
            HttpStatusCode.Created);
        var inv1 = await Leer<InvitacionCreada>(
            await ca.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(miembro.Id)), HttpStatusCode.Created);
        var inv2 = await Leer<InvitacionCreada>(
            await ca.PostAsJsonAsync("/api/invitaciones", new CrearInvitacionRequest(miembro.Id)), HttpStatusCode.Created);

        var rs = await Task.WhenAll(
            e.Cliente(u1).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv1.Token)),
            e.Cliente(u2).PostAsJsonAsync("/api/invitaciones/aceptar", new AceptarInvitacionRequest(inv2.Token)));

        Assert.Equal(1, rs.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, rs.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // El miembro queda vinculado a un único usuario (el hogar sigue en pie: 2 miembros activos).
        var lista = await Leer<List<MiembroDto>>(await ca.GetAsync("/api/miembros"));
        Assert.True(lista.Single(m => m.Id == miembro.Id).Vinculado);
        var ganador = rs[0].StatusCode == HttpStatusCode.OK ? u1 : u2;
        var perdedor = ganador == u1 ? u2 : u1;
        Assert.Equal(HttpStatusCode.OK, (await e.Cliente(ganador, hogar.Id).GetAsync("/api/miembros")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await e.Cliente(perdedor, hogar.Id).GetAsync("/api/miembros")).StatusCode);
    }
}
