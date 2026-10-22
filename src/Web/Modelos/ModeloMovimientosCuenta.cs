using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado del extracto de movimientos de la cuenta común entre dos fechas, para cuadrarlo con el banco: lo pide a Core.Api y lo filtra
/// en el cliente por tipo, persona y texto. Los filtros no recalculan el saldo: cada línea conserva el saldo real de la cuenta tras ella,
/// como en el banco. No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
/// <param name="hoy">Fecha de hoy: el periodo inicial va del día 1 de este mes a hoy.</param>
public sealed class ModeloMovimientosCuenta(CoreApiClient api, DateOnly hoy)
{
    /// <summary>Filtros de tipo que ofrece la pantalla, con su etiqueta; la clave vacía es «todos».</summary>
    public static readonly IReadOnlyList<(string Clave, string Etiqueta)> Tipos =
    [
        ("", "Todos"),
        ("entradas", "Solo entradas"),
        ("salidas", "Solo salidas"),
        ("aportacion", "Aportaciones"),
        ("ingreso", "Ingresos sueltos"),
        ("gasto", "Gastos pagados por la cuenta"),
        ("reembolso", "Reembolsos"),
        ("ahorro", "Ahorro (ingresos, retiradas y gastos)"),
    ];

    private int _version;

    /// <summary>Extracto del periodo; null hasta cargarlo o si la última carga falló.</summary>
    public MovimientosCuentaComunResponse? Extracto { get; private set; }

    /// <summary>Miembros del hogar, para poner nombre a cada línea.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Categorías, para nombrar los gastos sin concepto.</summary>
    public List<CategoriaDto> Categorias { get; private set; } = [];

    /// <summary>Primer día del periodo.</summary>
    public DateOnly Desde { get; set; } = new(hoy.Year, hoy.Month, 1);

    /// <summary>Último día del periodo.</summary>
    public DateOnly Hasta { get; set; } = hoy;

    /// <summary>Clave del filtro de tipo (ver <see cref="Tipos"/>); vacía para todos.</summary>
    public string FiltroTipo { get; set; } = "";

    /// <summary>Id del miembro por el que se filtra (aporta, recibe o registra); vacío para todos.</summary>
    public string FiltroMiembro { get; set; } = "";

    /// <summary>Texto a buscar en el concepto, la categoría o la persona.</summary>
    public string FiltroTexto { get; set; } = "";

    /// <summary>Si se está cargando.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Error de la última carga.</summary>
    public string? Error { get; private set; }

    /// <summary>Si el periodo elegido es válido.</summary>
    public bool PeriodoValido => Desde <= Hasta;

    /// <summary>Si hay algún filtro aparte de las fechas.</summary>
    public bool HayFiltros => FiltroTipo != "" || FiltroMiembro != "" || !string.IsNullOrWhiteSpace(FiltroTexto);

    /// <summary>Líneas que pasan los filtros, de la más reciente a la más antigua (como la app del banco).</summary>
    public IReadOnlyList<MovimientoCuentaComunDto> Visibles =>
        (Extracto?.Movimientos ?? []).Where(Pasa).Reverse().ToList();

    /// <summary>Suma de lo que entra en las líneas visibles.</summary>
    public decimal EntradasVisibles => Visibles.Where(m => m.Importe > 0).Sum(m => m.Importe);

    /// <summary>Suma de lo que sale en las líneas visibles (positiva).</summary>
    public decimal SalidasVisibles => -Visibles.Where(m => m.Importe < 0).Sum(m => m.Importe);

    /// <summary>Nombre del fichero CSV del periodo cargado.</summary>
    public string NombreCsv => CsvMovimientos.NombreFichero(Extracto?.Desde ?? Desde, Extracto?.Hasta ?? Hasta);

