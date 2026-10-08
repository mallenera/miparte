using System.Net;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Tests;

/// <summary>Servidor falso de Core.Api para las pruebas de componentes: responde por método y ruta y registra lo recibido.</summary>
internal sealed class ApiFalsa : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _rutas = new();

    /// <summary>Peticiones recibidas como «MÉTODO ruta».</summary>
    public List<string> Recibidas { get; } = new();

    /// <summary>Rutas con su cadena de consulta (<c>/api/auditoria?limite=50</c>), en el orden recibido.</summary>
    public List<string> Consultas { get; } = new();

    /// <summary>Cuerpos JSON recibidos por «MÉTODO ruta» (última petición de cada una).</summary>
    public Dictionary<string, string> Cuerpos { get; } = new();

    public ApiFalsa Responde(string metodoYRuta, HttpStatusCode codigo, object? cuerpo = null)
    {
        _rutas[metodoYRuta] = _ => Respuesta(codigo, cuerpo);
        return this;
    }

    public ApiFalsa Error(string metodoYRuta, HttpStatusCode codigo, string mensaje) =>
        Responde(metodoYRuta, codigo, new { error = mensaje });

    private static HttpResponseMessage Respuesta(HttpStatusCode codigo, object? cuerpo) =>
        cuerpo is null
            ? new HttpResponseMessage(codigo)
            : new HttpResponseMessage(codigo) { Content = new StringContent(JsonSerializer.Serialize(cuerpo, Web), System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clave = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        Recibidas.Add(clave);
        Consultas.Add(Uri.UnescapeDataString(request.RequestUri.PathAndQuery));
        if (request.Content is not null) Cuerpos[clave] = await request.Content.ReadAsStringAsync(cancellationToken);
        return _rutas.TryGetValue(clave, out var f)
            ? f(request)
            : Respuesta(HttpStatusCode.NotFound, new { error = $"Sin ruta falsa para {clave}" });
    }

    public static MiembroDto Miembro(string nombre, string rol = "miembro", bool esYo = false, bool vinculado = true,
        string tipo = "adulto", Guid? responsable = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), nombre, tipo, responsable, true, rol, vinculado, esYo);
}
