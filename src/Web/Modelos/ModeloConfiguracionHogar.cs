using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Hogares;

namespace MiParte.Web.Modelos;

/// <summary>Qué ocurrió al eliminar el hogar: el aviso a mostrar y adónde llevar a la persona sin él.</summary>
/// <param name="Aviso">Texto del aviso.</param>
/// <param name="Destino">Pantalla a la que ir (crear o elegir otro hogar, o la de inicio).</param>
public sealed record HogarEliminado(string Aviso, DestinoArranque Destino);

/// <summary>
/// Estado y orquestación de la configuración del hogar: funciones activadas, miembros, invitaciones y eliminación del hogar. Lo que
/// depende del navegador (portapapeles, «Compartir», navegación) se queda en la vista. No depende de Blazor.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
/// <param name="hogar">Hogar actual y sus funciones activadas.</param>
/// <param name="permisos">Permisos del usuario, que se actualizan al recargar los miembros.</param>
/// <param name="arranque">Flujo de arranque, para decidir adónde ir tras eliminar el hogar.</param>
public sealed class ModeloConfiguracionHogar(CoreApiClient api, EstadoHogar hogar, ServicioPermisos permisos, ServicioArranque arranque)
{
    // ───── Datos ─────

    /// <summary>Miembros del hogar.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Perfiles de reparto del hogar.</summary>
    public List<PerfilRepartoDto> Perfiles { get; private set; } = [];

    /// <summary>Adultos activos, que pueden ser responsables de un miembro a cargo.</summary>
    public IEnumerable<MiembroDto> Adultos => Miembros.Where(m => m.Tipo == TiposMiembro.Adulto && m.Activo);

    /// <summary>Si el hogar tiene habilitada la cuenta común.</summary>
    public bool CuentaComunActiva => hogar.HogarActual?.CuentaComunActiva ?? false;

    /// <summary>Si el hogar tiene activado el ahorro.</summary>
    public bool AhorroActivo => hogar.HogarActual?.AhorroActivo ?? false;

    /// <summary>Si el hogar genera gastos recurrentes (plantillas y generación mensual).</summary>
    public bool RecurrentesActivas => hogar.HogarActual?.RecurrentesActivas ?? false;

    /// <summary>Modo de liquidación del hogar: «mensual» o «ciclos».</summary>
    public string LiquidacionModo => hogar.HogarActual?.LiquidacionModo ?? ModosLiquidacion.Mensual;

    /// <summary>Nombre del miembro, o «—» si no está.</summary>
    public string NombreDe(Guid id) => Miembros.FirstOrDefault(m => m.Id == id)?.Nombre ?? "—";

    // ───── Estado de pantalla ─────

    /// <summary>Si se están cargando los datos.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Error de la carga.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Error de la última operación sobre miembros.</summary>
    public string? ErrorMiembros { get; private set; }

    /// <summary>Error al crear una invitación.</summary>
    public string? ErrorInvitacion { get; private set; }

    /// <summary>Error al cambiar una función del hogar.</summary>
    public string? ErrorFunciones { get; private set; }

    /// <summary>Error al eliminar el hogar.</summary>
    public string? ErrorEliminar { get; private set; }

    /// <summary>Si se está guardando un cambio de funciones.</summary>
    public bool GuardandoFunciones { get; private set; }

    /// <summary>Si se está creando una invitación.</summary>
    public bool CreandoInvitacion { get; private set; }

    /// <summary>Si se está eliminando el hogar.</summary>
    public bool Eliminando { get; private set; }

    // Cambia tras cada intento para recrear las casillas: si la API rechaza el cambio, el navegador no debe quedarse con la marca que puso la persona.
    private int _versionFunciones;

    /// <summary>Clave de la casilla de cuenta común (cambia tras cada intento).</summary>
    public string ClaveCuenta => $"cuenta-{_versionFunciones}";

    /// <summary>Clave de la casilla de ahorro (cambia tras cada intento).</summary>
    public string ClaveAhorro => $"ahorro-{_versionFunciones}";

    /// <summary>Clave de la casilla de recurrentes (cambia tras cada intento).</summary>
    public string ClaveRecurrentes => $"recurrentes-{_versionFunciones}";

    /// <summary>Clave del selector de liquidación (cambia tras cada intento).</summary>
    public string ClaveLiquidacion => $"liquidacion-{_versionFunciones}";

    // ───── Formularios ─────

    /// <summary>Miembro que se está renombrando.</summary>
    public Guid? Renombrando { get; set; }

    /// <summary>Nombre nuevo escrito al renombrar.</summary>
    public string NombreNuevo { get; set; } = "";

    /// <summary>Miembro cuya desactivación se está confirmando.</summary>
    public Guid? ConfirmandoDesactivar { get; set; }

    /// <summary>Nombre del miembro nuevo.</summary>
    public string NuevoNombre { get; set; } = "";

