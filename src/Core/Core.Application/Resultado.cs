namespace MiParte.Core.Application;

/// <summary>Clase de fallo de un caso de uso; la capa HTTP la traduce a su código (400, 409, 404).</summary>
public enum TipoError { Invalido, Conflicto, NoEncontrado }

/// <summary>Fallo de un caso de uso, con el mensaje en español y, si hace falta, datos extra para el cliente (p. ej. lo pendiente).</summary>
public sealed record ErrorAplicacion(TipoError Tipo, string Mensaje, IReadOnlyDictionary<string, object>? Extra = null);

/// <summary>Resultado de un caso de uso: el valor o un <see cref="ErrorAplicacion"/>.</summary>
public sealed record Resultado<T>
{
    private Resultado() { }

    /// <summary>Valor devuelto; solo tiene sentido si <see cref="Error"/> es null.</summary>
    public T? Valor { get; private init; }

    /// <summary>Fallo del caso de uso; null si fue bien.</summary>
    public ErrorAplicacion? Error { get; private init; }

    /// <summary>Resultado correcto con su valor.</summary>
    public static Resultado<T> Ok(T valor) => new() { Valor = valor };

    /// <summary>Resultado fallido con el error dado.</summary>
    public static Resultado<T> Fallo(ErrorAplicacion error) => new() { Error = error };

    /// <summary>Petición mal formada (400).</summary>
    public static Resultado<T> Invalido(string mensaje) => Fallo(new(TipoError.Invalido, mensaje));

    /// <summary>Choca con el estado actual (409).</summary>
    public static Resultado<T> Conflicto(string mensaje, IReadOnlyDictionary<string, object>? extra = null)
        => Fallo(new(TipoError.Conflicto, mensaje, extra));

    /// <summary>No existe en el hogar (404).</summary>
    public static Resultado<T> NoEncontrado(string mensaje = "No existe.") => Fallo(new(TipoError.NoEncontrado, mensaje));
}
