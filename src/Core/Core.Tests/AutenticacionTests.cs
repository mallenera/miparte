using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MiParte.Auth;
using MiParte.Core.Domain.Entidades;
using MiParte.Core.Infrastructure.Persistencia;

namespace MiParte.Core.Tests;

public class AutenticacionTests
{
    private const string Url = "https://proyecto.supabase.co";
    private const string Emisor = Url + "/auth/v1";
    private const string Secreto = "un-secreto-de-pruebas-de-al-menos-32-bytes!!";

    private sealed class ClaveFija(SecurityKey clave) : ISigningKeyProvider
    {
        public IReadOnlyCollection<SecurityKey> ObtenerClaves(string? kid) => kid == clave.KeyId ? [clave] : [];
    }

    private static WebApplicationFactory<Program> Crear(string? secreto, ISigningKeyProvider? claves = null)
    {
        var bd = Guid.NewGuid().ToString();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", "Host=no-se-usa");
            b.UseSetting("Supabase:Url", Url);
            if (secreto is not null) b.UseSetting("Supabase:JwtSecret", secreto);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<DbContextOptions<MiParteDbContext>>();
                s.AddDbContext<MiParteDbContext>(o => o.UseInMemoryDatabase(bd));
                if (claves is not null) s.Replace(ServiceDescriptor.Singleton(claves));
            });
        });
    }

    private static async Task Miembro(WebApplicationFactory<Program> f, Guid user, Guid hogar)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiParteDbContext>();
        db.Miembros.Add(new Miembro { Id = Guid.NewGuid(), HogarId = hogar, Nombre = "M", Tipo = TipoMiembro.Adulto, UserId = user });
        await db.SaveChangesAsync();
    }

    private static string Token(SigningCredentials cred, Guid user, string issuer = Emisor,
        string audience = "authenticated", DateTime? expira = null)
    {
        var d = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", user.ToString())]),
            Issuer = issuer,
            Audience = audience,
            Expires = expira ?? DateTime.UtcNow.AddMinutes(10),
            NotBefore = (expira ?? DateTime.UtcNow.AddMinutes(10)).AddHours(-1),
            SigningCredentials = cred,
        };
        return new JsonWebTokenHandler().CreateToken(d);
    }

    private static SigningCredentials Hs256(string secreto = Secreto) =>
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto)), SecurityAlgorithms.HmacSha256);

    private static HttpClient Cliente(WebApplicationFactory<Program> f, string? token, Guid? hogar = null)
    {
        var c = f.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (hogar is not null) c.DefaultRequestHeaders.Add(HogarActualMiddleware_Cabecera, hogar.ToString());
        return c;
    }

    private const string HogarActualMiddleware_Cabecera = "X-Hogar-Id";
    private sealed record Yo(string? UserId, Guid? HogarId);

    [Fact]
    public async Task SinToken_401()
    {
        using var f = Crear(Secreto);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, null).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task Health_NoRequiereToken()
    {
        using var f = Crear(Secreto);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(f, null).GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task TokenHs256Valido_DevuelveUsuarioYHogar()
    {
        using var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var hogar = Guid.NewGuid();
        await Miembro(f, user, hogar);

        var r = await Cliente(f, Token(Hs256(), user)).GetAsync("/api/yo");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var yo = await r.Content.ReadFromJsonAsync<Yo>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(user.ToString(), yo!.UserId);
        Assert.Equal(hogar, yo.HogarId);
    }

    [Fact]
    public async Task UsuarioSinHogar_AutenticadoPeroSinHogar()
    {
        using var f = Crear(Secreto);
        var r = await Cliente(f, Token(Hs256(), Guid.NewGuid())).GetAsync("/api/yo");
        var yo = await r.Content.ReadFromJsonAsync<Yo>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(yo!.HogarId);
    }

    [Fact]
    public async Task TokenCaducado_401()
    {
        using var f = Crear(Secreto);
        var t = Token(Hs256(), Guid.NewGuid(), expira: DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
    }

    [Theory]
    [InlineData("https://otro.supabase.co/auth/v1", "authenticated")]
    [InlineData(Emisor, "anon")]
    public async Task EmisorOAudienciaIncorrectos_401(string issuer, string audience)
    {
        using var f = Crear(Secreto);
        var t = Token(Hs256(), Guid.NewGuid(), issuer, audience);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task FirmaConOtroSecreto_401()
    {
        using var f = Crear(Secreto);
        var t = Token(Hs256("otro-secreto-distinto-de-32-bytes-o-mas!!"), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task SinConfiguracion_NingunTokenEsValido()
    {
        using var f = Crear(null, new ClaveFija(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k" }));
        var t = Token(Hs256(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task TokenAsimetricoRs256ConJwks_Valido()
    {
        var rsa = RSA.Create(2048);
        var clave = new RsaSecurityKey(rsa) { KeyId = "k1" };
        using var f = Crear(null, new ClaveFija(clave));
        var user = Guid.NewGuid();
        var hogar = Guid.NewGuid();
        await Miembro(f, user, hogar);

        var cred = new SigningCredentials(clave, SecurityAlgorithms.RsaSha256);
        var r = await Cliente(f, Token(cred, user)).GetAsync("/api/yo");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task TokenAsimetricoConClaveDesconocida_401()
    {
        var conocida = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" };
        var intrusa = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" };
        using var f = Crear(null, new ClaveFija(conocida));

        var t = Token(new SigningCredentials(intrusa, SecurityAlgorithms.RsaSha256), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task ConfusionDeAlgoritmo_Hs256FirmadoConLaClavePublica_401()
    {
        // Modo JWKS: un token HS256 no debe aceptarse aunque se firme con material público.
        var rsa = RSA.Create(2048);
        var clave = new RsaSecurityKey(rsa) { KeyId = "k1" };
        using var f = Crear(null, new ClaveFija(clave));
        var publico = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var hs = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(publico.PadRight(40, 'x'))), SecurityAlgorithms.HmacSha256);

        var t = Token(hs, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task VariosHogares_SinCabecera_409_ConCabecera_200()
    {
        using var f = Crear(Secreto);
        var user = Guid.NewGuid();
        var h1 = Guid.NewGuid();
        var h2 = Guid.NewGuid();
        await Miembro(f, user, h1);
        await Miembro(f, user, h2);
        var t = Token(Hs256(), user);

        Assert.Equal(HttpStatusCode.Conflict, (await Cliente(f, t).GetAsync("/api/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(f, t, h2).GetAsync("/api/yo")).StatusCode);
    }

    [Fact]
    public async Task CabeceraConHogarAjeno_403()
    {
        using var f = Crear(Secreto);
        var user = Guid.NewGuid();
        await Miembro(f, user, Guid.NewGuid());
        var t = Token(Hs256(), user);

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(f, t, Guid.NewGuid()).GetAsync("/api/yo")).StatusCode);
    }
}
