using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MiParte.Contracts;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Web.Demo;

/// <summary>
/// Réplica en memoria de Core.Api para el modo demo: responde a las mismas rutas que <c>CoreApiClient</c> con los mismos
/// DTO y casi las mismas validaciones, y calcula repartos, resumen, liquidación y cuenta común con la lógica real de
/// <c>Core.Domain</c>. Parte de un hogar de ejemplo con el mes anterior y el actual; los cambios viven solo hasta recargar la página.
/// </summary>
public sealed partial class ServidorDemo
{
    private const int MaxNombre = 100;
    private const int MaxConcepto = 200;

    private readonly List<MiembroDemo> _miembros = [];
    private readonly List<PerfilDemo> _perfiles = [];
    private readonly List<CategoriaDto> _categorias = [];
    private readonly List<GastoDemo> _gastos = [];
    private readonly List<RecurrenteDemo> _recurrentes = [];
    private readonly List<AportacionCuentaDto> _aportaciones = [];
    private readonly List<ReembolsoCuentaDto> _reembolsos = [];
    private readonly List<PagoLiquidacionDto> _pagos = [];
    private readonly HogarResumen _hogar = new(IdHogar, "Casa de Ana y Marcos");
    private readonly DateOnly _hoy;
    private bool _cuentaActiva = true;

    /// <summary>Crea el servidor con el hogar de ejemplo, fechado respecto a hoy.</summary>
    /// <param name="reloj">Reloj para situar los gastos de ejemplo en el mes actual y el anterior.</param>
    public ServidorDemo(TimeProvider reloj)
    {
        _hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        Sembrar();
    }

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
            ("yo", 2, "GET") => Ok(new YoResponse(CuentaDemo.IdUsuarioLocal, [_hogar], _hogar)),
            ("hogares", 2, "POST") => Mal(HttpStatusCode.Conflict, "En el modo demo no se pueden crear hogares."),
            ("invitaciones", 3, "POST") when subruta == "aceptar" => Mal(HttpStatusCode.Conflict, "En el modo demo no se pueden aceptar invitaciones."),

            ("miembros", 2, "GET") => Ok(_miembros.Where(m => m.Activo || Consulta(uri, "incluirInactivos") == "true").OrderBy(m => m.Nombre).Select(ADto).ToList()),
            ("miembros", 2, "POST") => CrearMiembro((await Cuerpo<CrearMiembroRequest>())!),
            ("miembros", 3, "PUT") when id is { } i => ActualizarMiembro(i, (await Cuerpo<ActualizarMiembroRequest>())!),
            ("invitaciones", 2, "POST") => CrearInvitacion(await Cuerpo<CrearInvitacionRequest>()),

            ("categorias", 2, "GET") => Ok(_categorias.OrderBy(c => c.Nombre).ToList()),
            ("categorias", 2, "POST") => GuardarCategoria(null, (await Cuerpo<GuardarCategoriaRequest>())!),
            ("categorias", 3, "PUT") when id is { } i => GuardarCategoria(i, (await Cuerpo<GuardarCategoriaRequest>())!),
            ("categorias", 3, "DELETE") when id is { } i => EliminarCategoria(i),

            ("perfiles", 2, "GET") => Ok(_perfiles.Select(PerfilDto).ToList()),
            ("perfiles", 2, "POST") => GuardarPerfil(null, (await Cuerpo<GuardarPerfilRequest>())!),
            ("perfiles", 3, "PUT") when id is { } i => GuardarPerfil(i, (await Cuerpo<GuardarPerfilRequest>())!),
            ("perfiles", 3, "DELETE") when id is { } i => EliminarPerfil(i),

            ("gastos", 2, "GET") => ListarGastos(mes, Consulta(uri, "categoriaId"), Consulta(uri, "miembroId"), Consulta(uri, "buscar")),
            ("gastos", 2, "POST") => GuardarGasto(null, (await Cuerpo<GastoRequest>())!),
            ("gastos", 3, "PUT") when id is { } i => GuardarGasto(i, (await Cuerpo<GastoRequest>())!),
            ("gastos", 3, "DELETE") when id is { } i => _gastos.RemoveAll(x => x.Id == i) > 0 ? Sin() : NoEncontrado(),

            ("gastos-recurrentes", 2, "GET") => Ok(_recurrentes.OrderBy(r => r.DiaMes).ThenBy(r => r.Id).Select(RecurrenteDto).ToList()),
            ("gastos-recurrentes", 2, "POST") => GuardarRecurrente(null, (await Cuerpo<GastoRecurrenteRequest>())!),
            ("gastos-recurrentes", 3, "POST") when subruta == "generar" => Generar(mes),
            ("gastos-recurrentes", 3, "PUT") when id is { } i => GuardarRecurrente(i, (await Cuerpo<GastoRecurrenteRequest>())!),
            ("gastos-recurrentes", 3, "DELETE") when id is { } i => EliminarRecurrente(i),

            // El modo demo no registra cambios: el historial siempre sale vacío.
            ("auditoria", 2, "GET") => Ok(new List<EventoAuditoriaDto>()),

