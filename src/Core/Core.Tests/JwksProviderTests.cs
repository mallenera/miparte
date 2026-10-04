using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MiParte.Auth;

namespace MiParte.Core.Tests;

public class JwksProviderTests
{
    private sealed class Manejador(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Peticiones;
        public Uri? Ultima;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticiones++;
            Ultima = request.RequestUri;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static string JwksDe(RSA rsa, string kid)
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = kid });
        return $$"""{"keys":[{"kty":"RSA","kid":"{{kid}}","use":"sig","alg":"RS256","n":"{{jwk.N}}","e":"{{jwk.E}}"}]}""";
    }

    private static JwksSigningKeyProvider Crear(Manejador m) =>
        new(new Fabrica(m), Options.Create(new SupabaseAuthOptions { Url = "https://p.supabase.co/" }),
            NullLogger<JwksSigningKeyProvider>.Instance);

    [Fact]
    public void DescargaElJwksDelProyectoYLoCachea()
    {
        var json = JwksDe(RSA.Create(2048), "k1");
        var m = new Manejador(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        var p = Crear(m);

        var a = p.ObtenerClaves("k1");
        var b = p.ObtenerClaves("k1");

        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal(1, m.Peticiones);
        Assert.Equal("https://p.supabase.co/auth/v1/.well-known/jwks.json", m.Ultima!.ToString());
    }

    [Fact]
    public void KidDesconocido_NoDevuelveClaves_YNoMartilleaAlServidor()
    {
        var json = JwksDe(RSA.Create(2048), "k1");
        var m = new Manejador(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var p = Crear(m);

        p.ObtenerClaves("k1");
        for (var i = 0; i < 5; i++) Assert.Empty(p.ObtenerClaves("otra"));

        Assert.Equal(1, m.Peticiones);
    }

    [Fact]
    public void ErrorDeRed_DevuelveVacio_SinLanzar()
    {
        var m = new Manejador(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Empty(Crear(m).ObtenerClaves("k1"));
    }
}
