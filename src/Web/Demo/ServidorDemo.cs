using System.Net;
using System.Net.Http.Json;
using MiParte.Contracts;
using MiParte.Core.Application;
using MiParte.Core.Application.Categorias;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Application.Gastos;
using MiParte.Core.Application.Liquidacion;
using MiParte.Core.Application.Miembros;
using MiParte.Core.Application.Perfiles;
using MiParte.Core.Application.Recurrentes;
using MiParte.Core.Domain;
using MiParte.Core.Mapeo;

namespace MiParte.Web.Demo;

/// <summary>
/// Core.Api en miniatura para el modo demo: responde a las mismas rutas que <c>CoreApiClient</c> y devuelve los mismos DTO, pero
/// ejecutando los <b>casos de uso reales</b> de <c>Core.Application</c> sobre repositorios en memoria
/// (<see cref="RepositoriosEnMemoria"/>) y el mismo mapeo a DTO que Core.Api (<c>Core.Mapeo</c>). Por eso las reglas, los mensajes y
/// los códigos son los del servidor y no una copia que diverge. Parte de un hogar de ejemplo con el mes anterior y el actual;
/// los cambios viven solo hasta recargar la página.
/// </summary>
public sealed partial class ServidorDemo
{
    private static readonly Guid IdUsuario = Guid.Parse(CuentaDemo.IdUsuarioLocal);

    private readonly AlmacenDemo _almacen;
    private readonly DateOnly _hoy;
    private readonly ServicioLiquidacion _liquidacion;
    private readonly ServicioGastos _gastos;
    private readonly ServicioCuentaComun _cuenta;
    private readonly ServicioMiembros _miembros;
    private readonly ServicioRecurrentes _recurrentes;
    private readonly ServicioPerfiles _perfiles;
    private readonly ServicioCategorias _categorias;
    private readonly ServicioCierres _cierres;

    /// <summary>Crea el servidor con el hogar de ejemplo, fechado respecto a hoy.</summary>
    /// <param name="reloj">Reloj para situar los gastos de ejemplo en el mes actual y el anterior.</param>
    public ServidorDemo(TimeProvider reloj)
    {
        _hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        _almacen = Sembrar(_hoy);

        var gastos = new RepositorioGastosEnMemoria(_almacen);
        _gastos = new ServicioGastos(gastos);
        _liquidacion = new ServicioLiquidacion(new RepositorioLiquidacionEnMemoria(_almacen), reloj);
        _cuenta = new ServicioCuentaComun(new RepositorioCuentaComunEnMemoria(_almacen), reloj);
        _miembros = new ServicioMiembros(new RepositorioMiembrosEnMemoria(_almacen), reloj);
        _recurrentes = new ServicioRecurrentes(new RepositorioRecurrentesEnMemoria(_almacen), gastos);
        _perfiles = new ServicioPerfiles(new RepositorioPerfilesEnMemoria(_almacen));
        _categorias = new ServicioCategorias(new RepositorioCategoriasEnMemoria(_almacen), gastos);
        _cierres = new ServicioCierres(new RepositorioCierresEnMemoria(_almacen), reloj);
    }

    private HogarResumen Hogar => _almacen.Hogar.ADto();

    /// <summary>Atiende una petición dirigida a Core.Api y devuelve la respuesta que daría el servidor real.</summary>
    /// <param name="peticion">Petición del <c>CoreApiClient</c>.</param>
    /// <param name="ct">Token de cancelación.</param>
    public async Task<HttpResponseMessage> ResponderAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        var uri = peticion.RequestUri!;
        var ruta = uri.AbsolutePath.Trim('/').Split('/');
        if (ruta.Length < 2 || ruta[0] != "api") return NoEncontrado();

        var mes = Consulta(uri, "mes");
        Guid? id = ruta.Length > 2 && Guid.TryParse(ruta[^1], out var g) ? g : null;
        var subruta = ruta.Length > 2 ? ruta[2] : "";

        // Un cuerpo ausente (p. ej. una invitación sin miembro) llega como null.
        async Task<T?> Cuerpo<T>() => peticion.Content is null ? default : await peticion.Content.ReadFromJsonAsync<T>(ct);

