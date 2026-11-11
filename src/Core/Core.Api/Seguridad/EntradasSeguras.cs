using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiParte.Contracts;

namespace MiParte.Core.Api.Seguridad;

/// <summary>
/// Filtros de entrada comunes a toda la API, aplicados antes de que los datos lleguen a los servicios:
/// <list type="bullet">
/// <item>El carácter NUL (<c>U+0000</c>): PostgreSQL no lo admite en un texto y la petición acababa en un 500 al guardar (o al usarlo
/// como parámetro de una consulta). Se rechaza con 400, en el cuerpo JSON y en la query string. También llegaba desde la ruta anónima de
/// la lista de la compra, así que cualquiera con un enlace QR podía llenar el log de errores.</item>
/// <item>Fechas del cuerpo JSON fuera de <see cref="FormatosApi.AnioMinimo"/>-<see cref="FormatosApi.AnioMaximo"/>: un gasto del año
/// 9999 se guardaba y después cualquier cálculo del mes siguiente desbordaba.</item>
/// </list>
/// Un JSON inválido responde 400, como cualquier cuerpo que no se puede leer.
/// </summary>
public static class EntradasSeguras
{
    /// <summary>Registra los conversores JSON que rechazan textos con NUL y fechas fuera de rango.</summary>
    /// <param name="builder">Constructor de la aplicación.</param>
    public static WebApplicationBuilder AddEntradasSeguras(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new TextoSinNulo());
            o.SerializerOptions.Converters.Add(new FechaEnRango());
        });
        return builder;
    }

    /// <summary>Responde 400 si algún parámetro de la query string contiene el carácter NUL.</summary>
    /// <param name="app">Aplicación.</param>
    public static IApplicationBuilder UseEntradasSeguras(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Query.Any(p => p.Key.Contains('\0') || p.Value.Any(v => v?.Contains('\0') == true)))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { error = "La petición contiene caracteres no válidos." });
                return;
            }
            await next();
        });

    /// <summary>Lee textos JSON rechazando el carácter NUL; escribe como el conversor por defecto.</summary>
    internal sealed class TextoSinNulo : JsonConverter<string>
    {
        /// <inheritdoc />
        public override bool HandleNull => false;

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var texto = reader.GetString();
            return texto is not null && texto.Contains('\0') ? throw new JsonException("El texto contiene el carácter NUL.") : texto;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    /// <summary>Lee fechas «YYYY-MM-DD» con año dentro del rango de la API; escribe en el mismo formato que el conversor por defecto.</summary>
    internal sealed class FechaEnRango : JsonConverter<DateOnly>
    {
        /// <inheritdoc />
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String && FormatosApi.TryFecha(reader.GetString(), out var fecha)
                ? fecha
                : throw new JsonException(
                    $"La fecha debe tener el formato YYYY-MM-DD y un año entre {FormatosApi.AnioMinimo} y {FormatosApi.AnioMaximo}.");

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
