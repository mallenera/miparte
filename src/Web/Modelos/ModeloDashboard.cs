using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>Periodos que se eligen con un atajo.</summary>
public enum PeriodoRapido
{
    /// <summary>Desde el día 1 del mes en curso hasta hoy.</summary>
    EsteMes,

    /// <summary>El mes natural anterior.</summary>
    MesAnterior,

    /// <summary>Los últimos 30 días, hoy incluido.</summary>
    Ultimos30,

    /// <summary>Desde el 1 de enero hasta hoy.</summary>
    EsteAnio,
}

/// <summary>
/// Estado y orquestación de la pestaña Dashboard: periodo, carga de los gastos del periodo y del anterior, y la selección cruzada
/// (categoría, persona, tramo) con el recálculo de cada gráfico. No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
/// <param name="hoy">Fecha de hoy (local); se pide en cada uso.</param>
public sealed class ModeloDashboard(CoreApiClient api, Func<DateOnly> hoy)
{
    private static readonly CultureInfo Es = new("es-ES");
    private int _version;

    // ───── Datos ─────

    /// <summary>Gastos del periodo y del anterior de igual duración.</summary>
    public List<GastoResponse> Gastos { get; private set; } = [];

    /// <summary>Categorías del hogar.</summary>
    public List<CategoriaDto> Categorias { get; private set; } = [];

    /// <summary>Miembros del hogar.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    // ───── Estado de pantalla ─────

    /// <summary>Si se están cargando los gastos.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Error de la carga.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Primer día del periodo.</summary>
    public DateOnly Desde { get; set; }

    /// <summary>Último día del periodo.</summary>
    public DateOnly Hasta { get; set; }

    /// <summary>Selección cruzada de categoría y persona.</summary>
    public FiltroAnalisis Filtro { get; private set; } = new();

    /// <summary>Tramo de la gráfica elegido; null si no hay.</summary>
    public PuntoSerie? Tramo { get; private set; }

    /// <summary>Sin filtros: decide si hay algo que mostrar en el periodo.</summary>
    public ResultadoAnalisis? Base { get; private set; }

    /// <summary>Con todos los filtros: indicadores y mayores gastos.</summary>
    public ResultadoAnalisis? R { get; private set; }

    /// <summary>Sin el filtro de categoría: gráfico de categorías.</summary>
    public ResultadoAnalisis? RCat { get; private set; }

    /// <summary>Sin el filtro de persona: rosco y lista de quién paga.</summary>
    public ResultadoAnalisis? RMiembro { get; private set; }

    /// <summary>Sin el filtro de fechas: gráfico de evolución, del periodo completo.</summary>
    public ResultadoAnalisis? RSerie { get; private set; }

    /// <summary>Hay alguna selección (categoría, persona, cuenta común o tramo).</summary>
    public bool HayFiltros => Filtro.Activo || Tramo is not null;

    /// <summary>Hay una persona (o la cuenta común) elegida.</summary>
    public bool HayMiembroElegido => Filtro.Miembro is not null || Filtro.CuentaComun;

    /// <summary>Por qué el periodo elegido no se puede analizar, o null si es válido.</summary>
    public string? ErrorPeriodo =>
        Desde > Hasta ? "La fecha «Desde» no puede ser posterior a «Hasta»."
        : !AnalisisGastos.PeriodoAdmitido(Desde, Hasta)
            ? "El periodo es demasiado largo para compararlo con el anterior de igual duración: elige fechas más recientes."
        : null;

    /// <summary>Días del periodo analizado.</summary>
    public int Dias => AnalisisGastos.Dias(R?.Desde ?? Desde, R?.Hasta ?? Hasta);

    /// <summary>Periodo analizado en texto («1 oct 2026 y 20 oct 2026»).</summary>
    public string Periodo => R is null ? "" : $"{R.Desde.ToString("d MMM yyyy", Es)} y {R.Hasta.ToString("d MMM yyyy", Es)}";

    /// <summary>Cómo se agrupa la evolución según la duración del periodo.</summary>
    public string TextoGranularidad => AnalisisGastos.GranularidadPara(AnalisisGastos.Dias(Desde, Hasta)) switch
    {
        Granularidad.Dia => "Por día",
        Granularidad.Semana => "Por semana",
        _ => "Por mes",
    };

    /// <summary>Hasta 7 categorías y el resto agrupado, para que el gráfico se lea en una pantalla pequeña; la elegida siempre se ve.</summary>
    public IReadOnlyList<GastoPorCategoria> CategoriasMostradas => GraficoGastos.AgruparResto(RCat!.PorCategoria, 7, Filtro.Categoria);

    /// <summary>Enlace a Gastos con la selección actual (periodo o tramo, categoría y persona) para ver la lista completa y editarla.</summary>
    public string UrlGastos
    {
        get
        {
            var (desde, hasta) = Tramo is { } t ? (t.Inicio, t.Fin) : (Desde, Hasta);
            var url = $"gastos?desde={desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&hasta={hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            if (Filtro.Categoria is { } c) url += $"&categoria={c}";
            // «pagador» (no «miembro»): el Dashboard filtra por quién paga, y «miembro» incluiría también a quien solo asume una parte.
            if (Filtro.Miembro is { } m) url += $"&pagador={m}";
            if (Filtro.CuentaComun) url += "&cuentacomun=true";
            return url;
        }
    }

    /// <summary>Nombre de la categoría (de primer nivel) para el chip del filtro.</summary>
    public string NombreCategoriaRaiz(Guid id) => Categorias.FirstOrDefault(c => c.Id == id)?.Nombre ?? "—";

