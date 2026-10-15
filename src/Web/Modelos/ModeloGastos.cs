using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado y orquestación de la pestaña Gastos: carga de datos de referencia, lista por mes o entre fechas con filtros, formulario
/// de alta y edición, y las operaciones contra la API. No depende de Blazor, así que se prueba con un <see cref="CoreApiClient"/> falso
/// sin renderizar componentes; <c>VistaGastos</c> solo enlaza sus campos y pinta.
/// </summary>
public sealed class ModeloGastos(CoreApiClient api, DateOnly hoy)
{
    private int _versionCarga;
    private DateOnly _mes = new(hoy.Year, hoy.Month, 1);

    // ───── Datos ─────

    /// <summary>Gastos mostrados, ya filtrados por categoría (con subcategorías) y por quién paga.</summary>
    public List<GastoResponse> Gastos { get; private set; } = [];

    /// <summary>Todos los miembros del hogar.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Adultos activos, los únicos que pagan y reparten.</summary>
    public List<MiembroDto> Adultos { get; private set; } = [];

    /// <summary>Categorías del hogar.</summary>
    public List<CategoriaDto> Categorias { get; private set; } = [];

    /// <summary>Perfiles de reparto del hogar.</summary>
    public List<PerfilRepartoDto> Perfiles { get; private set; } = [];

    /// <summary>Categorías aplanadas en árbol, con su nivel de sangrado.</summary>
    public List<NodoCategoria> Arbol { get; private set; } = [];

    /// <summary>Meses cerrados del hogar.</summary>
    public List<MesCerradoDto> Cierres { get; private set; } = [];

    /// <summary>Si el hogar tiene activada la cuenta común (se ofrece su perfil y el pagador «cuenta común»).</summary>
    public bool CuentaActiva { get; private set; }

    /// <summary>Si el hogar tiene activado el ahorro (se ofrece pagar desde él).</summary>
    public bool AhorroActivo { get; private set; }

    // ───── Estado de pantalla ─────

    /// <summary>Si se están cargando los datos de referencia.</summary>
    public bool Cargando { get; set; } = true;

    /// <summary>Si hay un guardado o un borrado en curso (se ignoran los demás: un doble clic crearía el gasto dos veces).</summary>
    public bool Guardando { get; private set; }

    /// <summary>Error de la carga inicial; null si fue bien.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Error de la última operación (filtrar, guardar, eliminar); null si no hay.</summary>
    public string? Error { get; private set; }

    /// <summary>Gasto cuya eliminación se está confirmando.</summary>
    public Guid? ConfirmandoEliminar { get; set; }

    // ───── Periodo y filtros ─────

    /// <summary>Si se listan los gastos entre dos fechas en vez de por mes.</summary>
    public bool ModoRango { get; private set; }

    /// <summary>Primer día del rango (modo «entre fechas»).</summary>
    public DateOnly Desde { get; set; }

    /// <summary>Último día del rango (modo «entre fechas»).</summary>
    public DateOnly Hasta { get; set; }

    /// <summary>Mes mostrado (primer día) en el modo «por mes».</summary>
    public DateOnly Mes => _mes;

    /// <summary>Persona elegida en el filtro («» = todas): quien paga o asume una parte.</summary>
    public string FiltroMiembro { get; set; } = "";

    /// <summary>Categoría elegida en el filtro («» = todas); incluye sus subcategorías.</summary>
    public string FiltroCategoria { get; set; } = "";

    /// <summary>Texto buscado en el concepto.</summary>
    public string FiltroTexto { get; set; } = "";

    /// <summary>Solo lo que paga esta persona (no lo que solo asume), con el que se abre desde el Dashboard.</summary>
    public Guid? SoloPaga { get; private set; }

    /// <summary>Solo lo que paga la cuenta común o el ahorro, sin persona.</summary>
    public bool SoloCuenta { get; private set; }

    /// <summary>Hay algún filtro aplicado.</summary>
    public bool HayFiltros
        => FiltroMiembro != "" || FiltroCategoria != "" || !string.IsNullOrWhiteSpace(FiltroTexto) || SoloPaga is not null || SoloCuenta;

    /// <summary>El mes mostrado está cerrado: el servidor rechazaría cualquier cambio en sus gastos.</summary>
    public bool MesCerrado => Cierres.Any(c => c.Mes == Formato.Mes(_mes));

