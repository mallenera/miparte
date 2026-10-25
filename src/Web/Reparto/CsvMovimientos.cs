using System.Globalization;
using System.Text;
using MiParte.Contracts;

namespace MiParte.Web.Reparto;

/// <summary>
/// CSV de los movimientos de la cuenta común, para cruzarlo en una hoja de cálculo con el extracto del banco. Va en el formato que
/// abre Excel en español sin asistente: separador «;», coma decimal y UTF-8 con BOM (sin él, Excel estropea las tildes).
/// </summary>
public static class CsvMovimientos
{
    private static readonly CultureInfo Es = new("es-ES");

    /// <summary>Cabecera de columnas.</summary>
    public const string Cabecera = "Fecha;Tipo;Descripción;Persona;Concepto;Importe;Ahorro;Saldo";

    /// <summary>Nombre del tipo de movimiento en singular, como se escribe en la columna «Tipo».</summary>
    /// <param name="tipo">Tipo tal como viaja en la API.</param>
    public static string NombreTipo(string tipo) => tipo switch
    {
        "saldo_inicial" => "Saldo inicial",
        "aportacion" => "Aportación",
        "ingreso" => "Ingreso",
        "deposito_ahorro" => "Ingreso al ahorro",
        "gasto" => "Gasto",
        "gasto_ahorro" => "Gasto desde el ahorro",
        "gasto_ahorro_adelantado" => "Gasto desde el ahorro adelantado",
        "reembolso" => "Reembolso",
        "retirada_ahorro" => "Retirada de ahorro",
        _ => tipo,
    };

    /// <summary>Nombre del fichero: el periodo, para no confundir dos descargas.</summary>
    /// <param name="desde">Primer día del periodo.</param>
    /// <param name="hasta">Último día del periodo.</param>
    public static string NombreFichero(DateOnly desde, DateOnly hasta)
        => $"movimientos-cuenta-comun_{desde:yyyy-MM-dd}_{hasta:yyyy-MM-dd}.csv";

    /// <summary>
    /// Genera el CSV, con BOM, de las líneas dadas en orden cronológico (el de un extracto). El saldo de cada línea es el real de la
    /// cuenta tras ella, aunque se exporte una selección filtrada.
    /// </summary>
    /// <param name="movimientos">Líneas a exportar en el orden del servidor (cronológico); dentro de un mismo día se respeta ese orden.</param>
    /// <param name="descripcion">Texto de la columna «Descripción» de cada línea.</param>
    /// <param name="miembros">Miembros del hogar, para la columna «Persona».</param>
    public static string Generar(
        IEnumerable<MovimientoCuentaComunDto> movimientos, Func<MovimientoCuentaComunDto, string> descripcion, IEnumerable<MiembroDto> miembros)
    {
        var nombres = miembros.ToDictionary(m => m.Id, m => m.Nombre);
        var sb = new StringBuilder("\uFEFF").Append(Cabecera).Append("\r\n");
        // OrderBy es estable: dentro de un día se mantiene el orden del servidor (primero las entradas).
        foreach (var m in movimientos.OrderBy(x => x.Fecha))
        {
            sb.Append(m.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).Append(';')
                .Append(Texto(NombreTipo(m.Tipo))).Append(';')
                .Append(Texto(descripcion(m))).Append(';')
                .Append(Texto(m.MiembroId is { } id ? nombres.GetValueOrDefault(id, "") : "")).Append(';')
                .Append(Texto(m.Concepto ?? "")).Append(';')
                .Append(Numero(m.Importe)).Append(';')
                .Append(m.Ahorro != 0 ? Numero(m.Ahorro) : "").Append(';')
                .Append(Numero(m.Saldo)).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Importe con coma decimal, sin separador de miles ni «€», para que la hoja lo lea como número.</summary>
    private static string Numero(decimal importe) => importe.ToString("0.00", Es);

    /// <summary>
    /// Campo de texto escapado. Entre comillas si lleva «;», comillas o saltos de línea. Un texto que empieza por «=», «+», «-» o «@»
    /// lleva delante un apóstrofo: el concepto lo escribe cualquiera del hogar y la hoja lo ejecutaría como fórmula.
    /// </summary>
    private static string Texto(string valor)
    {
        if (valor.Length > 0 && valor[0] is '=' or '+' or '-' or '@' or '\t' or '\r') valor = "'" + valor;
        return valor.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
    }
}