        return (ruta[1], ruta.Length, peticion.Method.Method) switch
        {
            ("yo", 2, "GET") => Ok(new YoResponse(CuentaDemo.IdUsuarioLocal, [Hogar], Hogar)),
            ("hogar", 2, "DELETE") => Mal(HttpStatusCode.Conflict, "En el modo demo no se puede eliminar el hogar."),
            ("hogares", 2, "POST") => Mal(HttpStatusCode.Conflict, "En el modo demo no se pueden crear hogares."),
            ("invitaciones", 3, "POST") when subruta == "aceptar" => Mal(HttpStatusCode.Conflict, "En el modo demo no se pueden aceptar invitaciones."),
            ("enlaces-grupo", 3, "POST") when subruta == "unirse" => Mal(HttpStatusCode.Conflict, "En el modo demo no se pueden usar enlaces de grupo."),

            ("miembros", 2, "GET") => Ok((await _miembros.ListarAsync(Consulta(uri, "incluirInactivos") == "true", ct))
                .Select(m => m.ADto(IdUsuario)).ToList()),
            ("miembros", 2, "POST") => await CrearMiembro((await Cuerpo<CrearMiembroRequest>())!, ct),
            ("miembros", 3, "PUT") when id is { } i => await ActualizarMiembro(i, (await Cuerpo<ActualizarMiembroRequest>())!, ct),
            ("miembros", 3, "DELETE") when id is { } i => await DesactivarMiembro(i, ct),
            ("invitaciones", 2, "POST") => await CrearInvitacion(await Cuerpo<CrearInvitacionRequest>(), ct),

            ("categorias", 2, "GET") => Ok((await _categorias.ListarAsync(ct)).Select(c => c.ADto()).ToList()),
            ("categorias", 2, "POST") => await GuardarCategoria(null, (await Cuerpo<GuardarCategoriaRequest>())!, ct),
            ("categorias", 3, "PUT") when id is { } i => await GuardarCategoria(i, (await Cuerpo<GuardarCategoriaRequest>())!, ct),
            ("categorias", 3, "DELETE") when id is { } i => Sin(await _categorias.EliminarAsync(i, DestinoDeMovimiento(uri), ct)),

            ("perfiles", 2, "GET") => Ok((await _perfiles.ListarAsync(ct)).Select(p => p.ADto()).ToList()),
            ("perfiles", 3, "GET") when id is { } i => await ObtenerPerfil(i, ct),
            ("perfiles", 2, "POST") => await GuardarPerfil(null, (await Cuerpo<GuardarPerfilRequest>())!, ct),
            ("perfiles", 3, "PUT") when id is { } i => await GuardarPerfil(i, (await Cuerpo<GuardarPerfilRequest>())!, ct),
            ("perfiles", 3, "DELETE") when id is { } i => Sin(await _perfiles.EliminarAsync(i, DestinoDeMovimiento(uri), ct)),

            ("gastos", 2, "GET") => await ListarGastos(uri, ct),
            ("gastos", 3, "GET") when id is { } i => await ObtenerGasto(i, ct),
            ("gastos", 2, "POST") => await CrearGasto((await Cuerpo<GastoRequest>())!, ct),
            ("gastos", 3, "PUT") when id is { } i => await EditarGasto(i, (await Cuerpo<GastoRequest>())!, ct),
            ("gastos", 3, "DELETE") when id is { } i => Sin(await _gastos.BorrarAsync(i, ct)),

            ("cierres-mes", 2, "GET") => Ok((await _cierres.ListarAsync(ct)).Select(c => c.ADto()).ToList()),
            ("cierres-mes", 2, "POST") => await CerrarMes(await Cuerpo<CerrarMesRequest>(), ct),
            ("cierres-mes", 3, "DELETE") => await ReabrirMes(ruta[2], ct),

            ("gastos-recurrentes", 2, "GET") => Ok((await _recurrentes.ListarAsync(ct)).Select(r => r.ADto()).ToList()),
            ("gastos-recurrentes", 3, "GET") when id is { } i => await ObtenerRecurrente(i, ct),
            ("gastos-recurrentes", 2, "POST") => await CrearRecurrente((await Cuerpo<GastoRecurrenteRequest>())!, ct),
            ("gastos-recurrentes", 3, "POST") when subruta == "generar" => await Generar(mes, ct),
            ("gastos-recurrentes", 3, "PUT") when id is { } i => await EditarRecurrente(i, (await Cuerpo<GastoRecurrenteRequest>())!, ct),
            ("gastos-recurrentes", 3, "DELETE") when id is { } i => Sin(await _recurrentes.BorrarAsync(i, ct)),

            // El modo demo no registra cambios: el historial siempre sale vacío.
            ("auditoria", 2, "GET") => Ok(new List<EventoAuditoriaDto>()),

            ("cuenta-comun", 2, "GET") => await EstadoCuentaComun(mes, ct),
            ("cuenta-comun", 4, "PUT") when subruta == "ahorro" && ruta[3] == "activacion" => await ActivarAhorro((await Cuerpo<ActivarAhorroRequest>())!, ct),
            ("cuenta-comun", 3, "PUT") when subruta == "activacion" => Ok(new { activa = await _cuenta.ActivarCuentaAsync((await Cuerpo<ActivarCuentaComunRequest>())!.Activa, ct) }),
            ("cuenta-comun", 3, "PUT") when subruta == "aportaciones" => await FijarAportacion((await Cuerpo<FijarAportacionRequest>())!, ct),
            ("cuenta-comun", 3, "PUT") when subruta == "saldo-inicial" => await FijarSaldoInicial((await Cuerpo<FijarSaldoInicialRequest>())!, ct),
            ("cuenta-comun", 3, "DELETE") when subruta == "saldo-inicial" => Sin(await _cuenta.EliminarSaldoInicialAsync(ct)),
            ("cuenta-comun", 3, "POST") when subruta == "reembolsos" => await CrearReembolso((await Cuerpo<CrearReembolsoRequest>())!, ct),
            ("cuenta-comun", 4, "DELETE") when subruta == "reembolsos" && id is { } i => Sin(await _cuenta.EliminarReembolsoAsync(i, ct)),
            ("cuenta-comun", 3, "POST") when subruta == "depositos-ahorro" => await CrearDeposito((await Cuerpo<CrearDepositoAhorroRequest>())!, ct),
            ("cuenta-comun", 4, "DELETE") when subruta == "depositos-ahorro" && id is { } i => Sin(await _cuenta.EliminarDepositoAsync(i, ct)),
            ("cuenta-comun", 3, "POST") when subruta == "retiradas-ahorro" => await CrearRetirada((await Cuerpo<CrearRetiradaAhorroRequest>())!, ct),
            ("cuenta-comun", 4, "DELETE") when subruta == "retiradas-ahorro" && id is { } i => Sin(await _cuenta.EliminarRetiradaAsync(i, ct)),

            ("chat", 2, "POST") => Ok(new ChatResponse(
                "En el modo demo el asistente no está conectado a un modelo de lenguaje, así que no puedo consultar los datos de ejemplo. "
                + "Con una cuenta real respondo preguntas como «¿cuánto gasté en alimentación en junio?» o «¿quién debe a quién este mes?».",
                [])),

            ("resumen", 2, "GET") => await Resumen(mes, ct),
            ("liquidacion", 2, "GET") => await Liquidacion(mes, ct),
            ("resumen-liquidacion", 2, "GET") => await ResumenYLiquidacion(mes, ct),
            ("pagos-liquidacion", 2, "POST") => await CrearPago((await Cuerpo<CrearPagoLiquidacionRequest>())!, ct),
            ("pagos-liquidacion", 3, "DELETE") when id is { } i => Sin(await _liquidacion.EliminarPagoAsync(i, ct)),

            _ => NoEncontrado(),
        };
    }

    // ───── Miembros e invitaciones ─────

    private async Task<HttpResponseMessage> CrearMiembro(CrearMiembroRequest r, CancellationToken ct)
    {
        var res = await _miembros.CrearAsync(IdUsuario, IdHogar, new NuevoMiembro(r.Nombre, r.Tipo, r.ResponsableId), ct);
        return res.Error is { } e ? Error(e, mensajeEn404: true) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> ActualizarMiembro(Guid id, ActualizarMiembroRequest r, CancellationToken ct)
    {
        var res = await _miembros.ActualizarAsync(IdUsuario, id, new CambiosMiembro(r.Nombre, r.Activo, r.ResponsableId, r.Rol, r.Permisos), ct);
        return res.Error is { } e ? Error(e, mensajeEn404: true) : Ok(res.Valor!.ADto(IdUsuario));
    }

    private async Task<HttpResponseMessage> DesactivarMiembro(Guid id, CancellationToken ct)
    {
        var res = await _miembros.DesactivarAsync(IdUsuario, id, ct);
        return res.Error is { } e ? Error(e, mensajeEn404: true) : Ok(res.Valor!.ADto(IdUsuario));
    }

    private async Task<HttpResponseMessage> CrearInvitacion(CrearInvitacionRequest? r, CancellationToken ct)
    {
        // El token solo sirve para mostrarlo: en la demo nadie puede aceptarlo.
        var res = await _miembros.CrearInvitacionAsync(IdUsuario, IdHogar, r?.MiembroId, ct);
        return res.Error is { } e
            ? Error(e, mensajeEn404: true)
            : Respuesta(HttpStatusCode.Created, new InvitacionCreada(res.Valor!.Id, res.Valor.Token, res.Valor.CaducaEn));
    }

    // ───── Categorías y perfiles ─────

    /// <summary>Destino indicado en <c>?moverA=</c> al borrar una categoría o un perfil en uso; null si no viene o no es un Guid.</summary>
    private static Guid? DestinoDeMovimiento(Uri uri) => Guid.TryParse(Consulta(uri, "moverA"), out var g) ? g : null;

    private async Task<HttpResponseMessage> GuardarCategoria(Guid? id, GuardarCategoriaRequest r, CancellationToken ct)
    {
        var datos = new DatosCategoria(r.Nombre, r.CategoriaPadreId, r.PerfilRepartoId, r.ACargoCuentaComun);
        var res = id is { } existente
            ? await _categorias.ActualizarAsync(existente, datos, ct)
            : await _categorias.CrearAsync(IdHogar, datos, ct);
        return res.Error is { } e ? Error(e) : Respuesta(id is null ? HttpStatusCode.Created : HttpStatusCode.OK, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> ObtenerPerfil(Guid id, CancellationToken ct)
        => await _perfiles.ObtenerAsync(id, ct) is { } p ? Ok(p.ADto()) : NoEncontrado();

    private async Task<HttpResponseMessage> GuardarPerfil(Guid? id, GuardarPerfilRequest r, CancellationToken ct)
    {
        var detalle = r.Detalle?.Select(d => new ValorMiembro(d.MiembroId, d.Valor)).ToList();
        var res = id is { } existente
            ? await _perfiles.ActualizarAsync(existente, r.Nombre, r.Modo, detalle, ct)
            : await _perfiles.CrearAsync(IdHogar, r.Nombre, r.Modo, detalle, ct);
        return res.Error is { } e ? Error(e) : Respuesta(id is null ? HttpStatusCode.Created : HttpStatusCode.OK, res.Valor!.ADto());
    }

    // ───── Gastos ─────

    private static DatosGasto Datos(GastoRequest r)
        => new(r.Fecha, r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.Concepto, r.PagadoDesdeAhorro, r.Personal);

    private async Task<HttpResponseMessage> ListarGastos(Uri uri, CancellationToken ct)
    {
        Guid? Id(string clave) => Guid.TryParse(Consulta(uri, clave), out var g) ? g : null;
        if (ConsultasApi.FiltroGastos(
                Consulta(uri, "mes"), Consulta(uri, "desde"), Consulta(uri, "hasta"), Id("categoriaId"), Id("miembroId"),
                Consulta(uri, "buscar"), out var filtro) is { } error)
            return Mal(error);

        return Ok((await _gastos.ListarAsync(filtro, ct)).Select(g => g.ADto()).ToList());
    }

    private async Task<HttpResponseMessage> ObtenerGasto(Guid id, CancellationToken ct)
        => await _gastos.ObtenerAsync(id, ct) is { } g ? Ok(g.ADto()) : NoEncontrado();

    private async Task<HttpResponseMessage> CrearGasto(GastoRequest r, CancellationToken ct)
    {
        var res = await _gastos.CrearAsync(IdHogar, Datos(r), ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> EditarGasto(Guid id, GastoRequest r, CancellationToken ct)
    {
        var res = await _gastos.EditarAsync(id, Datos(r), ct);
        return res.Error is { } e ? Error(e) : Ok(res.Valor!.ADto());
    }

    // ───── Cierre de mes ─────

    private async Task<HttpResponseMessage> CerrarMes(CerrarMesRequest? r, CancellationToken ct)
    {
        var yo = await _miembros.MiembroActivoAsync(IdUsuario, ct);
        if (yo is null) return Mal(HttpStatusCode.Forbidden, Mensajes.SoloMiembroCierra);
        if (!TryMes(r?.Mes, out var inicio)) return MesInvalido();

        var res = await _cierres.CerrarAsync(IdHogar, inicio, yo, ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> ReabrirMes(string mes, CancellationToken ct)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        return Sin(await _cierres.ReabrirAsync(inicio, ct));
    }

    // ───── Recurrentes ─────

    private static DatosRecurrente Datos(GastoRecurrenteRequest r)
        => new(r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.DiaMes, r.Concepto, r.Activo);

    private async Task<HttpResponseMessage> ObtenerRecurrente(Guid id, CancellationToken ct)
        => await _recurrentes.ObtenerAsync(id, ct) is { } r ? Ok(r.ADto()) : NoEncontrado();

    private async Task<HttpResponseMessage> CrearRecurrente(GastoRecurrenteRequest r, CancellationToken ct)
    {
        var res = await _recurrentes.CrearAsync(IdHogar, Datos(r), ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> EditarRecurrente(Guid id, GastoRecurrenteRequest r, CancellationToken ct)
    {
        var res = await _recurrentes.EditarAsync(id, Datos(r), ct);
        return res.Error is { } e ? Error(e) : Ok(res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> Generar(string? mes, CancellationToken ct)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        var res = await _recurrentes.GenerarAsync(inicio, ct);
        return res.Error is { } e ? Error(e) : Ok(new GenerarRecurrentesResponse(FormatosApi.FormatoMes(res.Valor!.Mes), res.Valor.Creados, res.Valor.YaExistentes));
    }

    // ───── Cuenta común ─────

    private async Task<HttpResponseMessage> EstadoCuentaComun(string? mes, CancellationToken ct)
        => TryMes(mes, out var inicio) ? Ok((await _cuenta.EstadoAsync(inicio, ct)).ADto()) : MesInvalido();

    private async Task<HttpResponseMessage> ActivarAhorro(ActivarAhorroRequest r, CancellationToken ct)
    {
        var res = await _cuenta.ActivarAhorroAsync(r.Activo, ct);
        return res.Error is { } e ? Error(e) : Ok(new { activo = res.Valor });
    }

    private async Task<HttpResponseMessage> FijarAportacion(FijarAportacionRequest r, CancellationToken ct)
    {
        var res = await _cuenta.FijarAportacionAsync(new FijarAportacion(IdHogar, r.MiembroId, r.Desde, r.Importe, r.Ahorro), ct);
        return res.Error is { } e ? Error(e) : Ok(res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> FijarSaldoInicial(FijarSaldoInicialRequest r, CancellationToken ct)
    {
        var res = await _cuenta.FijarSaldoInicialAsync(r.Fecha, r.Importe, ct);
        return res.Error is { } e ? Error(e) : Ok(new SaldoInicialDto(res.Valor!.Fecha, res.Valor.Importe));
    }

    private async Task<HttpResponseMessage> CrearReembolso(CrearReembolsoRequest r, CancellationToken ct)
    {
        var res = await _cuenta.CrearReembolsoAsync(new RegistrarMovimiento(IdHogar, r.MiembroId, r.Importe, r.Fecha, r.Concepto), ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> CrearDeposito(CrearDepositoAhorroRequest r, CancellationToken ct)
    {
        var res = await _cuenta.CrearDepositoAsync(new RegistrarMovimiento(IdHogar, r.MiembroId, r.Importe, r.Fecha, r.Concepto), ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    private async Task<HttpResponseMessage> CrearRetirada(CrearRetiradaAhorroRequest r, CancellationToken ct)
    {
        var res = await _cuenta.CrearRetiradaAsync(new RegistrarMovimiento(IdHogar, r.MiembroId, r.Importe, r.Fecha, r.Concepto), ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    // ───── Resumen y liquidación ─────

    private async Task<HttpResponseMessage> Resumen(string? mes, CancellationToken ct)
        => TryMes(mes, out var inicio) ? Ok((await _liquidacion.ResumenAsync(inicio, ct)).ADto()) : MesInvalido();

    private async Task<HttpResponseMessage> Liquidacion(string? mes, CancellationToken ct)
        => TryMes(mes, out var inicio) ? Ok((await _liquidacion.LiquidacionAsync(inicio, ct)).ADto(inicio)) : MesInvalido();

    private async Task<HttpResponseMessage> ResumenYLiquidacion(string? mes, CancellationToken ct)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        var r = await _liquidacion.ResumenYLiquidacionAsync(inicio, ct);
        return Ok(new ResumenYLiquidacionResponse(r.Resumen.ADto(), r.Liquidacion.ADto(inicio)));
    }

    private async Task<HttpResponseMessage> CrearPago(CrearPagoLiquidacionRequest r, CancellationToken ct)
    {
        var res = await _liquidacion.RegistrarPagoAsync(
            new RegistrarPago(IdHogar, r.Mes, r.DeMiembroId, r.AMiembroId, r.Importe, r.Fecha, r.Concepto), ct);
        return res.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.Created, res.Valor!.ADto());
    }

    // ───── Utilidades HTTP ─────

    private static bool TryMes(string? mes, out DateOnly inicio) => FormatosApi.TryMes(mes, out inicio);

    private static string? Consulta(Uri uri, string clave)
    {
        foreach (var par in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            if (i > 0 && par[..i] == clave) return Uri.UnescapeDataString(par[(i + 1)..]);
        }

        return null;
    }

    /// <summary>Traduce el fallo de un caso de uso al código y cuerpo que devolvería Core.Api.</summary>
    private static HttpResponseMessage Error(ErrorAplicacion e, bool mensajeEn404 = false) => e.Tipo switch
    {
        TipoError.NoEncontrado => mensajeEn404 ? Mal(HttpStatusCode.NotFound, e.Mensaje) : NoEncontrado(),
        TipoError.Conflicto => Respuesta(
            HttpStatusCode.Conflict,
            new Dictionary<string, object>(e.Extra ?? new Dictionary<string, object>()) { ["error"] = e.Mensaje }),
        TipoError.Prohibido => Mal(HttpStatusCode.Forbidden, e.Mensaje),
        _ => Mal(e.Mensaje),
    };

    /// <summary>204 si el caso de uso salió bien; si no, su error.</summary>
    private static HttpResponseMessage Sin<T>(Resultado<T> r) => r.Error is { } e ? Error(e) : Respuesta(HttpStatusCode.NoContent, null);

    private static HttpResponseMessage Respuesta(HttpStatusCode estado, object? cuerpo) =>
        new(estado) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo, cuerpo.GetType()) };

    private static HttpResponseMessage Ok(object cuerpo) => Respuesta(HttpStatusCode.OK, cuerpo);

    private static HttpResponseMessage NoEncontrado() => Respuesta(HttpStatusCode.NotFound, null);

    private static HttpResponseMessage Mal(string mensaje) => Mal(HttpStatusCode.BadRequest, mensaje);

    private static HttpResponseMessage Mal(HttpStatusCode estado, string mensaje) => Respuesta(estado, new { error = mensaje });

    private static HttpResponseMessage MesInvalido() => Mal("El mes debe tener el formato YYYY-MM.");
}