    /// <summary>El gasto cae en un mes cerrado: el servidor rechazaría editarlo o borrarlo.</summary>
    public bool CerradoGasto(GastoResponse g) => Cierres.Any(c => c.Mes == Formato.Mes(g.Fecha));

    // ───── Formulario ─────

    /// <summary>Gasto que se está editando; null al crear uno nuevo.</summary>
    public Guid? Editando { get; private set; }

    /// <summary>Fecha del gasto del formulario.</summary>
    public DateOnly Fecha { get; set; } = hoy;

    /// <summary>Importe tal como lo escribe la persona.</summary>
    public string Importe { get; set; } = "";

    /// <summary>Concepto del formulario.</summary>
    public string Concepto { get; set; } = "";

    /// <summary>Categoría elegida en el formulario.</summary>
    public Guid Categoria { get; set; }

    /// <summary>Perfil de reparto elegido.</summary>
    public Guid Perfil { get; set; }

    /// <summary>Quién adelanta el pago; null si lo paga la cuenta común o el ahorro.</summary>
    public Guid? Pagador { get; private set; }

    /// <summary>Si se paga desde el ahorro.</summary>
    public bool DesdeAhorro { get; private set; }

    /// <summary>Si es un gasto personal de quien paga.</summary>
    public bool Personal { get; private set; }

    /// <summary>Quién paga en el selector: un adulto (su id), la cuenta común ("") o el ahorro ("ahorro").</summary>
    public string OrigenPago
    {
        get => DesdeAhorro ? "ahorro" : Pagador?.ToString() ?? "";
        set
        {
            DesdeAhorro = value == "ahorro";
            Pagador = DesdeAhorro || value == "" ? null : Guid.Parse(value);
        }
    }

    /// <summary>Tipo de gasto en el selector: «hogar» (se reparte y entra en la liquidación) o «personal» (solo de quien paga).</summary>
    public string TipoGasto
    {
        get => Personal ? "personal" : "hogar";
        set => Personal = value == "personal";
    }

    /// <summary>Perfil de modo cuenta común del hogar, el único con el que puede pagar la cuenta directamente.</summary>
    public PerfilRepartoDto? PerfilCuentaComun => Perfiles.FirstOrDefault(p => p.Modo == ModosReparto.CuentaComun);

    /// <summary>Perfiles que se ofrecen en el formulario.</summary>
    public IReadOnlyList<PerfilRepartoDto> PerfilesOfrecidos
        => ListaGastos.PerfilesOfrecidos(Perfiles, CuentaActiva, Editando is not null ? Perfil : null);

    /// <summary>La categoría elegida está marcada «a cargo de la cuenta común» y la cuenta está activada.</summary>
    public bool CategoriaACargoDeLaCuenta
        => CuentaActiva && Categorias.FirstOrDefault(c => c.Id == Categoria)?.ACargoCuentaComun == true;

    /// <summary>El importe escrito es válido (mayor que 0, máx. 2 decimales).</summary>
    public bool ImporteValido => EntradaImporte.TryParse(Importe, out _);

    /// <summary>Faltan adultos, categorías o perfiles para poder registrar un gasto.</summary>
    public bool FaltanDatosParaRegistrar => Adultos.Count == 0 || Categorias.Count == 0 || Perfiles.Count == 0;

    // ───── Nombres para pintar ─────

    /// <summary>Ruta «Padre › Hija» de la categoría.</summary>
    public string NombreCategoria(Guid id) => ArbolCategorias.Ruta(Categorias, id);

    /// <summary>Color del avatar del miembro.</summary>
    public int ColorDe(Guid id) => Hogares.ColorMiembro.De(id, Miembros);

    // ───── Operaciones ─────

    /// <summary>
    /// Aplica los filtros y el periodo con los que se abre la pestaña (enlaces del Dashboard). Un rango solo se usa si es coherente.
    /// </summary>
    public void Abrir(
        Guid? categoria, Guid? miembro, Guid? pagador, bool soloCuenta, DateOnly? desde, DateOnly? hasta)
    {
        if (categoria is { } c) FiltroCategoria = c.ToString();
        if (miembro is { } m) FiltroMiembro = m.ToString();
        SoloPaga = pagador;
        SoloCuenta = soloCuenta && pagador is null;
        if (desde is { } d && hasta is { } h && d <= h)
        {
            ModoRango = true;
            Desde = d;
            Hasta = h;
            _mes = new DateOnly(d.Year, d.Month, 1);
        }
    }

