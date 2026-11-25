using System.Globalization;
using MiParte.Core.Domain;
using MiParte.Web.Api;
using MiParte.Web.Autenticacion;

namespace MiParte.Web.Hogares;

/// <summary>
/// Generación automática de los gastos recurrentes: al entrar en un hogar crea los gastos del mes en curso cuyas plantillas ya han
/// llegado a su día. No hay proceso en segundo plano (ni Render ni Supabase lo garantizan en el plan gratuito), así que lo dispara la
/// app de quien entra, una vez al día por hogar; la generación es idempotente en el servidor, así que da igual quién llegue antes.
/// Un mes en el que nadie con el permiso entra después del día de una plantilla no la genera solo: queda el botón de la pestaña.
/// </summary>
public sealed class GeneradorRecurrentes
{
    private const string Prefijo = "miparte.recurrentes.";
    private static readonly CultureInfo Es = new("es-ES");

    private readonly CoreApiClient _api;
    private readonly EstadoHogar _hogar;
    private readonly ServicioPermisos _permisos;
    private readonly IAlmacenLocal _almacen;
    private readonly ServicioAvisos _avisos;
    private readonly TimeProvider _reloj;

    // Una sola generación por hogar y día en esta sesión, compartida por quien la pida mientras está en curso (la página y el cambio
    // de hogar llegan a la vez al arrancar), y sin reintentar si falla: el botón de la pestaña muestra el error.
    private readonly Dictionary<string, Task<int>> _enCurso = [];

    /// <summary>Crea el generador.</summary>
    /// <param name="api">Cliente de Core.Api.</param>
    /// <param name="hogar">Estado del hogar seleccionado.</param>
    /// <param name="permisos">Permisos del usuario en el hogar actual.</param>
    /// <param name="almacen">Almacén del navegador, donde se recuerda el último día generado de cada hogar.</param>
    /// <param name="avisos">Avisos breves de la interfaz.</param>
    /// <param name="reloj">Reloj (fecha local de quien usa la app).</param>
    public GeneradorRecurrentes(
        CoreApiClient api, EstadoHogar hogar, ServicioPermisos permisos, IAlmacenLocal almacen, ServicioAvisos avisos, TimeProvider reloj)
    {
        _api = api;
        _hogar = hogar;
        _permisos = permisos;
        _almacen = almacen;
        _avisos = avisos;
        _reloj = reloj;
    }

    /// <summary>
    /// Genera los gastos pendientes del mes en curso hasta hoy en el hogar actual, si tiene las recurrentes activadas y el usuario
    /// puede gestionarlas. Nunca lanza: un error de la API (mes cerrado, ahorro insuficiente…) se ignora y se deja para el botón.
    /// </summary>
    /// <returns>Gastos creados.</returns>
    public Task<int> GenerarPendientesAsync()
    {
        if (_hogar.HogarActual is not { RecurrentesActivas: true } actual) return Task.FromResult(0);
        var hoy = DateOnly.FromDateTime(_reloj.GetLocalNow().DateTime);
        var clave = ClaveEnCurso(actual.Id, hoy);
        if (_enCurso.TryGetValue(clave, out var tarea)) return tarea;
        tarea = GenerarAsync(actual.Id, hoy);
        // Si ya ha terminado (p. ej. lo generado hoy en otra sesión) no hace falta guardarla; y así no se guarda una que haya
        // pedido no quedarse en la caché antes de que se insertara.
        if (!tarea.IsCompleted) _enCurso[clave] = tarea;
        return tarea;
    }

    private static string ClaveEnCurso(Guid hogarId, DateOnly hoy) => $"{hogarId}|{hoy:yyyy-MM-dd}";

    private async Task<int> GenerarAsync(Guid hogarId, DateOnly hoy)
    {
        try
        {
            var clave = Prefijo + hogarId;
            var marca = hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (await _almacen.LeerAsync(clave) == marca) return 0; // ya generado hoy en este navegador

            await _permisos.AsegurarAsync();
            if (_hogar.HogarActual?.Id != hogarId)
            {
                // Se cambió de hogar mientras se cargaban los permisos (que ya son los del otro): este sigue sin generar, así que
                // sale de la caché para que volver a él hoy lo intente de nuevo.
                _enCurso.Remove(ClaveEnCurso(hogarId, hoy));
                return 0;
            }
            if (!_permisos.Tiene(CatalogoPermisos.RecurrentesGestionar)) return 0;

            // Con el hogar fijo en la petición, la marca corresponde siempre al hogar en el que se ha generado (en el servidor es
            // idempotente, así que no duplica).
            var r = await _api.GenerarRecurrentesAsync(Formato.Mes(new DateOnly(hoy.Year, hoy.Month, 1)), hoy.Day, hogarId);
            await _almacen.EscribirAsync(clave, marca);
            // El aviso solo si se sigue en ese hogar: en otro confundiría.
            if (r.Creados > 0 && _hogar.HogarActual?.Id == hogarId)
                _avisos.Mostrar(r.Creados == 1
                    ? $"Se ha creado 1 gasto recurrente de {hoy.ToString("MMMM", Es)}"
                    : $"Se han creado {r.Creados} gastos recurrentes de {hoy.ToString("MMMM", Es)}");
            return r.Creados;
        }
        catch (Exception e) when (e is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Un timeout llega como TaskCanceledException, no como ApiException: si escapara, la página que espera se quedaría
            // en «Cargando tu hogar…» y la tarea fallida seguiría en _enCurso el resto del día.
            return 0;
        }
    }
}
