using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>Fila visible del árbol «Por categoría»; <c>Directo</c> es el gasto propio de una madre desplegada.</summary>
/// <param name="Nodo">Nodo del resumen al que corresponde la fila.</param>
/// <param name="Nivel">Nivel de sangrado.</param>
/// <param name="Directo">Si es el gasto directo de la madre («Sin subcategoría») y no la madre misma.</param>
public sealed record FilaCategoria(NodoResumen Nodo, int Nivel, bool Directo);

/// <summary>
/// Estado y orquestación de la pestaña Resumen: gastos del mes por persona y categoría, liquidación (saldos, transferencias
/// sugeridas con importe editable y pagos registrados) y cierre de mes. No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
/// <param name="hoy">Fecha de hoy, que fija el mes con el que se abre la pestaña.</param>
public sealed class ModeloResumen(CoreApiClient api, DateOnly hoy)
{
    private static readonly CultureInfo Es = new("es-ES");
    private DateOnly _mes = new(hoy.Year, hoy.Month, 1);
    private int _version;
    private readonly HashSet<Guid> _abiertas = [];
    private readonly Dictionary<(Guid, Guid), string> _importes = [];

    // ───── Datos ─────

    /// <summary>Resumen del mes mostrado; null hasta cargarlo.</summary>
    public ResumenMensualResponse? Resumen { get; private set; }

    /// <summary>Liquidación del mes mostrado; null hasta cargarla.</summary>
    public LiquidacionResponse? Liquidacion { get; private set; }

    /// <summary>
    /// Cuenta común del mes mostrado, con la tabla del resumen mensual; null si el hogar no la tiene o no se pudo leer (el resumen sigue
    /// viéndose sin ella).
    /// </summary>
    public CuentaComunResponse? CuentaComun { get; private set; }

    /// <summary>Miembros del hogar.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Categorías del hogar, para mostrar la ruta de las subcategorías.</summary>
    public List<CategoriaDto> Categorias { get; private set; } = [];

    /// <summary>Meses cerrados.</summary>
    public List<MesCerradoDto> Cierres { get; private set; } = [];

    /// <summary>Ciclos de liquidación cerrados, del más reciente al más antiguo (solo con el hogar en modo ciclos).</summary>
    public List<CicloLiquidacionDto> Ciclos { get; private set; } = [];

    /// <summary>
    /// Si el hogar liquida por ciclos. Entonces la liquidación se cierra con «Cerrar liquidación» y no se registran pagos; lo fija la
    /// pantalla antes de cargar.
    /// </summary>
    public bool PorCiclos { get; set; }

    // ───── Estado de pantalla ─────

    /// <summary>Si la persona está confirmando el cierre de la liquidación.</summary>
    public bool ConfirmandoCierre { get; set; }

    /// <summary>Si se están cargando los datos.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Si hay un pago registrándose o eliminándose.</summary>
    public bool Guardando { get; private set; }

    /// <summary>Error de la carga inicial.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Error de la última operación.</summary>
    public string? Error { get; private set; }

    /// <summary>Pago cuya eliminación se está confirmando.</summary>
    public Guid? ConfirmandoEliminar { get; set; }

    /// <summary>Persona elegida en «Ver solo a» («» = todas).</summary>
    public string FiltroMiembro { get; set; } = "";

    /// <summary>Mes mostrado (primer día).</summary>
    public DateOnly Mes => _mes;

    /// <summary>Nombre del mes mostrado.</summary>
    public string NombreMes => _mes.ToString("MMMM yyyy", Es);

    /// <summary>Cierre del mes mostrado; null si está abierto.</summary>
    public MesCerradoDto? CierreDelMes => Cierres.FirstOrDefault(c => c.Mes == Formato.Mes(_mes));

    /// <summary>Si el mes tiene gastos: sin ellos no hay nada que cerrar y no se muestra el cierre.</summary>
    public bool HayGastos => Resumen is { GastosTotales: > 0 };

    // ───── Lecturas para pintar ─────

    /// <summary>Sin filtro pasa todo; con filtro, solo lo que afecta al miembro elegido.</summary>
    public bool FiltraMiembro(Guid id) => FiltroMiembro == "" || id.ToString() == FiltroMiembro;