    /// <summary>Marca el modelo como «cargando» antes de pedir los permisos y los datos.</summary>
    public void IniciarCarga()
    {
        Cargando = true;
        ErrorCarga = null;
    }

    /// <summary>
    /// Carga miembros, categorías y perfiles, la lista de gastos y reinicia el formulario. Si la API falla deja el mensaje en
    /// <see cref="ErrorCarga"/>.
    /// </summary>
    /// <param name="cuentaActiva">Si el hogar tiene activada la cuenta común (se lee del hogar ya cargado, sin otra petición).</param>
    /// <param name="ahorroActivo">Si el hogar tiene activado el ahorro.</param>
    public async Task CargarAsync(bool cuentaActiva, bool ahorroActivo)
    {
        try
        {
            var miembros = api.ListarMiembrosAsync();
            var categorias = api.ListarCategoriasAsync();
            var perfiles = api.ListarPerfilesAsync();
            await Task.WhenAll(miembros, categorias, perfiles);
            Miembros = miembros.Result;
            Adultos = Miembros.Where(m => m.Tipo == TiposMiembro.Adulto && m.Activo).ToList();
            Categorias = categorias.Result;
            Perfiles = perfiles.Result;
            Arbol = ArbolCategorias.Aplanar(Categorias);
            CuentaActiva = cuentaActiva;
            AhorroActivo = ahorroActivo;
            await RecargarGastosAsync();
            ReiniciarFormulario();
        }
        catch (ApiException e)
        {
            ErrorCarga = e.Message;
        }

        Cargando = false;
    }

    /// <summary>
    /// Pide los gastos del periodo con los filtros y los cierres de mes. Una respuesta tardía de otra selección no pisa la actual;
    /// los errores de la lista se propagan.
    /// </summary>
    public async Task RecargarGastosAsync()
    {
        var version = ++_versionCarga;
        try
        {
            Guid? miembro = Guid.TryParse(FiltroMiembro, out var m) ? m : null;
            var gastos = ModoRango
                ? await api.ListarGastosPorRangoAsync(Desde, Hasta, miembro, FiltroTexto)
                : await api.ListarGastosAsync(Formato.Mes(_mes), null, miembro, FiltroTexto);
            // La categoría se filtra aquí y no en la API para que incluya sus subcategorías.
            var categorias = Guid.TryParse(FiltroCategoria, out var categoria)
                ? ArbolCategorias.ConDescendientes(Categorias, categoria)
                : null;

            if (version == _versionCarga) Gastos = ListaGastos.Filtrar(gastos, new FiltroLista(categorias, SoloPaga, SoloCuenta));
        }
        catch (ApiException) when (version != _versionCarga)
        {
            // Un fallo de una petición ya superada no debe mostrar un error junto a los resultados de la nueva.
        }

        try
        {
            Cierres = await api.ListarCierresMesAsync();
        }
        catch (ApiException)
        {
            Cierres = []; // sin saber qué meses están cerrados, el servidor sigue rechazando los cambios
        }
    }