    /// <summary>Tipo del miembro nuevo («adulto» o «a_cargo»).</summary>
    public string NuevoTipo { get; set; } = TiposMiembro.Adulto;

    /// <summary>Responsable elegido para un miembro a cargo (id como texto; «» sin elegir).</summary>
    public string NuevoResponsable { get; set; } = "";

    /// <summary>Texto escrito para confirmar la eliminación del hogar.</summary>
    public string ConfirmarNombre { get; set; } = "";

    /// <summary>La última invitación creada, con su token en claro (solo se muestra una vez).</summary>
    public InvitacionCreada? Invitacion { get; private set; }

    /// <summary>Nombre de la persona a la que va dirigida la invitación; null si es abierta.</summary>
    public string? InvitacionPara { get; private set; }

    /// <summary>El texto escrito coincide con el nombre del hogar.</summary>
    public bool NombreCoincide => ConfirmarNombre.Trim() == hogar.HogarActual?.Nombre;

    /// <summary>Se puede añadir el miembro del formulario: tiene nombre y, si es a cargo, responsable.</summary>
    public bool NuevoMiembroValido
        => !string.IsNullOrWhiteSpace(NuevoNombre) && !(NuevoTipo == TiposMiembro.ACargo && string.IsNullOrEmpty(NuevoResponsable));

    // ───── Carga ─────

    /// <summary>Carga miembros y perfiles mostrando el indicador de carga.</summary>
    public async Task CargarAsync()
    {
        Cargando = true;
        ErrorCarga = null;
        await CargarSinParpadeoAsync();
        Cargando = false;
    }

    /// <summary>Recarga miembros y perfiles sin volver a mostrar el indicador de carga; actualiza los permisos del usuario.</summary>
    public async Task CargarSinParpadeoAsync()
    {
        try
        {
            var miembros = api.ListarMiembrosAsync();
            var perfiles = api.ListarPerfilesAsync();
            await Task.WhenAll(miembros, perfiles);
            Miembros = miembros.Result;
            permisos.Establecer(Miembros);
            Perfiles = perfiles.Result;
        }
        catch (ApiException e)
        {
            ErrorCarga = e.Message;
        }
    }

    // ───── Funciones del hogar ─────

    /// <summary>Habilita o deshabilita la cuenta común; devuelve el aviso, o null si la API lo rechazó.</summary>
    public Task<string?> CambiarCuentaComunAsync(bool activa) =>
        EjecutarFuncionAsync(
            () => api.ActivarCuentaComunAsync(activa), activa, AhorroActivo, RecurrentesActivas,
            activa ? "Cuenta común habilitada" : "Cuenta común deshabilitada");

    /// <summary>Activa o desactiva el ahorro; devuelve el aviso, o null si la API lo rechazó.</summary>
    public Task<string?> CambiarAhorroAsync(bool activo) =>
        EjecutarFuncionAsync(
            () => api.ActivarAhorroAsync(activo), CuentaComunActiva, activo, RecurrentesActivas,
            activo ? "Ahorro activado" : "Ahorro desactivado");

    /// <summary>Activa o desactiva los gastos recurrentes; desactivarlos no borra las plantillas. Devuelve el aviso, o null si la API lo rechazó.</summary>
    public Task<string?> CambiarRecurrentesAsync(bool activas) =>
        EjecutarFuncionAsync(
            () => api.ActivarRecurrentesAsync(activas), CuentaComunActiva, AhorroActivo, activas,
            activas ? "Recurrentes activadas" : "Recurrentes desactivadas");

    /// <summary>
    /// Cambia el modo de liquidación («mensual» o «ciclos»). No altera los gastos ni los ciclos cerrados. Devuelve el aviso, o null si
    /// la API lo rechazó.
    /// </summary>
    public async Task<string?> CambiarLiquidacionAsync(string modo)
    {
        ErrorFunciones = null;
        GuardandoFunciones = true;
        try
        {
            await api.ActivarLiquidacionAsync(modo);
            hogar.ActualizarLiquidacion(modo);
            return modo == ModosLiquidacion.Ciclos ? "Liquidación por ciclos" : "Liquidación mensual";
        }
        catch (ApiException e)
        {
            ErrorFunciones = e.Message;
            return null;
        }
        finally
        {
            GuardandoFunciones = false;
            _versionFunciones++;
        }
    }

    /// <summary>Aplica un cambio de funciones y, si la API lo acepta, lo refleja en el estado compartido (menú y pestañas).</summary>
    private async Task<string?> EjecutarFuncionAsync(Func<Task> accion, bool cuentaComun, bool ahorro, bool recurrentes, string aviso)
    {
        ErrorFunciones = null;
        GuardandoFunciones = true;
        try
        {
            await accion();
            hogar.ActualizarFunciones(cuentaComun, ahorro, recurrentes);
            return aviso;
        }
        catch (ApiException e)
        {
            ErrorFunciones = e.Message;
            return null;
        }
        finally
        {
            GuardandoFunciones = false;
            _versionFunciones++;
        }
    }

