namespace MiParte.Web.Autenticacion;

/// <summary>Error de Supabase Auth con un mensaje ya traducido para mostrar al usuario.</summary>
public sealed class AuthException : Exception
{
    /// <summary>Código HTTP devuelto por Supabase Auth.</summary>
    public int CodigoHttp { get; }

    /// <summary>Crea la excepción.</summary>
    /// <param name="mensaje">Mensaje para el usuario.</param>
    /// <param name="codigoHttp">Código HTTP de la respuesta.</param>
    public AuthException(string mensaje, int codigoHttp) : base(mensaje) => CodigoHttp = codigoHttp;
}