    /// <summary>Vuelve a pedir los gastos con los filtros elegidos; los errores quedan en <see cref="Error"/>.</summary>
    public async Task AplicarFiltrosAsync()
    {
        Error = null;
        ConfirmandoEliminar = null;
        try
        {
            await RecargarGastosAsync();
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Quita todos los filtros, incluidos los que llegaron del Dashboard, y recarga.</summary>
    public Task LimpiarFiltrosAsync()
    {
        FiltroMiembro = FiltroCategoria = FiltroTexto = "";
        SoloPaga = null;
        SoloCuenta = false;
        return AplicarFiltrosAsync();
    }

    /// <summary>Cambia entre la vista por mes y la de entre fechas, conservando el periodo que se estaba viendo.</summary>
    public async Task CambiarModoAsync(bool rango)
    {
        if (rango == ModoRango) return;
        if (rango)
        {
            Desde = _mes;
            Hasta = _mes.AddMonths(1).AddDays(-1);
        }
        else
        {
            _mes = new DateOnly(Desde.Year, Desde.Month, 1);
        }

        ModoRango = rango;
        Error = null;
        ConfirmandoEliminar = null;
        if (Editando is not null) ReiniciarFormulario();
        await AplicarFiltrosAsync();
    }

    /// <summary>Recarga al cambiar el rango, salvo que «Desde» sea posterior a «Hasta».</summary>
    public Task AplicarRangoAsync() => Desde > Hasta ? Task.CompletedTask : AplicarFiltrosAsync();

    /// <summary>Pasa al mes anterior (-1) o siguiente (1).</summary>
    public async Task CambiarMesAsync(int delta)
    {
        _mes = _mes.AddMonths(delta);
        Error = null;
        ConfirmandoEliminar = null;
        if (Editando is not null) ReiniciarFormulario();
        try
        {
            await RecargarGastosAsync();
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Vuelve al formulario de alta con los valores por defecto.</summary>
    public void ReiniciarFormulario()
    {
        Editando = null;
        Importe = "";
        Concepto = "";
        Categoria = Arbol.Count > 0 ? Arbol[0].Categoria.Id : Guid.Empty;
        Pagador = (Adultos.FirstOrDefault(m => m.EsYo) ?? Adultos.FirstOrDefault())?.Id ?? Guid.Empty;
        DesdeAhorro = false;
        Personal = false;
        Perfil = PerfilesOfrecidos.FirstOrDefault()?.Id ?? Guid.Empty;
        AlCambiarCategoria();
    }

    /// <summary>
    /// Al elegir una categoría se propone su perfil de reparto por defecto; si va a cargo de la cuenta común (y está activada),
    /// el de cuenta común. Sin la cuenta activada no se propone un perfil de cuenta común.
    /// </summary>
    public void AlCambiarCategoria()
    {
        if (Editando is not null) return;
        var categoria = Categorias.FirstOrDefault(c => c.Id == Categoria);
        if (CategoriaACargoDeLaCuenta && PerfilCuentaComun is { } cuenta) Perfil = cuenta.Id;
        else if (categoria?.PerfilRepartoId is { } p && PerfilesOfrecidos.Any(x => x.Id == p)) Perfil = p;
    }

    /// <summary>La cuenta común solo paga gastos con su perfil: al elegirla como pagador se selecciona ese perfil.</summary>
    public void AlCambiarPagador()
    {
        if (Pagador is null) Personal = false; // un gasto personal siempre lo paga una persona
        if (Pagador is null && PerfilCuentaComun is { } p) Perfil = p.Id;
    }

    /// <summary>Carga un gasto en el formulario para editarlo.</summary>
    public void EmpezarEdicion(GastoResponse g)
    {
        Editando = g.Id;
        Fecha = g.Fecha;
        Importe = g.Importe.ToString(CultureInfo.InvariantCulture);
        Categoria = g.CategoriaId;
        Pagador = g.PagadoPor;
        DesdeAhorro = g.PagadoDesdeAhorro;
        Personal = g.Personal;
        Perfil = g.PerfilRepartoId;
        Concepto = g.Concepto ?? "";
        Error = null;
    }

    /// <summary>Descarta la edición y vuelve al formulario de alta.</summary>
    public void CancelarEdicion() => ReiniciarFormulario();

    /// <summary>
    /// Crea o guarda el gasto del formulario y recarga la lista. Devuelve el aviso a mostrar, o null si falló (el motivo queda en
    /// <see cref="Error"/>).
    /// </summary>
    public Task<string?> GuardarAsync() =>
        EjecutarAsync(async () =>
        {
            if (!EntradaImporte.TryParse(Importe, out var importe))
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, EntradaImporte.MensajeInvalido);
            var peticion = new GastoRequest(Fecha, importe, Categoria, Pagador, Perfil,
                string.IsNullOrWhiteSpace(Concepto) ? null : Concepto.Trim(), DesdeAhorro, Personal && Pagador is not null);
            if (Editando is { } id)
            {
                await api.GuardarGastoAsync(id, peticion);
                ReiniciarFormulario();
                return "Gasto guardado";
            }

            await api.CrearGastoAsync(peticion);
            Importe = "";
            Concepto = "";
            return "Gasto añadido";
        });

    /// <summary>Elimina el gasto y recarga la lista; devuelve el aviso, o null si falló.</summary>
    public Task<string?> EliminarAsync(GastoResponse g) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarGastoAsync(g.Id);
            ConfirmandoEliminar = null;
            if (Editando == g.Id) ReiniciarFormulario();
            return "Gasto eliminado";
        });

    /// <summary>Ejecuta una operación y recarga la lista; los errores de la API quedan en <see cref="Error"/>.</summary>
    private async Task<string?> EjecutarAsync(Func<Task<string>> accion)
    {
        if (Guardando) return null; // evita duplicar el gasto con doble clic o doble Intro
        Guardando = true;
        Error = null;
        try
        {
            var aviso = await accion();
            await RecargarGastosAsync();
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
