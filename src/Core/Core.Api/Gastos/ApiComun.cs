using MiParte.Contracts;
using MiParte.Core.Application;

namespace MiParte.Core.Api.Gastos;

/// <summary>
/// Utilidades HTTP compartidas por los endpoints de gastos, recurrentes, liquidación, categorías y perfiles: parseo de formatos de
/// la API y traducción de resultados a respuestas. Las reglas de negocio viven en Core.Domain y Core.Application.
/// </summary>
internal static class ApiComun
{
    /// <summary>Parsea "YYYY-MM" estricto; devuelve el primer día del mes.</summary>
    public static bool TryMes(string? mes, out DateOnly inicio) => FormatosApi.TryMes(mes, out inicio);

    /// <summary>Formatea el primer día de un mes como "YYYY-MM".</summary>
    public static string FormatoMes(DateOnly inicio) => FormatosApi.FormatoMes(inicio);

    /// <summary>Respuesta 400 con el mensaje de error indicado.</summary>
    public static IResult Invalido(string mensaje) => Results.BadRequest(new { error = mensaje });

    /// <summary>Respuesta 400 por mes con formato distinto de YYYY-MM.</summary>
    public static IResult MesInvalido() => Invalido("El mes debe tener el formato YYYY-MM.");

    /// <summary>Parsea una fecha con el formato exacto "YYYY-MM-DD".</summary>
    public static bool TryFecha(string? texto, out DateOnly fecha) => FormatosApi.TryFecha(texto, out fecha);

    /// <summary>Respuesta 400 por una fecha con formato distinto de YYYY-MM-DD; <paramref name="parametro"/> es el nombre del parámetro.</summary>
    public static IResult FechaInvalida(string parametro) => Invalido($"La fecha «{parametro}» debe tener el formato YYYY-MM-DD.");

    /// <summary>Traduce el fallo de un caso de uso a su respuesta HTTP: 400, 404 o 409 (con los datos extra, como lo pendiente).</summary>
    public static IResult Respuesta(ErrorAplicacion error) => error.Tipo switch
    {
        TipoError.NoEncontrado => Results.NotFound(),
        TipoError.Conflicto => Results.Conflict(
            new Dictionary<string, object>(error.Extra ?? new Dictionary<string, object>()) { ["error"] = error.Mensaje }),
        TipoError.Prohibido => Results.Json(new { error = error.Mensaje }, statusCode: StatusCodes.Status403Forbidden),
        _ => Invalido(error.Mensaje),
    };

    /// <summary>Respuesta 409 cuando la petición no tiene hogar seleccionado.</summary>
    public static IResult SinHogar() => Results.Conflict(new { error = "No hay hogar seleccionado." });
}