    /// <summary>CSV de las líneas visibles (con los filtros aplicados), en orden cronológico.</summary>
    // Visibles va de lo más reciente a lo más antiguo; se le da la vuelta para conservar el orden del servidor dentro de cada día.
    public string GenerarCsv() => CsvMovimientos.Generar(Visibles.Reverse(), Descripcion, Miembros);

    /// <summary>Carga miembros, categorías y el extracto del periodo.</summary>
    public async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            Miembros = await api.ListarMiembrosAsync();
            Categorias = await api.ListarCategoriasAsync();
        }
        catch (ApiException e)
        {
            Error = e.Message;
            Cargando = false;
            return;
        }

        await AplicarPeriodoAsync();
    }

    /// <summary>Vuelve a pedir el extracto con las fechas actuales; con un periodo inválido no pide nada.</summary>
    public async Task AplicarPeriodoAsync()
    {
        if (!PeriodoValido)
        {
            Cargando = false;
            return;
        }

        // Una respuesta tardía de un periodo anterior no pisa la del actual.
        var version = ++_version;
        Cargando = true;
        Error = null;
        try
        {
            var extracto = await api.MovimientosCuentaComunAsync(Desde, Hasta);
            if (version != _version) return;
            Extracto = extracto;
        }
        catch (ApiException e) when (version == _version)
        {
            Extracto = null;
            Error = e.Message;
        }
        catch (ApiException)
        {
            return; // fallo de una petición ya superada
        }

        Cargando = false;
    }

    /// <summary>Quita los filtros de tipo, persona y texto (no cambia las fechas).</summary>
    public void LimpiarFiltros()
    {
        FiltroTipo = "";
        FiltroMiembro = "";
        FiltroTexto = "";
    }

    /// <summary>Texto principal de una línea: qué es y de quién.</summary>
    public string Descripcion(MovimientoCuentaComunDto m)
    {
        var quien = m.MiembroId is { } id ? Formato.NombreMiembro(id, Miembros) : "";
        return m.Tipo switch
        {
            "saldo_inicial" => "Saldo inicial",
            "aportacion" => $"Aportación de {quien}",
            "ingreso" => $"Ingreso de {quien}",
            "deposito_ahorro" => $"Ingreso al ahorro de {quien}",
            "gasto" => $"Gasto: {Nombre(m)}",
            "gasto_ahorro" => $"Gasto desde el ahorro: {Nombre(m)}",
            "reembolso" => $"Reembolso a {quien}",
            "retirada_ahorro" => $"Retirada de ahorro de {quien}",
            _ => m.Tipo,
        };
    }

    /// <summary>Si la línea toca el ahorro (para marcarla en la lista).</summary>
    public static bool EsAhorro(MovimientoCuentaComunDto m) => m.Tipo is "deposito_ahorro" or "gasto_ahorro" or "retirada_ahorro";

    /// <summary>Concepto del gasto o, si no tiene, el nombre de su categoría.</summary>
    private string Nombre(MovimientoCuentaComunDto m) =>
        !string.IsNullOrWhiteSpace(m.Concepto) ? m.Concepto
        : Categorias.FirstOrDefault(c => c.Id == m.CategoriaId)?.Nombre ?? "sin categoría";

    /// <summary>Si la línea pasa los filtros de tipo, persona y texto.</summary>
    private bool Pasa(MovimientoCuentaComunDto m)
    {
        var tipo = FiltroTipo switch
        {
            "" => true,
            "entradas" => m.Importe > 0,
            "salidas" => m.Importe < 0,
            "gasto" => m.Tipo is "gasto" or "gasto_ahorro",
            "ahorro" => EsAhorro(m),
            var t => m.Tipo == t,
        };
        if (!tipo) return false;
        if (Guid.TryParse(FiltroMiembro, out var id) && m.MiembroId != id) return false;

        var texto = FiltroTexto.Trim();
        return texto.Length == 0
            || Descripcion(m).Contains(texto, StringComparison.CurrentCultureIgnoreCase)
            || (m.Concepto?.Contains(texto, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }
}
