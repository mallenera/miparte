using System.Globalization;
using System.Text.Json;
using MiParte.Contracts;

namespace MiParte.Web.Auditoria;

/// <summary>Un campo que cambia en un evento del historial, ya legible.</summary>
/// <param name="Campo">Nombre del campo en español.</param>
/// <param name="Antes">Valor anterior, o null si no existía (alta).</param>
/// <param name="Despues">Valor nuevo, o null si se borró.</param>
public sealed record CambioAuditoria(string Campo, string? Antes, string? Despues);

/// <summary>Convierte los eventos de <c>/api/auditoria</c> (JSON con los campos del servidor) en frases y cambios legibles.</summary>
public static class TextoAuditoria
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Tipos de entidad que registra la auditoría, con su nombre en plural para el filtro.</summary>
    public static readonly IReadOnlyList<(string Id, string Plural)> Entidades =
    [
        ("gasto", "Gastos"), ("gasto_recurrente", "Gastos recurrentes"), ("pago_liquidacion", "Pagos de liquidación"),
        ("aportacion_cuenta", "Aportaciones a la cuenta común"), ("reembolso_cuenta", "Reembolsos de la cuenta común"),
        ("deposito_ahorro", "Ingresos de ahorro"), ("retirada_ahorro", "Retiradas de ahorro"),
        ("perfil_reparto", "Perfiles de reparto"), ("categoria", "Categorías"),
        ("miembro", "Miembros"), ("invitacion", "Invitaciones"), ("hogar", "Hogar"),
    ];

    private static readonly Dictionary<string, string> Sustantivos = new()
    {
        ["hogar"] = "el hogar", ["miembro"] = "un miembro", ["invitacion"] = "una invitación", ["gasto"] = "un gasto",
        ["gasto_recurrente"] = "un gasto recurrente", ["pago_liquidacion"] = "un pago de liquidación",
        ["aportacion_cuenta"] = "una aportación a la cuenta común", ["reembolso_cuenta"] = "un reembolso de la cuenta común",
        ["retirada_ahorro"] = "una retirada de ahorro", ["deposito_ahorro"] = "un ingreso de ahorro",
        ["perfil_reparto"] = "un perfil de reparto", ["categoria"] = "una categoría",
    };

    private static readonly Dictionary<string, string> Campos = new()
    {
        ["nombre"] = "Nombre", ["concepto"] = "Concepto", ["importe"] = "Importe", ["fecha"] = "Fecha", ["mes"] = "Mes",
        ["desde"] = "Desde", ["diaMes"] = "Día del mes", ["activo"] = "Activo", ["activa"] = "Activa", ["rol"] = "Rol",
        ["tipo"] = "Tipo", ["modo"] = "Modo", ["notas"] = "Notas", ["pagadoPor"] = "Pagado por", ["miembroId"] = "Miembro",
        ["responsableId"] = "Responsable", ["repartos"] = "Reparto", ["detalle"] = "Detalle", ["ahorro"] = "Ahorro",
        ["aCargoCuentaComun"] = "A cargo de la cuenta común", ["pagadoDesdeAhorro"] = "Pagado desde el ahorro",
    };

    /// <summary>Frase del evento sin el autor: «creó un gasto "Compra"», «borró una categoría»…</summary>
    /// <param name="evento">Evento del historial.</param>
    public static string Frase(EventoAuditoriaDto evento)
    {
        var que = Sustantivos.GetValueOrDefault(evento.Entidad, evento.Entidad.Replace('_', ' '));
        var nombre = Nombre(evento) is { Length: > 0 } n ? $" «{n}»" : "";
        return evento.Accion switch
        {
            "crear" => evento.Entidad == "hogar" ? "creó el hogar" + nombre : $"creó {que}{nombre}",
            "editar" => $"editó {que}{nombre}",
            "borrar" => $"borró {que}{nombre}",
            "vincular" => $"vinculó su cuenta a {que}{nombre}",
            "usar" => $"aceptó {que}{nombre}",
            _ => $"{evento.Accion} {que}{nombre}",
        };
    }

    /// <summary>Campos que cambian en el evento: en un alta solo «después», en un borrado solo «antes».</summary>
    /// <param name="evento">Evento del historial.</param>
    /// <param name="miembro">Nombre de un miembro por su id, o null si no se conoce (el campo se omite).</param>
    public static List<CambioAuditoria> Cambios(EventoAuditoriaDto evento, Func<Guid, string?> miembro)
    {
        var claves = new List<string>();
        foreach (var obj in new[] { evento.Antes, evento.Despues })
            if (obj is { ValueKind: JsonValueKind.Object } o)
                foreach (var p in o.EnumerateObject())
                    if (!claves.Contains(p.Name) && Visible(p.Name)) claves.Add(p.Name);

        var cambios = new List<CambioAuditoria>();
        foreach (var clave in claves)
        {
            var antes = Valor(evento.Antes, clave, miembro, out var okAntes);
            var despues = Valor(evento.Despues, clave, miembro, out var okDespues);
            if (!okAntes || !okDespues) continue;
            cambios.Add(new CambioAuditoria(Etiqueta(clave), antes, despues));
        }
        return cambios;
    }

    // Los identificadores internos (id, hogarId, categoriaId...) no dicen nada a una persona; solo se muestran los que
    // se pueden resolver a un miembro.
    private static bool Visible(string clave) =>
        clave is not ("id" or "hogarId") && (!clave.EndsWith("Id", StringComparison.Ordinal) || Campos.ContainsKey(clave));

    private static string? Nombre(EventoAuditoriaDto e)
    {
        foreach (var obj in new[] { e.Despues, e.Antes })
            if (obj is { ValueKind: JsonValueKind.Object } o)
                foreach (var clave in new[] { "nombre", "concepto" })
                    if (o.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } t)
                        return t;
        return null;
    }

    private static string Etiqueta(string clave)
    {
        if (Campos.TryGetValue(clave, out var e)) return e;
        var texto = string.Concat(clave.Select((c, i) => char.IsUpper(c) && i > 0 ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return char.ToUpperInvariant(texto[0]) + texto[1..];
    }

    /// <summary>
    /// Reparto de un gasto o detalle de un perfil: «Ana 60, Luis 40». Cualquier otra lista se resume por su número de líneas.
    /// </summary>
    private static string Lineas(JsonElement lista, Func<Guid, string?> miembro)
    {
        var partes = new List<string>();
        foreach (var linea in lista.EnumerateArray())
        {
            if (linea.ValueKind != JsonValueKind.Object || !linea.TryGetProperty("miembroId", out var m)
                || !Guid.TryParse(m.GetString(), out var id))
                return lista.GetArrayLength() == 1 ? "1 línea" : $"{lista.GetArrayLength()} líneas";
            var dato = linea.TryGetProperty("valor", out var valor) ? valor
                : linea.TryGetProperty("importeAsumido", out var importe) ? importe : default;
            var numero = dato.ValueKind == JsonValueKind.Number ? " " + dato.GetDecimal().ToString("0.##", Es) : "";
            partes.Add((miembro(id) ?? "otro miembro") + numero);
        }
        return partes.Count == 0 ? "sin líneas" : string.Join(", ", partes);
    }

    /// <summary>Valor legible del campo; <paramref name="ok"/> es false si es un id que no se puede resolver.</summary>
    private static string? Valor(JsonElement? objeto, string clave, Func<Guid, string?> miembro, out bool ok)
    {
        ok = true;
        if (objeto is not { ValueKind: JsonValueKind.Object } o || !o.TryGetProperty(clave, out var v)) return null;
        switch (v.ValueKind)
        {
            case JsonValueKind.Null: return "—";
            case JsonValueKind.True: return "Sí";
            case JsonValueKind.False: return "No";
            case JsonValueKind.Number: return v.GetDecimal().ToString("0.##", Es);
            case JsonValueKind.Array: return Lineas(v, miembro);
            case JsonValueKind.Object: return "…";
            default:
                var texto = v.GetString() ?? "—";
                if (!clave.EndsWith("Id", StringComparison.Ordinal) && clave != "pagadoPor") return texto;
                if (Guid.TryParse(texto, out var id) && miembro(id) is { } nombre) return nombre;
                ok = false;
                return null;
        }
    }
}
