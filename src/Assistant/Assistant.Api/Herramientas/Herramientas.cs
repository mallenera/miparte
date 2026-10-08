using System.Globalization;
using System.Text;
using System.Text.Json;
using MiParte.Assistant.Api.Hogar;
using MiParte.Assistant.Api.Modelo;
using MiParte.Contracts;

namespace MiParte.Assistant.Api.Herramientas;

/// <summary>Resultado de ejecutar una herramienta: JSON para el modelo o un mensaje de error que el modelo puede explicar.</summary>
/// <param name="Contenido">JSON de datos, o texto del error.</param>
/// <param name="EsError">Si la herramienta no pudo responder (argumentos inválidos, dato inexistente...).</param>
public sealed record ResultadoHerramienta(string Contenido, bool EsError);

/// <summary>
/// Las cinco funciones de solo lectura del asistente (<c>docs/diseno-y-decisiones.md</c>). El modelo no calcula ni
/// escribe consultas libres: elige una función y sus argumentos, el código consulta Core.Api y devuelve cifras
/// ya calculadas. Los argumentos del modelo se validan como entrada no fiable y todo texto procedente de la base
/// de datos (nombres, conceptos) se limpia antes de volver al modelo.
/// </summary>
public static class CatalogoHerramientas
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    private const string CamposPeriodo = """
        "mes": { "type": "integer", "minimum": 1, "maximum": 12, "description": "Número del mes (1 = enero)." },
        "anio": { "type": "integer", "minimum": 2000, "maximum": 2100, "description": "Año de cuatro cifras. Omítelo si la persona no lo dijo: se asume el año en curso y la respuesta lo indica." }
        """;

    /// <summary>Definiciones que se envían al modelo.</summary>
    public static IReadOnlyList<DefinicionHerramienta> Definiciones { get; } =
    [
        Definir("gasto_total",
            "Suma los gastos del hogar de un periodo. Filtros opcionales por categoría y por persona. Sin mes, suma el año entero. "
            + "Úsala para preguntas como cuánto se gastó en alimentación en junio o cuánto pagó Ana este año.",
            $$"""{ "type": "object", "properties": { "categoria": { "type": "string", "description": "Nombre de la categoría (incluye sus subcategorías)." }, "persona": { "type": "string", "description": "Nombre de un miembro del hogar." }, {{CamposPeriodo}} } }"""),
        Definir("gasto_por_categoria",
            "Desglosa el gasto total de un mes por categoría, de mayor a menor. Para preguntas como en qué se fue el dinero en septiembre.",
            $$"""{ "type": "object", "properties": { {{CamposPeriodo}} }, "required": ["mes"] }"""),
        Definir("balance_mes",
            "Balance de un mes por miembro: lo que pagó cada uno, lo que le correspondía asumir y la diferencia. Para preguntas como cómo cerramos el mes.",
            $$"""{ "type": "object", "properties": { {{CamposPeriodo}} }, "required": ["mes"] }"""),
        Definir("liquidacion_mes",
            "Saldos de cada miembro y transferencias sugeridas para saldar el mes (quién debe a quién y cuánto). Ya descuenta los pagos registrados.",
            $$"""{ "type": "object", "properties": { {{CamposPeriodo}} }, "required": ["mes"] }"""),
        Definir("comparar_meses",
            "Compara el gasto de dos meses, en total y por categoría. Para preguntas como si gastamos más que el mes pasado.",
            """
            { "type": "object", "properties": {
              "mes_a": { "type": "integer", "minimum": 1, "maximum": 12, "description": "Primer mes (el de referencia, normalmente el anterior)." },
              "anio_a": { "type": "integer", "minimum": 2000, "maximum": 2100 },
              "mes_b": { "type": "integer", "minimum": 1, "maximum": 12, "description": "Segundo mes (el que se compara, normalmente el actual)." },
              "anio_b": { "type": "integer", "minimum": 2000, "maximum": 2100 } },
              "required": ["mes_a", "mes_b"] }
            """),
    ];

    private static DefinicionHerramienta Definir(string nombre, string descripcion, string esquema)
        => new(nombre, descripcion, JsonDocument.Parse(esquema).RootElement.Clone());

    /// <summary>
    /// Ejecuta la herramienta pedida por el modelo. Nunca lanza por culpa de los argumentos: los problemas de
    /// validación vuelven como <see cref="ResultadoHerramienta"/> con error para que el modelo se lo explique a la persona.
    /// Los fallos de acceso o de Core.Api sí se propagan (<see cref="HogarNoAccesibleException"/>, <see cref="DatosNoDisponiblesException"/>).
    /// </summary>
    /// <param name="nombre">Nombre de la herramienta.</param>
    /// <param name="entrada">Argumentos generados por el modelo.</param>
    /// <param name="datos">Acceso a los datos del hogar de la persona.</param>
    /// <param name="hoy">Fecha actual, para asumir el año cuando falta.</param>
    /// <param name="ct">Token de cancelación.</param>
    public static async Task<ResultadoHerramienta> EjecutarAsync(
        string nombre, JsonElement entrada, IDatosHogar datos, DateOnly hoy, CancellationToken ct)
    {
        try
        {
            if (entrada.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
                return Error("Los argumentos deben ser un objeto.");
            return nombre switch
            {
                "gasto_total" => await GastoTotalAsync(entrada, datos, hoy, ct),
                "gasto_por_categoria" => await PorCategoriaAsync(entrada, datos, hoy, ct),
                "balance_mes" => await BalanceAsync(entrada, datos, hoy, ct),
                "liquidacion_mes" => await LiquidacionAsync(entrada, datos, hoy, ct),
                "comparar_meses" => await CompararAsync(entrada, datos, hoy, ct),
                _ => Error("Herramienta desconocida. Solo existen las funciones de consulta definidas."),
            };
        }
        catch (ArgumentoInvalidoException ex)
        {
            return Error(ex.Message);
        }
    }

    private static async Task<ResultadoHerramienta> GastoTotalAsync(JsonElement e, IDatosHogar datos, DateOnly hoy, CancellationToken ct)
    {
        var mes = Entero(e, "mes", 1, 12);
        var periodo = Periodo.Resolver(mes, Entero(e, "anio", 2000, 2100), hoy);
        var categoriaTexto = Texto(e, "categoria");
        var personaTexto = Texto(e, "persona");

        Guid[]? categorias = null;
        string? categoriaNombre = null;
        if (categoriaTexto is not null)
        {
            var todas = await datos.CategoriasAsync(ct);
            var elegida = Buscar(categoriaTexto, todas, c => c.Nombre, "categoría");
            categoriaNombre = Limpiar(elegida.Nombre);
            categorias = [elegida.Id, .. todas.Where(c => c.CategoriaPadreId == elegida.Id).Select(c => c.Id)];
        }

        MiembroDto? persona = null;
        if (personaTexto is not null)
            persona = Buscar(personaTexto, await datos.MiembrosAsync(ct), m => m.Nombre, "persona");

        var meses = mes is not null ? new[] { periodo.Mes } : Enumerable.Range(1, 12).ToArray();
        decimal total = 0, pagado = 0, asumido = 0;
        var numero = 0;
        foreach (var m in meses)
        {
            foreach (var g in await datos.GastosAsync($"{periodo.Anio:D4}-{m:D2}", ct))
            {
                if (categorias is not null && !categorias.Contains(g.CategoriaId)) continue;
                total += g.Importe;
                numero++;
                if (persona is null) continue;
                if (g.PagadoPor == persona.Id) pagado += g.Importe;
                asumido += g.Repartos.Where(r => r.MiembroId == persona.Id).Sum(r => r.ImporteAsumido);
            }
        }

        var resultado = new Dictionary<string, object?>
        {
            ["periodo"] = mes is not null ? periodo.Texto : periodo.Anio.ToString(CultureInfo.InvariantCulture),
            ["categoria"] = categoriaNombre,
            ["persona"] = persona is null ? null : Limpiar(persona.Nombre),
            ["totalGastos"] = total,
            ["numeroGastos"] = numero,
        };
        if (persona is not null)
        {
            resultado["pagadoPorLaPersona"] = pagado;
            resultado["asumidoPorLaPersona"] = asumido;
        }
        periodo.AnotarSiAsumido(resultado);
        return Datos(resultado);
    }

    private static async Task<ResultadoHerramienta> PorCategoriaAsync(JsonElement e, IDatosHogar datos, DateOnly hoy, CancellationToken ct)
    {
        var periodo = Periodo.Resolver(Entero(e, "mes", 1, 12, obligatorio: true), Entero(e, "anio", 2000, 2100), hoy);
        var resumen = await datos.ResumenAsync(periodo.Texto, ct);
        var resultado = new Dictionary<string, object?>
        {
            ["periodo"] = periodo.Texto,
            ["gastosTotales"] = resumen.GastosTotales,
            ["categorias"] = resumen.Categorias.OrderByDescending(c => c.Total)
                .Select(c => new { categoria = Limpiar(c.Nombre), total = c.Total }).ToList(),
        };
        periodo.AnotarSiAsumido(resultado);
        return Datos(resultado);
    }

    private static async Task<ResultadoHerramienta> BalanceAsync(JsonElement e, IDatosHogar datos, DateOnly hoy, CancellationToken ct)
    {
        var periodo = Periodo.Resolver(Entero(e, "mes", 1, 12, obligatorio: true), Entero(e, "anio", 2000, 2100), hoy);
        var resumen = await datos.ResumenAsync(periodo.Texto, ct);
        var resultado = new Dictionary<string, object?>
        {
            ["periodo"] = periodo.Texto,
            ["gastosTotales"] = resumen.GastosTotales,
            ["miembros"] = resumen.Miembros.Select(m => new
            {
                persona = Limpiar(m.Nombre),
                pagado = m.Pagado,
                asumido = m.Asumido,
                diferencia = m.Pagado - m.Asumido,
            }).ToList(),
            ["nota"] = "diferencia positiva: pagó de más; negativa: pagó de menos.",
        };
        periodo.AnotarSiAsumido(resultado);
        return Datos(resultado);
    }

    private static async Task<ResultadoHerramienta> LiquidacionAsync(JsonElement e, IDatosHogar datos, DateOnly hoy, CancellationToken ct)
    {
        var periodo = Periodo.Resolver(Entero(e, "mes", 1, 12, obligatorio: true), Entero(e, "anio", 2000, 2100), hoy);
        var liquidacion = await datos.LiquidacionAsync(periodo.Texto, ct);
        var nombres = liquidacion.Saldos.ToDictionary(s => s.MiembroId, s => Limpiar(s.Nombre));
        string Nombre(Guid id) => nombres.GetValueOrDefault(id, "(otro miembro)");
        var resultado = new Dictionary<string, object?>
        {
            ["periodo"] = periodo.Texto,
            ["saldos"] = liquidacion.Saldos.Select(s => new { persona = Limpiar(s.Nombre), saldo = s.Saldo }).ToList(),
            ["transferencias"] = liquidacion.Transferencias
                .Select(t => new { de = Nombre(t.De), a = Nombre(t.A), importe = t.Importe }).ToList(),
            ["pagosRegistrados"] = liquidacion.Pagos.Count,
            ["nota"] = "saldo positivo: le deben; negativo: debe. Los importes ya descuentan los pagos registrados.",
        };
        periodo.AnotarSiAsumido(resultado);
        return Datos(resultado);
    }

    private static async Task<ResultadoHerramienta> CompararAsync(JsonElement e, IDatosHogar datos, DateOnly hoy, CancellationToken ct)
    {
        var a = Periodo.Resolver(Entero(e, "mes_a", 1, 12, obligatorio: true), Entero(e, "anio_a", 2000, 2100), hoy);
        var b = Periodo.Resolver(Entero(e, "mes_b", 1, 12, obligatorio: true), Entero(e, "anio_b", 2000, 2100), hoy);
        var ra = await datos.ResumenAsync(a.Texto, ct);
        var rb = await datos.ResumenAsync(b.Texto, ct);
        var totalesA = ra.Categorias.ToDictionary(c => c.CategoriaId, c => (c.Nombre, c.Total));
        var totalesB = rb.Categorias.ToDictionary(c => c.CategoriaId, c => (c.Nombre, c.Total));
        var filas = totalesA.Keys.Union(totalesB.Keys).Select(id =>
        {
            var x = totalesA.GetValueOrDefault(id);
            var y = totalesB.GetValueOrDefault(id);
            return new
            {
                categoria = Limpiar(y.Nombre ?? x.Nombre ?? ""),
                mesA = x.Total,
                mesB = y.Total,
                diferencia = y.Total - x.Total,
            };
        }).OrderByDescending(f => Math.Abs(f.diferencia)).ToList();

        var resultado = new Dictionary<string, object?>
        {
            ["mesA"] = new { periodo = a.Texto, gastosTotales = ra.GastosTotales },
            ["mesB"] = new { periodo = b.Texto, gastosTotales = rb.GastosTotales },
            ["diferenciaTotal"] = rb.GastosTotales - ra.GastosTotales,
            ["porCategoria"] = filas,
        };
        if (a.Asumido || b.Asumido) resultado["anioAsumido"] = true;
        return Datos(resultado);
    }

    // --- utilidades ---

    private static ResultadoHerramienta Datos(Dictionary<string, object?> valor)
        => new(JsonSerializer.Serialize(new { datos = valor }, Json), false);

    private static ResultadoHerramienta Error(string mensaje) => new(mensaje, true);

    /// <summary>Entero opcional validado en un rango; <c>null</c> si no viene.</summary>
    private static int? Entero(JsonElement e, string campo, int min, int max, bool obligatorio = false)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(campo, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return obligatorio ? throw new ArgumentoInvalidoException($"Falta el argumento «{campo}».") : null;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < min || n > max)
            throw new ArgumentoInvalidoException($"El argumento «{campo}» debe ser un entero entre {min} y {max}.");
        return n;
    }

    private static string? Texto(JsonElement e, string campo)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(campo, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind != JsonValueKind.String)
            throw new ArgumentoInvalidoException($"El argumento «{campo}» debe ser texto.");
        var s = v.GetString()!.Trim();
        if (s.Length == 0) return null;
        if (s.Length > 100) throw new ArgumentoInvalidoException($"El argumento «{campo}» es demasiado largo.");
        return s;
    }

    /// <summary>Busca por nombre sin distinguir mayúsculas ni tildes: coincidencia exacta o, si es única, parcial.</summary>
    private static T Buscar<T>(string texto, IReadOnlyList<T> opciones, Func<T, string> nombre, string tipo)
    {
        var buscado = Normalizar(texto);
        var exactas = opciones.Where(o => Normalizar(nombre(o)) == buscado).ToList();
        if (exactas.Count == 1) return exactas[0];
        var parciales = opciones.Where(o =>
        {
            var n = Normalizar(nombre(o));
            return n.Contains(buscado) || buscado.Contains(n);
        }).ToList();
        if (parciales.Count == 1) return parciales[0];
        var disponibles = string.Join(", ", opciones.Select(o => Limpiar(nombre(o))));
        throw new ArgumentoInvalidoException(parciales.Count == 0
            ? $"No existe ninguna {tipo} llamada «{Limpiar(texto)}». Disponibles: {disponibles}."
            : $"«{Limpiar(texto)}» es ambigua entre varias {tipo}s. Disponibles: {disponibles}.");
    }

    private static string Normalizar(string s)
    {
        var d = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Limpia un texto que viene de la base de datos antes de dárselo al modelo: sin caracteres de control ni saltos de
    /// línea y con longitud acotada, para que un nombre de categoría o miembro no pueda llevar «instrucciones» largas.
    /// </summary>
    internal static string Limpiar(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, 60));
        foreach (var c in s)
        {
            if (sb.Length >= 60) break;
            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    private sealed class ArgumentoInvalidoException(string mensaje) : Exception(mensaje);

    /// <summary>Mes y año resueltos; recuerda si el año se asumió.</summary>
    private readonly record struct Periodo(int Anio, int Mes, bool Asumido)
    {
        public string Texto => $"{Anio:D4}-{Mes:D2}";

        /// <summary>Sin año: el en curso, salvo que el mes aún no haya llegado, que entonces es el último que pasó (el año anterior).</summary>
        public static Periodo Resolver(int? mes, int? anio, DateOnly hoy)
        {
            var m = mes ?? hoy.Month;
            return anio is not null
                ? new Periodo(anio.Value, m, false)
                : new Periodo(m > hoy.Month ? hoy.Year - 1 : hoy.Year, m, true);
        }

        public void AnotarSiAsumido(Dictionary<string, object?> resultado)
        {
            if (Asumido) resultado["anioAsumido"] = true;
        }
    }
}