    /// <summary>
    /// Personas de la tabla «Por persona»: las del resumen del mes y las que solo tienen saldo de liquidación (p. ej. quien solo recibió
    /// un pago).
    /// </summary>
    public IReadOnlyList<ResumenMiembroDto> Personas()
    {
        var delResumen = Resumen!.Miembros;
        var solaLiquidacion = (Liquidacion?.Saldos ?? []).Where(s => delResumen.All(m => m.MiembroId != s.MiembroId))
            .Select(s => new ResumenMiembroDto(s.MiembroId, s.Nombre, 0m, 0m));
        return [.. delResumen, .. solaLiquidacion];
    }

    /// <summary>Saldo de liquidación de una persona en el mes; null si no tiene.</summary>
    public SaldoMiembroDto? SaldoDe(Guid miembroId) => Liquidacion?.Saldos.FirstOrDefault(s => s.MiembroId == miembroId);

    /// <summary>Si la categoría madre está desplegada.</summary>
    public bool EstaAbierta(Guid categoriaId) => _abiertas.Contains(categoriaId);

    /// <summary>Despliega o contrae una categoría madre.</summary>
    public void Alternar(Guid id)
    {
        if (!_abiertas.Remove(id)) _abiertas.Add(id);
    }

    /// <summary>Aplana el árbol «Por categoría» según las madres desplegadas.</summary>
    public IEnumerable<FilaCategoria> FilasCategorias()
    {
        IEnumerable<FilaCategoria> Recorrer(NodoResumen n, int nivel)
        {
            yield return new FilaCategoria(n, nivel, false);
            if (n.Hijos.Count == 0 || !_abiertas.Contains(n.CategoriaId)) yield break;
            if (n.Propio > 0) yield return new FilaCategoria(n, nivel + 1, true);
            foreach (var h in n.Hijos)
                foreach (var f in Recorrer(h, nivel + 1)) yield return f;
        }

        return ArbolCategorias.ResumenEnArbol(Categorias, Resumen!.Categorias).SelectMany(n => Recorrer(n, 0));
    }

    /// <summary>Porcentaje entero que supone un importe sobre el total del mes.</summary>
    public string Porcentaje(decimal total)
        => Math.Round(GraficoGastos.PorCiento(total, Resumen!.GastosTotales), 0).ToString("0", CultureInfo.InvariantCulture);

    private static (Guid, Guid) Clave(TransferenciaDto t) => (t.De, t.A);

    /// <summary>Lo escrito por el usuario o, si aún no ha tocado el campo, el importe sugerido.</summary>
    public string ImporteEscrito(TransferenciaDto t) =>
        _importes.TryGetValue(Clave(t), out var texto) ? texto : t.Importe.ToString("0.00", Es);

    /// <summary>Guarda lo que escribe la persona en el importe de una transferencia.</summary>
    public void EscribirImporte(TransferenciaDto t, string? texto) => _importes[Clave(t)] = texto ?? "";

    /// <summary>Importe del pago: mayor que 0, con máximo 2 decimales y sin pasar de lo pendiente.</summary>
    public bool TryImporte(TransferenciaDto t, out decimal valor)
    {
        // La regla del importe es una sola (EntradaImporte); aquí solo se añade que no pase de lo pendiente.
        return EntradaImporte.TryParse(ImporteEscrito(t), out valor) && valor <= t.Importe;
    }

    /// <summary>El importe escrito para la transferencia es válido.</summary>
    public bool ImporteValido(TransferenciaDto t) => TryImporte(t, out _);

    // ───── Operaciones ─────

    /// <summary>Marca el modelo como «cargando» antes de pedir los permisos y los datos.</summary>
    public void IniciarCarga()
    {
        Cargando = true;
        ErrorCarga = null;
    }

    /// <summary>Carga miembros, categorías, resumen, liquidación y cierres en paralelo; si falla deja el mensaje en <see cref="ErrorCarga"/>.</summary>
    public async Task CargarAsync()
    {
        try
        {
            // En paralelo: la latencia hacia la API se paga una vez y no cinco.
            var miembros = api.ListarMiembrosAsync();
            // Solo sirven para mostrar la ruta de las subcategorías: sin ellas el resumen usa el nombre suelto.
            var categorias = OVacia(api.ListarCategoriasAsync());
            var datos = RecargarAsync();
            await Task.WhenAll(miembros, categorias, datos);
            Miembros = miembros.Result;
            Categorias = categorias.Result;
        }
        catch (ApiException e)
        {
            ErrorCarga = e.Message;
        }

        Cargando = false;
    }

