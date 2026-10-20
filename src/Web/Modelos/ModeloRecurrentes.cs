using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado y orquestación de la pestaña Recurrentes: plantillas de gasto mensual (alta, edición, pausa y borrado) y generación de
/// los gastos de un mes. No depende de Blazor; <c>VistaRecurrentes</c> solo enlaza sus campos y pinta.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
/// <param name="hoy">Fecha y hora local actuales (se pide en cada uso, para no fijar el día al crear el modelo).</param>
public sealed class ModeloRecurrentes(CoreApiClient api, Func<DateTime> hoy)
{
    private static readonly CultureInfo Es = new("es-ES");

    // ───── Datos ─────

    /// <summary>Plantillas ordenadas por día del mes y concepto.</summary>
    public List<GastoRecurrenteResponse> Plantillas { get; private set; } = [];

    /// <summary>Todos los miembros.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Adultos activos, los únicos que pueden pagar una plantilla.</summary>
    public List<MiembroDto> Adultos { get; private set; } = [];

    /// <summary>Categorías del hogar.</summary>
    public List<CategoriaDto> Categorias { get; private set; } = [];

    /// <summary>Perfiles de reparto del hogar.</summary>
    public List<PerfilRepartoDto> Perfiles { get; private set; } = [];

    /// <summary>Categorías aplanadas en árbol, con su nivel de sangrado.</summary>
    public List<NodoCategoria> Arbol { get; private set; } = [];

    // ───── Estado de pantalla ─────

    /// <summary>Si se están cargando los datos de referencia.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Si hay una generación de gastos en curso.</summary>
    public bool Generando { get; private set; }

    /// <summary>Si hay un guardado, pausa o borrado en curso (evita duplicar con doble clic).</summary>
    public bool Guardando { get; private set; }

    /// <summary>Error de la carga inicial.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Error de la última operación.</summary>
    public string? Error { get; private set; }

    /// <summary>Resultado de la última generación, en texto.</summary>
    public string? Resultado { get; private set; }

    /// <summary>Mes para el que se generan los gastos (primer día).</summary>
    public DateOnly Mes { get; private set; } = new(hoy().Year, hoy().Month, 1);

    /// <summary>Plantilla cuya eliminación se está confirmando.</summary>
    public Guid? ConfirmandoEliminar { get; set; }

    /// <summary>Plantilla recién creada cuyo día ya pasó este mes: no se genera sola, se ofrece crear el gasto.</summary>
    public (string Nombre, int Dia)? DiaPasado { get; set; }

    /// <summary>Nombre del mes mostrado.</summary>
    public string NombreMes => Mes.ToString("MMMM yyyy", Es);

    /// <summary>Día del mes de hoy.</summary>
    public int DiaHoy => hoy().Day;

    /// <summary>Nombre del mes en curso.</summary>
    public string NombreMesActual => hoy().ToString("MMMM yyyy", Es);

    // ───── Formulario ─────

    /// <summary>Plantilla que se está editando; null al crear una nueva.</summary>
    public Guid? Editando { get; private set; }

    /// <summary>Importe tal como lo escribe la persona.</summary>
    public string Importe { get; set; } = "";

    /// <summary>Concepto del formulario.</summary>
    public string Concepto { get; set; } = "";

    /// <summary>Si la plantilla genera gastos personales (solo de quien paga, fuera de la liquidación).</summary>
    public bool Personal { get; set; }

    /// <summary>Día del mes elegido.</summary>
    public int Dia { get; set; } = 1;

    /// <summary>Categoría elegida.</summary>
    public Guid Categoria { get; set; }

    /// <summary>Quién paga.</summary>
    public Guid Pagador { get; set; }

    /// <summary>Perfil de reparto elegido.</summary>
    public Guid Perfil { get; set; }

    /// <summary>El importe y el día del formulario son válidos.</summary>
    public bool FormularioValido => EntradaImporte.TryParse(Importe, out _) && Dia is >= 1 and <= 28;