    /// <summary>Concepto del gasto o, si no tiene, la ruta de su categoría.</summary>
    public string NombreGasto(GastoResponse g) =>
        string.IsNullOrWhiteSpace(g.Concepto) ? ArbolCategorias.Ruta(Categorias, g.CategoriaId) : g.Concepto;

    /// <summary>Porcentaje con un decimal que supone una parte sobre un total.</summary>
    public static string Porcentaje(decimal parte, decimal total) => (total > 0 ? parte / total : 0m).ToString("P1", Es);

    // ───── Operaciones ─────

    /// <summary>Fija el periodo inicial (el mes en curso hasta hoy) y carga.</summary>
    public Task IniciarAsync()
    {
        var h = hoy();
        Desde = new DateOnly(h.Year, h.Month, 1);
        Hasta = h;
        return CargarAsync();
    }

    /// <summary>Elige un periodo con un atajo y carga.</summary>
    public Task ElegirAsync(PeriodoRapido p)
    {
        var h = hoy();
        (Desde, Hasta) = p switch
        {
            PeriodoRapido.EsteMes => (new DateOnly(h.Year, h.Month, 1), h),
            PeriodoRapido.MesAnterior => (new DateOnly(h.Year, h.Month, 1).AddMonths(-1), new DateOnly(h.Year, h.Month, 1).AddDays(-1)),
            PeriodoRapido.Ultimos30 => (h.AddDays(-29), h),
            _ => (new DateOnly(h.Year, 1, 1), h),
        };
        return CargarAsync();
    }

    /// <summary>Recarga al cambiar las fechas a mano, salvo que el periodo no sea válido.</summary>
    public Task AplicarFechasAsync() => ErrorPeriodo is not null ? Task.CompletedTask : CargarAsync();

    /// <summary>
    /// Pide los gastos del periodo y del anterior (una sola petición) y recalcula. Una respuesta tardía de otro periodo no pisa la
    /// actual. Sin un periodo válido no hace nada.
    /// </summary>
    public async Task CargarAsync()
    {
        // Sin un periodo válido no se calcula el anterior: InicioAnterior se saldría del calendario.
        if (ErrorPeriodo is not null) return;
        var version = ++_version;
        Cargando = true;
        ErrorCarga = null;
        var desde = Desde;
        var hasta = Hasta;
        try
        {
            var gastos = api.ListarGastosPorRangoAsync(AnalisisGastos.InicioAnterior(desde, hasta), hasta);
            var categorias = api.ListarCategoriasAsync();
            var miembros = api.ListarMiembrosAsync();
            await Task.WhenAll(gastos, categorias, miembros);
            if (version != _version) return;
            Gastos = gastos.Result;
            Categorias = categorias.Result;
            Miembros = miembros.Result;
            // Otro periodo es otra vista: la selección anterior ya no tiene por qué existir.
            Filtro = new FiltroAnalisis();
            Tramo = null;
            Recalcular();
        }
        catch (ApiException e) when (version == _version)
        {
            ErrorCarga = e.Message;
            R = RCat = RMiembro = RSerie = Base = null;
        }
        catch (ApiException)
        {
            return; // fallo de una petición ya superada
        }

        Cargando = false;
    }

    /// <summary>Recalcula cada gráfico con los filtros de los demás, en el cliente: los gastos del periodo ya están cargados.</summary>
    private void Recalcular()
    {
        var (desde, hasta) = Tramo is { } t ? (t.Inicio, t.Fin) : (Desde, Hasta);
        List<GastoResponse> Con(FiltroAnalisis f) => AnalisisGastos.Filtrar(Gastos, Categorias, f);

        Base = AnalisisGastos.Calcular(Gastos, Categorias, Desde, Hasta);
        R = AnalisisGastos.Calcular(Con(Filtro), Categorias, desde, hasta);
        RCat = AnalisisGastos.Calcular(Con(Filtro with { Categoria = null }), Categorias, desde, hasta);
        RMiembro = AnalisisGastos.Calcular(Con(Filtro with { Miembro = null, CuentaComun = false }), Categorias, desde, hasta);
        RSerie = AnalisisGastos.Calcular(Con(Filtro), Categorias, Desde, Hasta);
    }

    /// <summary>Pincha en una categoría: la elige, o la quita si ya estaba elegida. «Otras categorías» no se puede elegir.</summary>
    public void ElegirCategoria(Guid id)
    {
        if (id == Guid.Empty) return;
        Filtro = Filtro with { Categoria = Filtro.Categoria == id ? null : id };
        Recalcular();
    }

    /// <summary>Pincha en una persona (o en la cuenta común si es null): la elige, o la quita si ya estaba elegida.</summary>
    public void ElegirMiembro(Guid? id)
    {
        Filtro = id is { } m
            ? Filtro with { Miembro = Filtro.Miembro == m ? null : m, CuentaComun = false }
            : Filtro with { CuentaComun = !Filtro.CuentaComun, Miembro = null };
        Recalcular();
    }

    /// <summary>Pincha en un punto de la gráfica: filtra por ese tramo de fechas, o lo quita si ya estaba elegido.</summary>
    public void ElegirTramo(PuntoSerie p)
    {
        Tramo = Tramo?.Inicio == p.Inicio ? null : p;
        Recalcular();
    }

    /// <summary>Quita todas las selecciones.</summary>
    public void QuitarFiltros()
    {
        Filtro = new FiltroAnalisis();
        Tramo = null;
        Recalcular();
    }
}
