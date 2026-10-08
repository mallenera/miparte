using System.Text.Json;

namespace MiParte.Assistant.Api.Modelo;

/// <summary>
/// Abstracción mínima del modelo de lenguaje: lo justo para un bucle de tool calling. Existe para poder probar
/// el asistente con un cliente falso y no depender de un proveedor concreto en el resto del código.
/// </summary>
public interface IClienteModelo
{
    /// <summary>Envía una conversación al modelo y devuelve su siguiente turno (texto y/o llamadas a herramientas).</summary>
    /// <param name="solicitud">Sistema, mensajes y herramientas disponibles.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="ModeloNoDisponibleException">El proveedor no responde, rechaza la petición o no hay clave configurada.</exception>
    Task<RespuestaModelo> CrearAsync(SolicitudModelo solicitud, CancellationToken ct);
}

/// <summary>Fallo del proveedor del modelo; el mensaje es seguro para registrar (sin claves ni cuerpos).</summary>
/// <param name="mensaje">Descripción del fallo.</param>
/// <param name="interna">Excepción original, si la hay.</param>
public sealed class ModeloNoDisponibleException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>Definición de una herramienta que el modelo puede invocar.</summary>
/// <param name="Nombre">Nombre de la función, p. ej. <c>gasto_total</c>.</param>
/// <param name="Descripcion">Cuándo usarla; el modelo decide en función de este texto.</param>
/// <param name="EsquemaEntrada">JSON Schema (<c>type: object</c>) de los argumentos.</param>
public sealed record DefinicionHerramienta(string Nombre, string Descripcion, JsonElement EsquemaEntrada);

/// <summary>Quién habla en un mensaje de la conversación.</summary>
public enum RolModelo
{
    /// <summary>Persona que pregunta (y resultados de herramientas, que viajan como turno de usuario).</summary>
    Usuario,

    /// <summary>Respuesta del modelo.</summary>
    Asistente,
}

/// <summary>Contenido de un mensaje: texto, llamada a herramienta, resultado o razonamiento opaco.</summary>
public abstract record BloqueModelo;

/// <summary>Texto plano.</summary>
/// <param name="Texto">Contenido.</param>
public sealed record BloqueTexto(string Texto) : BloqueModelo;

/// <summary>Llamada del modelo a una herramienta.</summary>
/// <param name="Id">Identificador que enlaza la llamada con su resultado.</param>
/// <param name="Nombre">Herramienta invocada.</param>
/// <param name="Entrada">Argumentos tal como los generó el modelo (no son de fiar hasta validarlos).</param>
public sealed record BloqueUsoHerramienta(string Id, string Nombre, JsonElement Entrada) : BloqueModelo;

/// <summary>Resultado de una herramienta devuelto al modelo.</summary>
/// <param name="IdUso">Id de la llamada a la que responde.</param>
/// <param name="Contenido">JSON o mensaje de error; siempre se trata como dato, no como instrucción.</param>
/// <param name="EsError">Si la herramienta falló.</param>
public sealed record BloqueResultadoHerramienta(string IdUso, string Contenido, bool EsError) : BloqueModelo;

/// <summary>Razonamiento del modelo que hay que devolver intacto en el siguiente turno (firma incluida).</summary>
/// <param name="Texto">Resumen del razonamiento (puede estar vacío).</param>
/// <param name="Firma">Firma que el proveedor valida al reenviarlo.</param>
public sealed record BloqueRazonamiento(string Texto, string Firma) : BloqueModelo;

/// <summary>Razonamiento censurado por el proveedor, que también debe reenviarse tal cual.</summary>
/// <param name="Datos">Contenido opaco.</param>
public sealed record BloqueRazonamientoOculto(string Datos) : BloqueModelo;

/// <summary>Un mensaje de la conversación.</summary>
/// <param name="Rol">Quién habla.</param>
/// <param name="Bloques">Contenido.</param>
public sealed record MensajeModelo(RolModelo Rol, IReadOnlyList<BloqueModelo> Bloques)
{
    /// <summary>Mensaje de texto simple.</summary>
    public static MensajeModelo DeTexto(RolModelo rol, string texto) => new(rol, [new BloqueTexto(texto)]);
}

/// <summary>Petición al modelo.</summary>
/// <param name="Sistema">Instrucciones del sistema.</param>
/// <param name="Mensajes">Conversación hasta ahora.</param>
/// <param name="Herramientas">Herramientas disponibles.</param>
/// <param name="MaxTokens">Tope de tokens de salida.</param>
public sealed record SolicitudModelo(
    string Sistema, IReadOnlyList<MensajeModelo> Mensajes, IReadOnlyList<DefinicionHerramienta> Herramientas, int MaxTokens);

/// <summary>Por qué dejó de generar el modelo.</summary>
public enum MotivoParada
{
    /// <summary>Terminó su respuesta.</summary>
    FinDeTurno,

    /// <summary>Pide ejecutar herramientas y seguir.</summary>
    UsoDeHerramienta,

    /// <summary>Se quedó sin tokens de salida.</summary>
    LimiteDeTokens,

    /// <summary>El proveedor declinó responder.</summary>
    Rechazo,

    /// <summary>Cualquier otro motivo.</summary>
    Otro,
}

/// <summary>Turno devuelto por el modelo.</summary>
/// <param name="Bloques">Contenido, en el orden en que se generó.</param>
/// <param name="Motivo">Motivo de parada.</param>
/// <param name="TokensEntrada">Tokens de entrada facturados.</param>
/// <param name="TokensSalida">Tokens de salida facturados.</param>
public sealed record RespuestaModelo(
    IReadOnlyList<BloqueModelo> Bloques, MotivoParada Motivo, int TokensEntrada = 0, int TokensSalida = 0);