    /// <summary>Faltan adultos, categorías o perfiles para crear plantillas.</summary>
    public bool FaltanDatosParaCrear => Adultos.Count == 0 || Categorias.Count == 0 || Perfiles.Count == 0;

    // ───── Nombres para pintar ─────

    /// <summary>Ruta «Padre › Hija» de la categoría.</summary>
    public string NombreCategoria(Guid id) => ArbolCategorias.Ruta(Categorias, id);

    /// <summary>Nombre del perfil, o «—» si no existe.</summary>
    public string NombrePerfil(Guid id) => Perfiles.FirstOrDefault(p => p.Id == id)?.Nombre ?? "—";

    /// <summary>Concepto de la plantilla o, si no tiene, el de su categoría.</summary>
    public string NombrePlantilla(GastoRecurrenteResponse p)
        => string.IsNullOrWhiteSpace(p.Concepto) ? NombreCategoria(p.CategoriaId) : p.Concepto;

    // ───── Operaciones ─────

    /// <summary>Marca el modelo como «cargando» antes de pedir los permisos y los datos.</summary>
    public void IniciarCarga()
    {
        Cargando = true;
        ErrorCarga = null;
    }

    /// <summary>Carga miembros, categorías, perfiles y plantillas y reinicia el formulario; si falla deja el mensaje en <see cref="ErrorCarga"/>.</summary>
    public async Task CargarAsync()
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
            await RecargarAsync();
            ReiniciarFormulario();
        }
        catch (ApiException e)
        {
            ErrorCarga = e.Message;
        }

        Cargando = false;
    }

    private async Task RecargarAsync()
        => Plantillas = (await api.ListarRecurrentesAsync()).OrderBy(p => p.DiaMes).ThenBy(p => p.Concepto).ToList();

    /// <summary>Pasa al mes anterior (-1) o siguiente (1) y olvida el último resultado.</summary>
    public void CambiarMes(int delta)
    {
        Mes = Mes.AddMonths(delta);
        Resultado = null;
    }

    /// <summary>Vuelve al formulario de alta con los valores por defecto.</summary>
    public void ReiniciarFormulario()
    {
        Editando = null;
        Importe = "";
        Concepto = "";
        Personal = false;
        Dia = 1;
        Categoria = Arbol.Count > 0 ? Arbol[0].Categoria.Id : Guid.Empty;
        Pagador = (Adultos.FirstOrDefault(m => m.EsYo) ?? Adultos.FirstOrDefault())?.Id ?? Guid.Empty;
        Perfil = Perfiles.FirstOrDefault()?.Id ?? Guid.Empty;
        AlCambiarCategoria();
    }

    /// <summary>Al elegir una categoría se propone su perfil de reparto por defecto.</summary>
    public void AlCambiarCategoria()
    {
        if (Editando is null && Categorias.FirstOrDefault(c => c.Id == Categoria)?.PerfilRepartoId is { } p) Perfil = p;
    }

    /// <summary>Carga una plantilla en el formulario para editarla.</summary>
    public void EmpezarEdicion(GastoRecurrenteResponse p)
    {
        Editando = p.Id;
        Importe = p.Importe.ToString(CultureInfo.InvariantCulture);
        Concepto = p.Concepto ?? "";
        Personal = p.Personal;
        Dia = p.DiaMes;
        Categoria = p.CategoriaId;
        Pagador = p.PagadoPor;
        Perfil = p.PerfilRepartoId;
        Error = null;
    }

    /// <summary>Crea o guarda la plantilla del formulario; devuelve el aviso, o null si falló (el motivo queda en <see cref="Error"/>).</summary>
    public Task<string?> GuardarAsync()
    {
        DiaPasado = null;
        return EjecutarAsync(async () =>
        {
            if (!EntradaImporte.TryParse(Importe, out var importe))
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, EntradaImporte.MensajeInvalido);
            // Al editar se conserva si la plantilla estaba pausada: el formulario no toca ese estado.
            var activo = Editando is { } actual ? Plantillas.FirstOrDefault(p => p.Id == actual)?.Activo ?? true : true;
            var peticion = new GastoRecurrenteRequest(importe, Categoria, Pagador, Perfil, (short)Dia,
                string.IsNullOrWhiteSpace(Concepto) ? null : Concepto.Trim(), activo, Personal);
            if (Editando is { } id)
            {
                await api.GuardarRecurrenteAsync(id, peticion);
                ReiniciarFormulario();
                return "Plantilla guardada";
            }

            await api.CrearRecurrenteAsync(peticion);
            // El día de hoy también cuenta: la plantilla solo genera su gasto al pedirlo, así que hay que ofrecerlo igual que si hubiera pasado.
            if (activo && Dia <= hoy().Day)
                DiaPasado = (string.IsNullOrWhiteSpace(Concepto) ? NombreCategoria(Categoria) : Concepto.Trim(), Dia);
            Importe = "";
            Concepto = "";
            return "Plantilla añadida";
        });
    }

    /// <summary>Pausa o reanuda la plantilla.</summary>
    public Task<string?> AlternarActivaAsync(GastoRecurrenteResponse p) =>
        EjecutarAsync(async () =>
        {
            await api.GuardarRecurrenteAsync(p.Id, new GastoRecurrenteRequest(
                p.Importe, p.CategoriaId, p.PagadoPor, p.PerfilRepartoId, p.DiaMes, p.Concepto, !p.Activo, p.Personal));
            return p.Activo ? "Plantilla pausada" : "Plantilla reanudada";
        });

    /// <summary>Elimina la plantilla; con gastos ya generados el servidor responde 409 y se sugiere pausarla.</summary>
    public Task<string?> EliminarAsync(GastoRecurrenteResponse p) =>
        EjecutarAsync(async () =>
        {
            try
            {
                await api.EliminarRecurrenteAsync(p.Id);
            }
            catch (ApiException e) when (e.Codigo == System.Net.HttpStatusCode.Conflict)
            {
                throw new ApiException(e.Codigo, $"{e.Message} Si ya generó gastos, pausa la plantilla en lugar de eliminarla.");
            }

            ConfirmandoEliminar = null;
            if (Editando == p.Id) ReiniciarFormulario();
            return "Plantilla eliminada";
        });

    /// <summary>Genera los gastos pendientes del mes en curso (idempotente) desde el aviso de día pasado.</summary>
    public Task<string?> GenerarMesActualAsync()
    {
        Mes = new DateOnly(hoy().Year, hoy().Month, 1);
        DiaPasado = null;
        return GenerarAsync();
    }

    /// <summary>
    /// Genera los gastos pendientes del mes elegido. Deja el texto del resultado en <see cref="Resultado"/> y devuelve el aviso, o null
    /// si falló.
    /// </summary>
    public async Task<string?> GenerarAsync()
    {
        Error = null;
        Resultado = null;
        Generando = true;
        try
        {
            var r = await api.GenerarRecurrentesAsync(Formato.Mes(Mes));
            Resultado = r.Creados == 0
                ? $"No se ha creado ningún gasto: {r.YaExistentes} {(r.YaExistentes == 1 ? "plantilla ya tenía" : "plantillas ya tenían")} su gasto en {NombreMes}."
                : $"Se {(r.Creados == 1 ? "ha creado 1 gasto" : $"han creado {r.Creados} gastos")} en {NombreMes}"
                  + (r.YaExistentes > 0 ? $" ({r.YaExistentes} ya existían)." : ".");
            return "Gastos generados";
        }
        catch (ApiException e)
        {
            Error = e.Message;
            return null;
        }
        finally
        {
            Generando = false;
        }
    }

    /// <summary>Ejecuta una operación y recarga las plantillas; los errores de la API quedan en <see cref="Error"/>.</summary>
    private async Task<string?> EjecutarAsync(Func<Task<string>> accion)
    {
        if (Guardando) return null; // evita duplicar plantillas con doble clic o doble Intro
        Guardando = true;
        Error = null;
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