            ("cuenta-comun", 2, "GET") => EstadoCuentaComun(mes),
            ("cuenta-comun", 3, "PUT") when subruta == "activacion" => Activar((await Cuerpo<ActivarCuentaComunRequest>())!),
            ("cuenta-comun", 3, "PUT") when subruta == "aportaciones" => FijarAportacion((await Cuerpo<FijarAportacionRequest>())!),
            ("cuenta-comun", 3, "POST") when subruta == "reembolsos" => CrearReembolso((await Cuerpo<CrearReembolsoRequest>())!),
            ("cuenta-comun", 4, "DELETE") when id is { } i => _reembolsos.RemoveAll(x => x.Id == i) > 0 ? Sin() : NoEncontrado(),

            ("resumen", 2, "GET") => Resumen(mes),
            ("liquidacion", 2, "GET") => Liquidacion(mes),
            ("pagos-liquidacion", 2, "POST") => CrearPago((await Cuerpo<CrearPagoLiquidacionRequest>())!),
            ("pagos-liquidacion", 3, "DELETE") when id is { } i => _pagos.RemoveAll(x => x.Id == i) > 0 ? Sin() : NoEncontrado(),

            _ => NoEncontrado(),
        };
    }

    // ───── Miembros e invitaciones ─────

    private static MiembroDto ADto(MiembroDemo m) => new(m.Id, m.Nombre, m.Tipo, m.ResponsableId, m.Activo, m.Rol, m.Vinculado, m.EsYo);

    private HttpResponseMessage CrearMiembro(CrearMiembroRequest r)
    {
        var nombre = r.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return Mal("El nombre es obligatorio.");
        if (nombre.Length > MaxNombre) return Mal($"El nombre admite como máximo {MaxNombre} caracteres.");
        if (r.Tipo is not ("adulto" or "a_cargo")) return Mal("El tipo debe ser 'adulto' o 'a_cargo'.");

        if (r.Tipo == "a_cargo")
        {
            if (r.ResponsableId is null) return Mal("Un miembro a cargo necesita un responsable adulto.");
            if (!EsAdultoActivo(r.ResponsableId.Value)) return Mal("El responsable debe ser un adulto activo del hogar.");
        }
        else if (r.ResponsableId is not null) return Mal("Solo un miembro a cargo tiene responsable.");

        var nuevo = new MiembroDemo { Id = Guid.NewGuid(), Nombre = nombre, Tipo = r.Tipo, ResponsableId = r.ResponsableId };
        _miembros.Add(nuevo);
        return Respuesta(HttpStatusCode.Created, ADto(nuevo));
    }

    private HttpResponseMessage ActualizarMiembro(Guid id, ActualizarMiembroRequest r)
    {
        var m = _miembros.FirstOrDefault(x => x.Id == id);
        if (m is null) return Mal(HttpStatusCode.NotFound, "El miembro no existe en este hogar.");

        string? nombre = null;
        if (r.Nombre is not null)
        {
            nombre = r.Nombre.Trim();
            if (nombre.Length == 0) return Mal("El nombre no puede estar vacío.");
            if (nombre.Length > MaxNombre) return Mal($"El nombre admite como máximo {MaxNombre} caracteres.");
        }

        if (r.Rol is not null and not ("admin" or "miembro")) return Mal("El rol debe ser 'admin' o 'miembro'.");
        if (r.ResponsableId is { } responsable)
        {
            if (m.Tipo != "a_cargo") return Mal("Solo un miembro a cargo tiene responsable.");
            if (!EsAdultoActivo(responsable)) return Mal("El responsable debe ser un adulto activo del hogar.");
        }

        var activo = r.Activo ?? m.Activo;
        var rol = r.Rol ?? m.Rol;
        // El hogar siempre conserva un administrador activo y vinculado.
        if (m.Rol == "admin" && m.Activo && m.Vinculado && (!activo || rol != "admin")
            && !_miembros.Any(x => x.Id != m.Id && x.Rol == "admin" && x.Activo && x.Vinculado))
            return Mal(HttpStatusCode.Conflict, "El hogar debe conservar al menos un administrador.");
        if (m.Activo && !activo && _miembros.Any(x => x.ResponsableId == m.Id && x.Activo))
            return Mal(HttpStatusCode.Conflict, "Es responsable de miembros a cargo activos: reasígnalos antes.");

        if (nombre is not null) m.Nombre = nombre;
        m.Activo = activo;
        m.Rol = rol;
        if (r.ResponsableId is not null) m.ResponsableId = r.ResponsableId;
        return Ok(ADto(m));
    }

    private HttpResponseMessage CrearInvitacion(CrearInvitacionRequest? r)
    {
        if (r?.MiembroId is { } destino)
        {
            var m = _miembros.FirstOrDefault(x => x.Id == destino);
            if (m is null) return Mal(HttpStatusCode.NotFound, "El miembro no existe en este hogar.");
            if (!m.Activo) return Mal(HttpStatusCode.Conflict, "El miembro está desactivado.");
            if (m.Vinculado) return Mal(HttpStatusCode.Conflict, "El miembro ya está vinculado a un usuario.");
        }

        // El token solo sirve para mostrarlo: en la demo nadie puede aceptarlo.
        return Respuesta(HttpStatusCode.Created, new InvitacionCreada(Guid.NewGuid(), $"demo-{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddDays(7)));
    }

    private bool EsAdultoActivo(Guid id) => _miembros.Any(m => m.Id == id && m.Activo && m.Tipo == "adulto");

    private List<MiembroDemo> AdultosActivos() =>
        _miembros.Where(m => m.Activo && m.Tipo == "adulto").OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToList();

    // ───── Categorías ─────

    private HttpResponseMessage GuardarCategoria(Guid? id, GuardarCategoriaRequest r)
    {
        if (id is { } existente && _categorias.All(c => c.Id != existente)) return NoEncontrado();
        var nombre = r.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return Mal("El nombre es obligatorio.");
        if (nombre.Length > MaxNombre) return Mal($"El nombre admite como máximo {MaxNombre} caracteres.");
        if (r.PerfilRepartoId is { } perfil && _perfiles.All(p => p.Id != perfil)) return Mal("El perfil de reparto no existe en este hogar.");

        if (r.CategoriaPadreId is { } padre)
        {
            if (padre == id) return Mal("Una categoría no puede ser su propio padre.");
            if (_categorias.All(c => c.Id != padre)) return Mal("La categoría padre no existe en este hogar.");
            // Sin ciclos: subiendo desde el padre no debe aparecer la categoría que se edita.
            var actual = (Guid?)padre;
            var visitados = new HashSet<Guid>();
            while (id is not null && actual is { } a && visitados.Add(a))
            {
                if (a == id) return Mal("La categoría padre crearía un ciclo.");
                actual = _categorias.First(c => c.Id == a).CategoriaPadreId;
            }
        }

        if (_categorias.Any(c => c.CategoriaPadreId == r.CategoriaPadreId && c.Nombre == nombre && c.Id != id))
            return Mal(HttpStatusCode.Conflict, "Ya existe una categoría con ese nombre en ese nivel.");

        // «A cargo de la cuenta» y el perfil de cuenta común son la misma decisión (igual que en Core.Api).
        var perfilElegido = r.PerfilRepartoId is { } pe ? _perfiles.First(p => p.Id == pe) : null;
        var aCargo = r.ACargoCuentaComun || perfilElegido?.Modo == ModoReparto.CuentaComun;
        var perfilId = r.PerfilRepartoId;
        if (aCargo)
        {
            if (perfilElegido is null)
            {
                perfilId = _perfiles.FirstOrDefault(p => p.Modo == ModoReparto.CuentaComun)?.Id;
                if (perfilId is null) return Mal("El hogar no tiene un perfil de cuenta común: créalo antes de marcar la categoría.");
            }
            else if (perfilElegido.Modo != ModoReparto.CuentaComun)
            {
                return Mal("Una categoría a cargo de la cuenta común debe usar el perfil de cuenta común.");
            }

            var yaMarcada = id is { } actualId && _categorias.Any(c => c.Id == actualId && c.ACargoCuentaComun);
            if (r.ACargoCuentaComun && !yaMarcada && !_cuentaActiva) return CuentaInactiva();
        }

        var dto = new CategoriaDto(id ?? Guid.NewGuid(), nombre, r.CategoriaPadreId, perfilId, aCargo);
        if (id is null) _categorias.Add(dto);
        else _categorias[_categorias.FindIndex(c => c.Id == id)] = dto;
        return Respuesta(id is null ? HttpStatusCode.Created : HttpStatusCode.OK, dto);
    }

    private HttpResponseMessage EliminarCategoria(Guid id)
    {
        if (_categorias.All(c => c.Id != id)) return NoEncontrado();
        if (_categorias.Any(c => c.CategoriaPadreId == id)) return Mal(HttpStatusCode.Conflict, "La categoría tiene subcategorías.");
        if (_gastos.Any(g => g.CategoriaId == id) || _recurrentes.Any(r => r.CategoriaId == id))
            return Mal(HttpStatusCode.Conflict, "La categoría tiene gastos asociados.");
        _categorias.RemoveAll(c => c.Id == id);
        return Sin();
    }

    // ───── Perfiles de reparto ─────

    private static string ModoTexto(ModoReparto modo) => modo switch
    {
        ModoReparto.Porcentaje => "porcentaje",
        ModoReparto.Partes => "partes",
        ModoReparto.CuentaComun => "cuenta_comun",
        _ => "individual",
    };

    private static PerfilRepartoDto PerfilDto(PerfilDemo p) => new(p.Id, p.Nombre, ModoTexto(p.Modo), p.Detalle);

    private HttpResponseMessage GuardarPerfil(Guid? id, GuardarPerfilRequest r)
    {
        if (id is { } existente && _perfiles.All(p => p.Id != existente)) return NoEncontrado();
        var nombre = r.Nombre?.Trim();
        if (string.IsNullOrEmpty(nombre)) return Mal("El nombre es obligatorio.");
        if (nombre.Length > MaxNombre) return Mal($"El nombre admite como máximo {MaxNombre} caracteres.");
        ModoReparto? modo = r.Modo switch
        {
            "porcentaje" => ModoReparto.Porcentaje,
            "partes" => ModoReparto.Partes,
            "cuenta_comun" => ModoReparto.CuentaComun,
            "individual" => ModoReparto.Individual,
            _ => null,
        };
        if (modo is null) return Mal("Modo no válido: use porcentaje, partes, cuenta_comun o individual.");

        var detalle = r.Detalle ?? [];
        if (modo is ModoReparto.CuentaComun or ModoReparto.Individual)
        {
            if (detalle.Count > 0) return Mal($"El modo {r.Modo} no lleva detalle.");
        }
        else
        {
            if (detalle.Count == 0) return Mal("El detalle es obligatorio en este modo.");
            if (detalle.Select(d => d.MiembroId).Distinct().Count() != detalle.Count) return Mal("Hay miembros repetidos en el detalle.");
            if (detalle.Any(d => !EsAdultoActivo(d.MiembroId))) return Mal("El detalle solo puede incluir adultos activos del hogar.");
            if (detalle.Any(d => d.Valor < 0)) return Mal("Los valores no pueden ser negativos.");
            if (modo == ModoReparto.Porcentaje && Math.Abs(detalle.Sum(d => d.Valor) - 100m) > 0.01m) return Mal("Los porcentajes deben sumar 100.");
            if (modo == ModoReparto.Partes && detalle.All(d => d.Valor == 0)) return Mal("Alguna parte debe ser mayor que 0.");
        }

        if (_perfiles.Any(p => p.Nombre == nombre && p.Id != id)) return Mal(HttpStatusCode.Conflict, "Ya existe un perfil con ese nombre.");

        var perfil = id is null ? new PerfilDemo { Id = Guid.NewGuid() } : _perfiles.First(p => p.Id == id);
        perfil.Nombre = nombre;
        perfil.Modo = modo.Value;
        perfil.Detalle = detalle.ToList();
        if (id is null) _perfiles.Add(perfil);
        return Respuesta(id is null ? HttpStatusCode.Created : HttpStatusCode.OK, PerfilDto(perfil));
    }

    private HttpResponseMessage EliminarPerfil(Guid id)
    {
        if (_perfiles.All(p => p.Id != id)) return NoEncontrado();
        if (_categorias.Any(c => c.PerfilRepartoId == id) || _gastos.Any(g => g.PerfilRepartoId == id) || _recurrentes.Any(r => r.PerfilRepartoId == id))
            return Mal(HttpStatusCode.Conflict, "El perfil está en uso por categorías, gastos o gastos recurrentes.");
        _perfiles.RemoveAll(p => p.Id == id);
        return Sin();
    }

    // ───── Gastos ─────

    private static GastoResponse GastoDto(GastoDemo g) => new(
        g.Id, g.Fecha, g.Importe, g.CategoriaId, g.PagadoPor, g.PerfilRepartoId, g.Concepto, g.GastoRecurrenteId,
        g.Repartos.OrderBy(r => r.MiembroId).ToList(), g.ACargoCuentaComun, false, g.EsPersonal);

    private HttpResponseMessage ListarGastos(string? mes, string? categoriaId, string? miembroId, string? buscar)
    {
        var lista = _gastos.AsEnumerable();
        if (Guid.TryParse(categoriaId, out var cat)) lista = lista.Where(g => g.CategoriaId == cat);
        if (Guid.TryParse(miembroId, out var mie)) lista = lista.Where(g => g.PagadoPor == mie || g.Repartos.Any(r => r.MiembroId == mie && r.ImporteAsumido > 0m));
        if (!string.IsNullOrWhiteSpace(buscar))
            lista = lista.Where(g => g.Concepto?.Contains(buscar.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        if (mes is not null)
        {
            if (!TryMes(mes, out var inicio)) return MesInvalido();
            var fin = inicio.AddMonths(1);
            lista = lista.Where(g => g.Fecha >= inicio && g.Fecha < fin);
        }

        return Ok(lista.OrderByDescending(g => g.Fecha).ThenBy(g => g.Id).Select(GastoDto).ToList());
    }

    /// <summary>Valida un gasto y calcula su reparto; devuelve el error (o null) y si lo asume la cuenta común.</summary>
    private string? PrepararGasto(GastoRequest r, out List<RepartoGastoDto> repartos, out bool cuentaComun)
    {
        repartos = [];
        cuentaComun = false;
        var error = ValidarImporte(r.Importe) ?? ValidarConcepto(r.Concepto);
        if (error is not null) return error;
        if (r.Fecha == default) return "La fecha es obligatoria.";
        if (_categorias.All(c => c.Id != r.CategoriaId)) return "La categoría no existe en el hogar.";
        if (r.PagadoPor is { } pagador && !EsAdultoActivo(pagador)) return "Quien paga debe ser un adulto activo del hogar.";
        if (r.Personal && (r.PagadoPor is null || r.PagadoDesdeAhorro)) return "Un gasto personal lo paga un miembro del hogar, no la cuenta común ni el ahorro.";
        var perfil = _perfiles.FirstOrDefault(p => p.Id == r.PerfilRepartoId);
        if (perfil is null) return "El perfil de reparto no existe en el hogar.";
        if (r.PagadoPor is null && perfil.Modo != ModoReparto.CuentaComun) return "La cuenta común solo paga gastos con el perfil de cuenta común.";
        if (r.Personal)
        {
            repartos = [new RepartoGastoDto(r.PagadoPor!.Value, r.Importe)]; // lo asume íntegro quien paga, sea cual sea el perfil
            return null;
        }

        cuentaComun = perfil.Modo == ModoReparto.CuentaComun;
        return Repartir(perfil, r.Importe, r.PagadoPor ?? Guid.Empty, out repartos);
    }

    /// <summary>Reparte un importe entre los adultos activos según el perfil (misma regla que <c>ApiComun.Repartir</c> de Core.Api).</summary>
    private string? Repartir(PerfilDemo perfil, decimal importe, Guid pagadoPor, out List<RepartoGastoDto> repartos)
    {
        repartos = [];
        if (perfil.Modo == ModoReparto.CuentaComun) return null; // lo asume la cuenta común: sin reparto entre personas
        var adultos = AdultosActivos();
        if (adultos.Count == 0) return "El hogar no tiene adultos activos entre los que repartir.";

        var miembros = adultos.Select(a => new MiembroReparto(a.Id, perfil.Modo is ModoReparto.Porcentaje or ModoReparto.Partes
            ? perfil.Detalle.Where(d => d.MiembroId == a.Id).Sum(d => d.Valor) : 0m)).ToList();
        if (perfil.Modo is ModoReparto.Porcentaje or ModoReparto.Partes && adultos.Count > 1 && miembros.All(m => m.Valor <= 0))
            return "El perfil de reparto no asigna valor a ningún adulto activo.";

        try
        {
            repartos = RepartoMiembros.Repartir(importe, perfil.Modo, miembros, pagadoPor)
                .Select(p => new RepartoGastoDto(p.MiembroId, p.Importe)).ToList();
            return null;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }

    private HttpResponseMessage GuardarGasto(Guid? id, GastoRequest r)
    {
        var existente = id is null ? null : _gastos.FirstOrDefault(g => g.Id == id);
        if (id is not null && existente is null) return NoEncontrado();
        if (PrepararGasto(r, out var repartos, out var cuentaComun) is { } error) return Mal(error);
        if (cuentaComun && !_cuentaActiva) return CuentaInactiva();

        var g = existente ?? new GastoDemo { Id = Guid.NewGuid() };
        g.Fecha = r.Fecha;
        g.Importe = r.Importe;
        g.CategoriaId = r.CategoriaId;
        g.PagadoPor = r.PagadoPor;
        g.PerfilRepartoId = r.PerfilRepartoId;
        g.Concepto = NormalizarConcepto(r.Concepto);
        g.ACargoCuentaComun = cuentaComun;
        g.EsPersonal = r.Personal;
        g.Repartos = repartos;
        if (existente is null) _gastos.Add(g);
        return Respuesta(existente is null ? HttpStatusCode.Created : HttpStatusCode.OK, GastoDto(g));
    }

    // ───── Recurrentes ─────

    private static GastoRecurrenteResponse RecurrenteDto(RecurrenteDemo r) =>
        new(r.Id, r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.DiaMes, r.Concepto, r.Activo);

    private string? ValidarRecurrente(GastoRecurrenteRequest r)
    {
        var error = ValidarImporte(r.Importe) ?? ValidarConcepto(r.Concepto);
        if (error is not null) return error;
        if (r.DiaMes is < 1 or > 28) return "El día del mes debe estar entre 1 y 28.";
        if (_categorias.All(c => c.Id != r.CategoriaId)) return "La categoría no existe en el hogar.";
        if (!EsAdultoActivo(r.PagadoPor)) return "Quien paga debe ser un adulto activo del hogar.";
        if (_perfiles.All(p => p.Id != r.PerfilRepartoId)) return "El perfil de reparto no existe en el hogar.";
        return null;
    }

    private HttpResponseMessage GuardarRecurrente(Guid? id, GastoRecurrenteRequest r)
    {
        var existente = id is null ? null : _recurrentes.FirstOrDefault(x => x.Id == id);
        if (id is not null && existente is null) return NoEncontrado();
        if (ValidarRecurrente(r) is { } error) return Mal(error);

        // Los gastos ya generados no se tocan: la plantilla solo afecta a los futuros.
        var p = existente ?? new RecurrenteDemo { Id = Guid.NewGuid() };
        p.Importe = r.Importe;
        p.CategoriaId = r.CategoriaId;
        p.PagadoPor = r.PagadoPor;
        p.PerfilRepartoId = r.PerfilRepartoId;
        p.DiaMes = r.DiaMes;
        p.Concepto = NormalizarConcepto(r.Concepto);
        p.Activo = r.Activo;
        if (existente is null) _recurrentes.Add(p);
        return Respuesta(existente is null ? HttpStatusCode.Created : HttpStatusCode.OK, RecurrenteDto(p));
    }

    private HttpResponseMessage EliminarRecurrente(Guid id)
    {
        if (_recurrentes.All(r => r.Id != id)) return NoEncontrado();
        if (_gastos.Any(g => g.GastoRecurrenteId == id)) return Mal(HttpStatusCode.Conflict, "Ya tiene gastos generados: desactívala en lugar de borrarla.");
        _recurrentes.RemoveAll(r => r.Id == id);
        return Sin();
    }

    private HttpResponseMessage Generar(string? mes)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        var fin = inicio.AddMonths(1);
        var plantillas = _recurrentes.Where(r => r.Activo).OrderBy(r => r.DiaMes).ThenBy(r => r.Id).ToList();
        var yaGenerados = _gastos.Where(g => g.GastoRecurrenteId is not null && g.Fecha >= inicio && g.Fecha < fin)
            .Select(g => g.GastoRecurrenteId!.Value).ToHashSet();
        var pendientes = plantillas.Where(p => !yaGenerados.Contains(p.Id)).ToList();

        // O se generan todos o ninguno.
        var nuevos = new List<GastoDemo>();
        foreach (var p in pendientes)
        {
            var perfil = _perfiles.First(x => x.Id == p.PerfilRepartoId);
            if (Repartir(perfil, p.Importe, p.PagadoPor, out var repartos) is { } error)
                return Mal($"No se puede repartir el gasto recurrente '{p.Concepto ?? p.Id.ToString()}': {error}");
            nuevos.Add(new GastoDemo
            {
                Id = Guid.NewGuid(), Fecha = new DateOnly(inicio.Year, inicio.Month, p.DiaMes), Importe = p.Importe,
                CategoriaId = p.CategoriaId, PagadoPor = p.PagadoPor, PerfilRepartoId = p.PerfilRepartoId, Concepto = p.Concepto,
                GastoRecurrenteId = p.Id, Repartos = repartos, ACargoCuentaComun = perfil.Modo == ModoReparto.CuentaComun,
            });
        }

        _gastos.AddRange(nuevos);
        return Ok(new GenerarRecurrentesResponse(FormatoMes(inicio), nuevos.Count, plantillas.Count - pendientes.Count));
    }

    // ───── Cuenta común ─────

    private List<GastoDeCuenta> GastosDeCuenta() =>
        _gastos.Where(g => g.ACargoCuentaComun).Select(g => new GastoDeCuenta(g.PagadoPor, g.Fecha, g.Importe)).ToList();

    private HttpResponseMessage EstadoCuentaComun(string? mes)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        var fin = inicio.AddMonths(1);
        var e = CuentaComun.Calcular(
            inicio, _aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe)).ToList(),
            GastosDeCuenta(), _reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe)));

        var vigentes = _aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe)).ToList();
        var partes = CuentaComun.PartesPorPersona(inicio, vigentes, e);

        return Ok(new CuentaComunResponse(
            FormatoMes(inicio), e.AportadoMes, e.Aportado, e.Gastado, e.Saldo,
            e.Pendientes.Select(p => new PendienteCuentaDto(p.MiembroId, Nombre(p.MiembroId), p.Importe)).ToList(), e.Efectivo,
            _aportaciones.OrderBy(a => a.MiembroId).ThenBy(a => a.Desde).ToList(),
            _reembolsos.Where(r => r.Fecha >= inicio && r.Fecha < fin).OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToList(),
            Activa: _cuentaActiva,
            Partes: partes.Select(p => new PartePersonaDto(
                p.MiembroId, Nombre(p.MiembroId), p.Aportado, p.Ahorrado, p.PorcentajeGastos, p.PorcentajeAhorro,
                p.ParteSaldo, p.ParteAhorro, p.Pendiente)).ToList()));
    }

    private HttpResponseMessage CuentaInactiva() =>
        Mal(HttpStatusCode.Conflict, "La cuenta común no está activada en este hogar. Un administrador puede activarla en la pestaña Cuenta común.");

    private HttpResponseMessage Activar(ActivarCuentaComunRequest r)
    {
        _cuentaActiva = r.Activa;
        return Ok(new { activa = _cuentaActiva });
    }

    private HttpResponseMessage FijarAportacion(FijarAportacionRequest r)
    {
        if (!_cuentaActiva) return CuentaInactiva();
        if (r.Desde.Day != 1) return Mal("El mes de la aportación debe ser el día 1 del mes.");
        // Una aportación de 0 es válida: deja de aportar desde ese mes.
        var error = r.Importe < 0 ? "El importe no puede ser negativo." : r.Importe == 0 ? null : ValidarImporte(r.Importe);
        if (error is not null) return Mal(error);
        if (!EsAdultoActivo(r.MiembroId)) return Mal("Solo los adultos activos del hogar aportan a la cuenta común.");

        var i = _aportaciones.FindIndex(a => a.MiembroId == r.MiembroId && a.Desde == r.Desde);
        var dto = new AportacionCuentaDto(i >= 0 ? _aportaciones[i].Id : Guid.NewGuid(), r.MiembroId, r.Desde, r.Importe);
        if (i >= 0) _aportaciones[i] = dto;
        else _aportaciones.Add(dto);
        return Ok(dto);
    }

    private HttpResponseMessage CrearReembolso(CrearReembolsoRequest r)
    {
        if (!_cuentaActiva) return CuentaInactiva();
        var error = ValidarImporte(r.Importe) ?? ValidarConcepto(r.Concepto);
        if (error is not null) return Mal(error);
        if (_miembros.All(m => m.Id != r.MiembroId)) return Mal("El miembro del reembolso debe pertenecer al hogar.");

        var pendiente = _gastos.Where(g => g.ACargoCuentaComun && g.PagadoPor == r.MiembroId).Sum(g => g.Importe)
                        - _reembolsos.Where(x => x.MiembroId == r.MiembroId).Sum(x => x.Importe);
        if (r.Importe > pendiente)
            return Mal(HttpStatusCode.Conflict, $"El importe supera lo pendiente de reembolsar al miembro ({Math.Max(0m, pendiente):0.00}).");

        var dto = new ReembolsoCuentaDto(Guid.NewGuid(), r.MiembroId, r.Fecha ?? _hoy, r.Importe, NormalizarConcepto(r.Concepto));
        _reembolsos.Add(dto);
        return Respuesta(HttpStatusCode.Created, dto);
    }

    // ───── Resumen y liquidación ─────

    private List<GastoDemo> GastosDelMes(DateOnly inicio) =>
        _gastos.Where(g => g.Fecha >= inicio && g.Fecha < inicio.AddMonths(1)).ToList();

    private string Nombre(Guid id) => _miembros.FirstOrDefault(m => m.Id == id)?.Nombre ?? "";

    private HttpResponseMessage Resumen(string? mes)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        var delMes = GastosDelMes(inicio);
        var gastos = delMes.Where(g => !g.EsPersonal).ToList();
        var personales = delMes.Where(g => g.EsPersonal).ToList();
        var cuenta = CuentaComun.Calcular(inicio, [], GastosDeCuenta(), _reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe)));

        // Los acreedores con pendiente de meses anteriores aparecen aunque no participen en los gastos de este mes.
        var implicados = AdultosActivos().Select(m => m.Id)
            .Concat(gastos.Where(g => g.PagadoPor is not null).Select(g => g.PagadoPor!.Value))
            .Concat(gastos.SelectMany(g => g.Repartos).Select(r => r.MiembroId))
            .Concat(cuenta.Pendientes.Where(p => p.Importe > 0m).Select(p => p.MiembroId))
            .Concat(personales.Select(g => g.PagadoPor!.Value))
            .ToHashSet();
        var porMiembro = _miembros.Where(m => implicados.Contains(m.Id)).OrderBy(m => m.Nombre).ThenBy(m => m.Id)
            .Select(m => new ResumenMiembroDto(
                m.Id, m.Nombre,
                gastos.Where(g => g.PagadoPor == m.Id).Sum(g => g.Importe),
                gastos.SelectMany(g => g.Repartos).Where(r => r.MiembroId == m.Id).Sum(r => r.ImporteAsumido),
                Math.Max(0m, cuenta.Pendientes.FirstOrDefault(p => p.MiembroId == m.Id)?.Importe ?? 0m),
                personales.Where(g => g.PagadoPor == m.Id).Sum(g => g.Importe)))
            .ToList();

        var porCategoria = gastos.GroupBy(g => g.CategoriaId)
            .Select(c => new ResumenCategoriaDto(
                c.Key, _categorias.FirstOrDefault(x => x.Id == c.Key)?.Nombre ?? "", c.Sum(g => g.Importe),
                c.SelectMany(g => g.Repartos).GroupBy(r => r.MiembroId)
                    .Select(r => new ImporteMiembroDto(r.Key, r.Sum(x => x.ImporteAsumido))).OrderBy(x => x.MiembroId).ToList()))
            .OrderBy(c => c.Nombre).ThenBy(c => c.CategoriaId).ToList();

        ResumenCuentaComunDto? resumenCuenta = null;
        if (_cuentaActiva)
        {
            var estado = CuentaComun.Calcular(
                inicio, _aportaciones.Select(a => new AportacionVigente(a.MiembroId, a.Desde, a.Importe)).ToList(),
                GastosDeCuenta(), _reembolsos.Select(r => new ReembolsoDeCuenta(r.MiembroId, r.Fecha, r.Importe)));
            resumenCuenta = new ResumenCuentaComunDto(
                estado.AportadoMes, gastos.Where(g => g.ACargoCuentaComun).Sum(g => g.Importe), estado.Saldo, estado.Efectivo,
                estado.Pendientes.Where(p => p.Importe > 0m).Sum(p => p.Importe), estado.AhorroMes, estado.AhorroDisponible);
        }

        return Ok(new ResumenMensualResponse(FormatoMes(inicio), gastos.Sum(g => g.Importe), porMiembro, porCategoria, resumenCuenta, personales.Sum(g => g.Importe)));
    }

    private (IReadOnlyList<SaldoMiembro> Saldos, IReadOnlyList<Transferencia> Transferencias, List<PagoLiquidacionDto> Pagos, bool HayGastos)
        CalcularLiquidacion(DateOnly inicio)
    {
        // Lo que asume la cuenta común no entra en la deuda entre personas.
        var gastos = GastosDelMes(inicio).Where(g => !g.ACargoCuentaComun && !g.EsPersonal).ToList();
        var pagos = _pagos.Where(p => p.Mes == inicio).OrderBy(p => p.Fecha).ThenBy(p => p.Id).ToList();
        var ids = AdultosActivos().Select(m => m.Id)
            .Concat(gastos.Select(g => g.PagadoPor!.Value)).Concat(gastos.SelectMany(g => g.Repartos).Select(r => r.MiembroId))
            .Concat(pagos.SelectMany(p => new[] { p.DeMiembroId, p.AMiembroId }))
            .Distinct().OrderBy(x => x).ToList();

        var calculados = gastos.Select(g => new GastoCalculado(
            g.PagadoPor!.Value, g.Importe, g.Repartos.Select(r => new ParteAsumida(r.MiembroId, r.ImporteAsumido)).ToList()));
        var saldos = Core.Domain.Liquidacion.CalcularSaldos(ids, calculados, pagos.Select(p => new PagoLiquidacion(p.DeMiembroId, p.AMiembroId, p.Importe)));
        return (saldos, Core.Domain.Liquidacion.Liquidar(saldos), pagos, gastos.Count > 0);
    }

    private HttpResponseMessage Liquidacion(string? mes)
    {
        if (!TryMes(mes, out var inicio)) return MesInvalido();
        var c = CalcularLiquidacion(inicio);
        return Ok(new LiquidacionResponse(
            FormatoMes(inicio), c.Saldos.Select(s => new SaldoMiembroDto(s.MiembroId, Nombre(s.MiembroId), s.Importe)).ToList(),
            c.Transferencias.Select(t => new TransferenciaDto(t.De, t.A, t.Importe)).ToList(), c.Pagos));
    }

    private HttpResponseMessage CrearPago(CrearPagoLiquidacionRequest r)
    {
        if (r.Mes.Day != 1) return Mal("El mes del pago debe ser el día 1 del mes.");
        if (r.DeMiembroId == r.AMiembroId) return Mal("Quien paga y quien recibe deben ser distintos.");
        var error = ValidarImporte(r.Importe) ?? ValidarConcepto(r.Concepto);
        if (error is not null) return Mal(error);
        if (_miembros.Count(m => m.Id == r.DeMiembroId || m.Id == r.AMiembroId) != 2) return Mal("Los miembros del pago deben pertenecer al hogar.");

        // Lo máximo que puede pagar «de» sin pasarse de su deuda ni de lo que se le debe a «a».
        var c = CalcularLiquidacion(r.Mes);
        if (c.HayGastos)
        {
            var deuda = -(c.Saldos.FirstOrDefault(s => s.MiembroId == r.DeMiembroId)?.Importe ?? 0m);
            var credito = c.Saldos.FirstOrDefault(s => s.MiembroId == r.AMiembroId)?.Importe ?? 0m;
            var pendiente = Math.Max(0m, Math.Min(deuda, credito));
            if (r.Importe > pendiente) return Mal(HttpStatusCode.Conflict, $"El importe supera la deuda pendiente entre ambos miembros ({pendiente:0.00}).");
        }

        var dto = new PagoLiquidacionDto(Guid.NewGuid(), r.Mes, r.DeMiembroId, r.AMiembroId, r.Importe, r.Fecha ?? _hoy, NormalizarConcepto(r.Concepto));
        _pagos.Add(dto);
        return Respuesta(HttpStatusCode.Created, dto);
    }

    // ───── Utilidades ─────

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex PatronMes();

    private static bool TryMes(string? mes, out DateOnly inicio)
    {
        inicio = default;
        if (mes is null || !PatronMes().IsMatch(mes) || int.Parse(mes[..4]) < 1) return false;
        inicio = new DateOnly(int.Parse(mes[..4]), int.Parse(mes[5..]), 1);
        return true;
    }

    private static string FormatoMes(DateOnly inicio) => $"{inicio.Year:0000}-{inicio.Month:00}";

    private static string? ValidarImporte(decimal importe)
    {
        if (importe <= 0) return "El importe debe ser mayor que cero.";
        if (importe > 9_999_999_999.99m) return "El importe es demasiado grande.";
        if (decimal.Round(importe, 2) != importe) return "El importe admite como máximo 2 decimales.";
        return null;
    }

    private static string? ValidarConcepto(string? concepto) =>
        concepto is { Length: > MaxConcepto } ? $"El concepto admite como máximo {MaxConcepto} caracteres." : null;

    private static string? NormalizarConcepto(string? concepto) => string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim();

    private static string? Consulta(Uri uri, string clave)
    {
        foreach (var par in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            if (i > 0 && par[..i] == clave) return Uri.UnescapeDataString(par[(i + 1)..]);
        }

        return null;
    }

    private static HttpResponseMessage Respuesta(HttpStatusCode estado, object? cuerpo) =>
        new(estado) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo, cuerpo.GetType()) };

    private static HttpResponseMessage Ok(object cuerpo) => Respuesta(HttpStatusCode.OK, cuerpo);

    private static HttpResponseMessage Sin() => Respuesta(HttpStatusCode.NoContent, null);

    private static HttpResponseMessage NoEncontrado() => Respuesta(HttpStatusCode.NotFound, null);

    private static HttpResponseMessage Mal(string mensaje) => Mal(HttpStatusCode.BadRequest, mensaje);

    private static HttpResponseMessage Mal(HttpStatusCode estado, string mensaje) => Respuesta(estado, new { error = mensaje });

    private static HttpResponseMessage MesInvalido() => Mal("El mes debe tener el formato YYYY-MM.");

    // ───── Estado en memoria ─────

    private sealed class MiembroDemo
    {
        public Guid Id { get; init; }
        public string Nombre { get; set; } = "";
        public string Tipo { get; init; } = "adulto";
        public Guid? ResponsableId { get; set; }
        public bool Activo { get; set; } = true;
        public string Rol { get; set; } = "miembro";
        public bool Vinculado { get; init; }
        public bool EsYo { get; init; }
    }

    private sealed class PerfilDemo
    {
        public Guid Id { get; init; }
        public string Nombre { get; set; } = "";
        public ModoReparto Modo { get; set; }
        public List<PerfilDetalleDto> Detalle { get; set; } = [];
    }

    private sealed class GastoDemo
    {
        public Guid Id { get; init; }
        public DateOnly Fecha { get; set; }
        public decimal Importe { get; set; }
        public Guid CategoriaId { get; set; }
        public Guid? PagadoPor { get; set; }
        public Guid PerfilRepartoId { get; set; }
        public string? Concepto { get; set; }
        public Guid? GastoRecurrenteId { get; set; }
        public List<RepartoGastoDto> Repartos { get; set; } = [];
        public bool ACargoCuentaComun { get; set; }
        public bool EsPersonal { get; set; }
    }

    private sealed class RecurrenteDemo
    {
        public Guid Id { get; init; }
        public decimal Importe { get; set; }
        public Guid CategoriaId { get; set; }
        public Guid PagadoPor { get; set; }
        public Guid PerfilRepartoId { get; set; }
        public short DiaMes { get; set; }
        public string? Concepto { get; set; }
        public bool Activo { get; set; }
    }
}
