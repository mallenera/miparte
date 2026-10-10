using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Tests;

/// <summary>Almacén en memoria que sustituye a <c>localStorage</c>.</summary>
internal sealed class AlmacenMemoria : IAlmacenLocal
{
    public Dictionary<string, string> Datos { get; } = new();

    public Task<string?> LeerAsync(string clave) => Task.FromResult(Datos.TryGetValue(clave, out var v) ? v : null);

    public Task EscribirAsync(string clave, string valor)
    {
        Datos[clave] = valor;
        return Task.CompletedTask;
    }

    public Task EliminarAsync(string clave)
    {
        Datos.Remove(clave);
        return Task.CompletedTask;
    }
}

/// <summary>Manejador HTTP que delega en una función y registra las peticiones recibidas.</summary>
internal sealed class ManejadorFalso : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Peticiones { get; } = new();

    public ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public static ManejadorFalso Json(HttpStatusCode codigo, string json) => new(_ => RespuestaJson(codigo, json));

    public static HttpResponseMessage RespuestaJson(HttpStatusCode codigo, string json) =>
        new(codigo) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Peticiones.Add(request);
        return Task.FromResult(_responder(request));
    }
}

/// <summary>Registro de <see cref="MiParte.Web.Hogares.ServicioPermisos"/> en las pruebas de componentes.</summary>
internal static class PermisosDePrueba
{
    /// <summary>Registra el servicio con los permisos indicados (todos por defecto, como un admin) para no depender de <c>/api/miembros</c>.</summary>
    public static MiParte.Web.Hogares.ServicioPermisos Registrar(
        Microsoft.Extensions.DependencyInjection.IServiceCollection servicios, MiParte.Web.Api.CoreApiClient api,
        MiParte.Web.Hogares.EstadoHogar hogar, IEnumerable<string>? claves = null)
    {
        var permisos = new MiParte.Web.Hogares.ServicioPermisos(api, hogar);
        permisos.Fijar(claves ?? MiParte.Core.Domain.CatalogoPermisos.Todos.Select(p => p.Clave));
        servicios.AddSingleton(permisos);
        return permisos;
    }
}