    private async Task RecargarAsync()
    {
        // Resumen y liquidación llegan juntos: la API lee una sola vez los gastos y los miembros del mes.
        // Una respuesta tardía de otro mes no pisa la actual: si no, un pago se registraría en el mes mostrado con datos de otro.
        var version = ++_version;
        var datos = api.ObtenerResumenYLiquidacionAsync(Formato.Mes(_mes));
        var cierres = OVacia(api.ListarCierresMesAsync()); // el cierre es informativo aquí: sin él se sigue viendo el resumen
        var ciclos = PorCiclos ? OVacia(api.ListarCiclosLiquidacionAsync()) : Task.FromResult<List<CicloLiquidacionDto>>([]);
        var cuenta = SiFalla(api.ObtenerCuentaComunAsync(Formato.Mes(_mes))); // solo para su tabla: sin ella se sigue viendo el resumen
        try
        {
            await Task.WhenAll(datos, cierres, ciclos, cuenta);
        }
        catch (ApiException) when (version != _version)
        {
            return; // fallo de una petición ya superada
        }

        if (version != _version) return;
        Resumen = datos.Result.Resumen;
        Liquidacion = datos.Result.Liquidacion;
        Cierres = cierres.Result;
        Ciclos = ciclos.Result;
        CuentaComun = cuenta.Result;
    }

    /// <summary>
    /// Espera una lectura opcional: si la API falla o la petición caduca (el timeout de HttpClient se ve como cancelación), devuelve null
    /// para que la carga principal no se caiga con ella.
    /// </summary>
    private static async Task<T?> SiFalla<T>(Task<T> carga) where T : class
    {
        try
        {
            return await carga;
        }
        catch (Exception e) when (e is ApiException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cierra la liquidación por ciclos: liquida los gastos pendientes desde el cierre anterior. Devuelve el aviso, o null si falló (el
    /// error queda en <see cref="Error"/>).
    /// </summary>
    public Task<string?> CerrarCicloAsync() =>
        EjecutarAsync(async () =>
        {
            await api.CerrarCicloLiquidacionAsync();
            ConfirmandoCierre = false;
            return "Liquidación cerrada";
        });

    /// <summary>Espera una lista opcional: si la API falla, la pantalla sigue con la lista vacía.</summary>
    private static async Task<List<T>> OVacia<T>(Task<List<T>> carga)
    {
        try
        {
            return await carga;
        }
        catch (ApiException)
        {
            return [];
        }
    }

    /// <summary>Recarga el mes tras cerrarlo o reabrirlo; los errores quedan en <see cref="Error"/>.</summary>
    public async Task RecargarTrasCierreAsync()
    {
        try
        {
            await RecargarAsync();
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Pasa al mes anterior (-1) o siguiente (1) y recarga.</summary>
    public async Task CambiarMesAsync(int delta)
    {
        _mes = _mes.AddMonths(delta);
        Error = null;
        ConfirmandoEliminar = null;
        _importes.Clear();
        try
        {
            await RecargarAsync();
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Registra el pago de una transferencia sugerida por el importe escrito; devuelve el aviso, o null si falló.</summary>
    public Task<string?> RegistrarPagoAsync(TransferenciaDto t) =>
        EjecutarAsync(async () =>
        {
            if (!TryImporte(t, out var importe))
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, "Introduce un importe mayor que 0 con máximo 2 decimales y sin pasar de lo pendiente.");
            await api.CrearPagoLiquidacionAsync(new CrearPagoLiquidacionRequest(_mes, t.De, t.A, importe, null, null));
            _importes.Remove(Clave(t));
            return importe == t.Importe ? "Pago registrado" : "Pago parcial registrado";
        });

    /// <summary>Elimina un pago registrado; devuelve el aviso, o null si falló.</summary>
    public Task<string?> EliminarPagoAsync(PagoLiquidacionDto p) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarPagoLiquidacionAsync(p.Id);
            ConfirmandoEliminar = null;
            return "Pago eliminado";
        });

    /// <summary>Ejecuta una operación y recarga el mes; los errores de la API quedan en <see cref="Error"/>.</summary>
    private async Task<string?> EjecutarAsync(Func<Task<string>> accion)
    {
        Error = null;
        Guardando = true;
        try
        {
            var aviso = await accion();
            await RecargarAsync();
            return aviso;
        }
        catch (ApiException e)
        {
            Error = e.Message;
            ConfirmandoEliminar = null;
            return null;
        }
        finally
        {
            Guardando = false;
        }
    }
}