    // ───── Miembros ─────

    /// <summary>Empieza a renombrar al miembro con su nombre actual.</summary>
    public void EmpezarRenombrar(MiembroDto m)
    {
        Renombrando = m.Id;
        NombreNuevo = m.Nombre;
        ErrorMiembros = null;
    }

    /// <summary>Guarda el nombre nuevo.</summary>
    public Task<string?> GuardarNombreAsync(MiembroDto m) =>
        EjecutarMiembroAsync(async () =>
        {
            await api.ActualizarMiembroAsync(m.Id, new ActualizarMiembroRequest(Nombre: NombreNuevo.Trim()));
            Renombrando = null;
            return "Nombre actualizado";
        });

    /// <summary>Hace admin o quita el rol de admin al miembro.</summary>
    public Task<string?> CambiarRolAsync(MiembroDto m) =>
        EjecutarMiembroAsync(async () =>
        {
            await api.ActualizarMiembroAsync(m.Id, new ActualizarMiembroRequest(Rol: m.Rol == "admin" ? "miembro" : "admin"));
            return m.Rol == "admin" ? $"{m.Nombre} ya no es admin" : $"{m.Nombre} ahora es admin";
        });

    /// <summary>Desactiva al miembro.</summary>
    public Task<string?> DesactivarAsync(MiembroDto m) =>
        EjecutarMiembroAsync(async () =>
        {
            await api.ActualizarMiembroAsync(m.Id, new ActualizarMiembroRequest(Activo: false));
            ConfirmandoDesactivar = null;
            return $"{m.Nombre} desactivado";
        });

    /// <summary>Añade el miembro del formulario.</summary>
    public Task<string?> AnadirMiembroAsync() =>
        EjecutarMiembroAsync(async () =>
        {
            Guid? responsable = NuevoTipo == TiposMiembro.ACargo && Guid.TryParse(NuevoResponsable, out var r) ? r : null;
            var nombre = NuevoNombre.Trim();
            await api.CrearMiembroAsync(new CrearMiembroRequest(nombre, NuevoTipo, responsable));
            NuevoNombre = "";
            NuevoResponsable = "";
            return $"{nombre} añadido";
        });

    /// <summary>Ejecuta una operación sobre miembros y recarga la lista; los errores de la API quedan en <see cref="ErrorMiembros"/>.</summary>
    private async Task<string?> EjecutarMiembroAsync(Func<Task<string>> accion)
    {
        ErrorMiembros = null;
        try
        {
            var aviso = await accion();
            await CargarSinParpadeoAsync();
            return aviso;
        }
        catch (ApiException e)
        {
            ErrorMiembros = e.Message;
            return null;
        }
    }

    // ───── Invitaciones ─────

    /// <summary>
    /// Crea una invitación, abierta o para vincular a un miembro existente. Deja el resultado en <see cref="Invitacion"/> y devuelve
    /// el aviso, o null si falló.
    /// </summary>
    public async Task<string?> InvitarAsync(Guid? miembroId)
    {
        ErrorInvitacion = null;
        CreandoInvitacion = true;
        try
        {
            Invitacion = await api.CrearInvitacionAsync(new CrearInvitacionRequest(miembroId));
            InvitacionPara = miembroId is { } id ? NombreDe(id) : null;
            return "Invitación creada";
        }
        catch (ApiException e)
        {
            ErrorInvitacion = e.Message;
            return null;
        }
        finally
        {
            CreandoInvitacion = false;
        }
    }

    /// <summary>Oculta la invitación mostrada (el token no se vuelve a ver).</summary>
    public void OcultarInvitacion()
    {
        Invitacion = null;
        InvitacionPara = null;
    }

    // ───── Eliminar el hogar ─────

    /// <summary>
    /// Elimina el hogar actual (la API verifica permiso y nombre) y decide adónde llevar a la persona sin él. Devuelve null si no
    /// se podía (el nombre no coincide) o la API lo rechazó, con el motivo en <see cref="ErrorEliminar"/>.
    /// </summary>
    public async Task<HogarEliminado?> EliminarHogarAsync()
    {
        if (!NombreCoincide || hogar.HogarActual is not { } actual) return null;
        ErrorEliminar = null;
        Eliminando = true;
        try
        {
            await api.EliminarHogarAsync(actual.Nombre);
            var destino = await arranque.CargarAsync();
            return new HogarEliminado($"Hogar «{actual.Nombre}» eliminado", destino);
        }
        catch (ApiException e)
        {
            ErrorEliminar = e.Message;
            return null;
        }
        finally
        {
            Eliminando = false;
        }
    }
}
