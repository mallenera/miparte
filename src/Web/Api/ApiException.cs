using System.Net;

namespace MiParte.Web.Api;

/// <summary>Error devuelto por Core.Api, con el mensaje en español del cuerpo <c>{ "error": ... }</c> si lo trae.</summary>
public sealed class ApiException : Exception
{
    /// <summary>Código HTTP de la respuesta.</summary>
    public HttpStatusCode Codigo { get; }

    /// <summary>Crea la excepción.</summary>
    /// <param name="codigo">Código HTTP de la respuesta.</param>
    /// <param name="mensaje">Mensaje para el usuario.</param>
    public ApiException(HttpStatusCode codigo, string mensaje) : base(mensaje) => Codigo = codigo;
}
