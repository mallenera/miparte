using System.Globalization;
using MiParte.Contracts;
using MiParte.Web.Api;
using MiParte.Web.Reparto;

namespace MiParte.Web.Modelos;

/// <summary>
/// Estado y orquestación de la pestaña Cuenta común: saldo del mes, su parte por persona, aportaciones, reembolsos y movimientos del
/// ahorro. No depende de Blazor; <c>VistaCuentaComun</c> solo enlaza sus campos y pinta.
/// </summary>
/// <param name="api">Cliente de Core.Api.</param>
/// <param name="hoy">Fecha de hoy, que fija el mes con el que se abre la pestaña.</param>
public sealed class ModeloCuentaComun(CoreApiClient api, DateOnly hoy)
{
    private static readonly CultureInfo Es = new("es-ES");
    private DateOnly _mes = new(hoy.Year, hoy.Month, 1);
    private int _version;

    // ───── Datos ─────

    /// <summary>Estado de la cuenta al final del mes mostrado; null hasta cargarlo.</summary>
    public CuentaComunResponse? Estado { get; private set; }

    /// <summary>Todos los miembros.</summary>
    public List<MiembroDto> Miembros { get; private set; } = [];

    /// <summary>Adultos activos, los únicos que aportan, ingresan y retiran.</summary>
    public List<MiembroDto> Adultos { get; private set; } = [];

    /// <summary>Todos los ingresos a la cuenta común del hogar, del más reciente al más antiguo.</summary>
    public List<IngresoCuentaComunDto> Ingresos { get; private set; } = [];

    /// <summary>Ingresos del mes mostrado.</summary>
    public List<IngresoCuentaComunDto> IngresosDelMes => Ingresos
        .Where(i => i.Fecha.Year == _mes.Year && i.Fecha.Month == _mes.Month).ToList();

    /// <summary>Total ingresado en el mes mostrado.</summary>
    public decimal TotalIngresosDelMes => IngresosDelMes.Sum(i => i.Importe);

    // ───── Estado de pantalla ─────

    /// <summary>Si se están cargando los datos.</summary>
    public bool Cargando { get; private set; } = true;

    /// <summary>Si hay una operación en curso (se ignoran las demás: un doble clic crearía el registro dos veces).</summary>
    public bool Enviando { get; private set; }

    /// <summary>Error de la carga inicial.</summary>
    public string? ErrorCarga { get; private set; }

    /// <summary>Error de la última operación.</summary>
    public string? Error { get; private set; }

    /// <summary>Movimiento cuya eliminación se está confirmando.</summary>
    public Guid? ConfirmandoEliminar { get; set; }

    /// <summary>Mes mostrado (primer día).</summary>
    public DateOnly Mes => _mes;

    /// <summary>Nombre del mes mostrado.</summary>
    public string NombreMes => _mes.ToString("MMMM yyyy", Es);

    // ───── Formularios ─────

    /// <summary>Adulto cuya aportación se fija.</summary>
    public Guid Aportante { get; set; }

    /// <summary>Miembro al que se reembolsa.</summary>
    public Guid Reembolsado { get; set; }

    /// <summary>Adulto que retira del ahorro.</summary>
    public Guid Retirante { get; set; }

    /// <summary>Adulto que ingresa al ahorro.</summary>
    public Guid Depositante { get; set; }

    /// <summary>Importe mensual de la aportación, tal como se escribe.</summary>
    public string ImporteAportacion { get; set; } = "";

    /// <summary>Parte de la aportación que va a ahorro, tal como se escribe.</summary>
    public string AhorroAportacion { get; set; } = "";

    /// <summary>Importe del reembolso.</summary>
    public string ImporteReembolso { get; set; } = "";

    /// <summary>Concepto del reembolso.</summary>
    public string ConceptoReembolso { get; set; } = "";

    /// <summary>Importe de la retirada.</summary>
    public string ImporteRetirada { get; set; } = "";

    /// <summary>Concepto de la retirada.</summary>
    public string ConceptoRetirada { get; set; } = "";

    /// <summary>Miembro que hace el ingreso a la cuenta común.</summary>
    public Guid Ingresante { get; set; }

    /// <summary>Importe del ingreso a la cuenta común, tal como se escribe.</summary>
    public string ImporteIngreso { get; set; } = "";

    /// <summary>Concepto del ingreso a la cuenta común (de dónde viene).</summary>
    public string ConceptoIngreso { get; set; } = "";

    /// <summary>Importe del ingreso al ahorro.</summary>
    public string ImporteDeposito { get; set; } = "";

    /// <summary>Concepto del ingreso al ahorro.</summary>
    public string ConceptoDeposito { get; set; } = "";

    /// <summary>Fecha a la que corresponde el saldo inicial de la cuenta.</summary>
    public DateOnly? FechaSaldoInicial { get; set; }

    /// <summary>Importe del saldo inicial, tal como se escribe.</summary>
    public string ImporteSaldoInicial { get; set; } = "";

    /// <summary>La aportación es válida: importe de 0 o más y ahorro entre 0 y el importe (sin ahorro activo el campo no cuenta).</summary>
    public bool AportacionValida => EntradaImporte.TryParse(ImporteAportacion, permitirCero: true, out var importe)
        && EntradaImporte.TryParse(
            string.IsNullOrWhiteSpace(AhorroAportacion) || Estado?.AhorroActivo == false ? "0" : AhorroAportacion,
            permitirCero: true, out var ahorro)
        && ahorro <= importe;

    /// <summary>El importe del ingreso a la cuenta común es válido.</summary>
    public bool IngresoValido => EntradaImporte.TryParse(ImporteIngreso, out _);

    /// <summary>El importe del ingreso es válido.</summary>
    public bool DepositoValido => EntradaImporte.TryParse(ImporteDeposito, out _);

    /// <summary>El importe de la retirada es válido.</summary>
    public bool RetiradaValida => EntradaImporte.TryParse(ImporteRetirada, out _);

    /// <summary>El importe del reembolso es válido.</summary>
    public bool ReembolsoValido => EntradaImporte.TryParse(ImporteReembolso, out _);

    /// <summary>El saldo inicial tiene fecha y un importe de 0 o más.</summary>
    public bool SaldoInicialValido => FechaSaldoInicial is not null && EntradaImporte.TryParse(ImporteSaldoInicial, permitirCero: true, out _);

    // ───── Lecturas para pintar ─────

    /// <summary>Texto de un porcentaje con hasta dos decimales.</summary>
    public static string Porcentaje(decimal p) => $"{p.ToString("0.##", Es)} %";

    /// <summary>Aportación de un adulto en el mes mostrado: la de la fila más reciente que no pase de ese mes.</summary>
    public decimal VigenteDe(Guid miembro) =>
        Estado?.Aportaciones.Where(a => a.MiembroId == miembro && a.Desde <= _mes)
            .OrderByDescending(a => a.Desde).Select(a => a.Importe).FirstOrDefault() ?? 0m;

    /// <summary>Parte de ahorro de la aportación de un adulto en el mes mostrado.</summary>
    public decimal AhorroVigenteDe(Guid miembro) =>
        Estado?.Aportaciones.Where(a => a.MiembroId == miembro && a.Desde <= _mes)
            .OrderByDescending(a => a.Desde).Select(a => a.Ahorro).FirstOrDefault() ?? 0m;

    // ───── Operaciones ─────

    /// <summary>Marca el modelo como «cargando» antes de pedir los permisos y los datos.</summary>
    public void IniciarCarga()
    {
        Cargando = true;
        ErrorCarga = null;
    }

    /// <summary>Carga miembros y estado de la cuenta; si falla deja el mensaje en <see cref="ErrorCarga"/>.</summary>
    public async Task CargarAsync()
    {
        try
        {
            Miembros = await api.ListarMiembrosAsync();
            Adultos = Miembros.Where(m => m.Tipo == TiposMiembro.Adulto && m.Activo).ToList();
            Aportante = Retirante = Depositante = (Adultos.FirstOrDefault(m => m.EsYo) ?? Adultos.FirstOrDefault())?.Id ?? Guid.Empty;
            Ingresante = (Miembros.FirstOrDefault(m => m.EsYo) ?? Miembros.FirstOrDefault())?.Id ?? Guid.Empty;
            await RecargarAsync();
        }
        catch (ApiException e)
        {
            ErrorCarga = e.Message;
        }

        Cargando = false;
    }

    private async Task RecargarAsync()
    {
        // Una respuesta tardía de otro mes no pisa la actual (ni los campos del formulario que se rellenan desde ella).
        var version = ++_version;
        CuentaComunResponse estado;
        try
        {
            estado = await api.ObtenerCuentaComunAsync(Formato.Mes(_mes));
        }
        catch (ApiException) when (version != _version)
        {
            return; // fallo de una petición ya superada
        }

        if (version != _version) return;
        Estado = estado;
        if (Estado.Pendientes.All(p => p.MiembroId != Reembolsado))
            Reembolsado = Estado.Pendientes.FirstOrDefault()?.MiembroId ?? Guid.Empty;
        AlCambiarAportante();
        AlCambiarSaldoInicial();

        // Un fallo de los ingresos no tumba la cuenta: solo deja su error.
        try
        {
            var ingresos = await api.ListarIngresosCuentaComunAsync();
            if (version == _version) Ingresos = ingresos;
        }
        catch (ApiException e) when (version == _version)
        {
            Error = e.Message;
        }
        catch (ApiException)
        {
            // fallo de una petición ya superada
        }
    }

    /// <summary>Al recargar, el formulario del saldo inicial muestra el que hay (o el mes mostrado si todavía no hay ninguno).</summary>
    private void AlCambiarSaldoInicial()
    {
        FechaSaldoInicial = Estado?.SaldoInicial?.Fecha ?? _mes;
        ImporteSaldoInicial = Estado?.SaldoInicial?.Importe.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>Pasa al mes anterior (-1) o siguiente (1) y recarga.</summary>
    public async Task CambiarMesAsync(int delta)
    {
        _mes = _mes.AddMonths(delta);
        Error = null;
        ConfirmandoEliminar = null;
        try
        {
            await RecargarAsync();
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Al elegir adulto (o cambiar de mes) los campos muestran su aportación y ahorro vigentes.</summary>
    public void AlCambiarAportante()
    {
        ImporteAportacion = VigenteDe(Aportante).ToString(CultureInfo.InvariantCulture);
        AhorroAportacion = AhorroVigenteDe(Aportante).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Fija la aportación del adulto elegido desde el mes mostrado.</summary>
    public Task<string?> FijarAportacionAsync() =>
        EjecutarAsync(async () =>
        {
            if (!EntradaImporte.TryParse(ImporteAportacion, permitirCero: true, out var importe))
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, "Introduce un importe de 0 o más con máximo 2 decimales.");
            var ahorro = 0m;
            // Sin ahorro activado el campo no se muestra y la aportación queda entera para gastos.
            if (Estado!.AhorroActivo && !string.IsNullOrWhiteSpace(AhorroAportacion)
                && (!EntradaImporte.TryParse(AhorroAportacion, permitirCero: true, out ahorro) || ahorro > importe))
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, "El ahorro debe estar entre 0 y la aportación, con máximo 2 decimales.");
            await api.FijarAportacionAsync(new FijarAportacionRequest(Aportante, _mes, importe, ahorro));
            return "Aportación guardada";
        });

    /// <summary>Registra un reembolso de la cuenta a quien adelantó gastos.</summary>
    public Task<string?> ReembolsarAsync() =>
        EjecutarAsync(async () =>
        {
            var importe = Importe(ImporteReembolso);
            await api.CrearReembolsoAsync(new CrearReembolsoRequest(Reembolsado, importe, null, Texto(ConceptoReembolso)));
            ImporteReembolso = "";
            ConceptoReembolso = "";
            return "Reembolso registrado";
        });

    /// <summary>
    /// Registra un ingreso a la cuenta común (p. ej. lo que pagan las amigas de una cena). Suma al saldo y al efectivo de la cuenta,
    /// no a los reembolsos. Si el mes mostrado no es el actual, el ingreso se fecha el primer día de ese mes.
    /// </summary>
    public Task<string?> IngresarAsync() =>
        EjecutarAsync(async () =>
        {
            var importe = Importe(ImporteIngreso);
            var fecha = _mes == new DateOnly(hoy.Year, hoy.Month, 1) ? (DateOnly?)null : _mes;
            await api.CrearIngresoCuentaComunAsync(new CrearIngresoCuentaComunRequest(Ingresante, importe, fecha, Texto(ConceptoIngreso)));
            ImporteIngreso = "";
            ConceptoIngreso = "";
            return "Ingreso registrado";
        });

    /// <summary>Elimina un ingreso a la cuenta común.</summary>
    public Task<string?> EliminarIngresoAsync(IngresoCuentaComunDto i) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarIngresoCuentaComunAsync(i.Id);
            ConfirmandoEliminar = null;
            return "Ingreso eliminado";
        });

    /// <summary>Ingresa dinero al ahorro fuera de la aportación mensual.</summary>
    public Task<string?> IngresarAhorroAsync() =>
        EjecutarAsync(async () =>
        {
            var importe = Importe(ImporteDeposito);
            await api.CrearDepositoAhorroAsync(new CrearDepositoAhorroRequest(Depositante, importe, null, Texto(ConceptoDeposito)));
            ImporteDeposito = "";
            ConceptoDeposito = "";
            return "Ahorro añadido";
        });

    /// <summary>Retira dinero del ahorro.</summary>
    public Task<string?> RetirarAhorroAsync() =>
        EjecutarAsync(async () =>
        {
            var importe = Importe(ImporteRetirada);
            await api.CrearRetiradaAhorroAsync(new CrearRetiradaAhorroRequest(Retirante, importe, null, Texto(ConceptoRetirada)));
            ImporteRetirada = "";
            ConceptoRetirada = "";
            return "Retirada registrada";
        });

    /// <summary>Fija el saldo inicial de la cuenta a partir de la fecha y el importe escritos (sustituye el que hubiera).</summary>
    public Task<string?> FijarSaldoInicialAsync() =>
        EjecutarAsync(async () =>
        {
            if (FechaSaldoInicial is not { } fecha)
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, "Indica la fecha a la que corresponde el saldo.");
            if (!EntradaImporte.TryParse(ImporteSaldoInicial, permitirCero: true, out var importe))
                throw new ApiException(System.Net.HttpStatusCode.BadRequest, "Introduce un importe de 0 o más con máximo 2 decimales.");
            await api.FijarSaldoInicialAsync(new FijarSaldoInicialRequest(fecha, importe));
            return "Saldo inicial guardado";
        });

    /// <summary>Quita el saldo inicial de la cuenta.</summary>
    public Task<string?> EliminarSaldoInicialAsync() =>
        EjecutarAsync(async () =>
        {
            await api.EliminarSaldoInicialAsync();
            return "Saldo inicial quitado";
        });

    /// <summary>Elimina un ingreso al ahorro.</summary>
    public Task<string?> EliminarDepositoAsync(DepositoAhorroDto d) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarDepositoAhorroAsync(d.Id);
            ConfirmandoEliminar = null;
            return "Ingreso eliminado";
        });

    /// <summary>Elimina una retirada del ahorro.</summary>
    public Task<string?> EliminarRetiradaAsync(RetiradaAhorroDto r) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarRetiradaAhorroAsync(r.Id);
            ConfirmandoEliminar = null;
            return "Retirada eliminada";
        });

    /// <summary>Elimina un reembolso.</summary>
    public Task<string?> EliminarAsync(ReembolsoCuentaDto r) =>
        EjecutarAsync(async () =>
        {
            await api.EliminarReembolsoAsync(r.Id);
            ConfirmandoEliminar = null;
            return "Reembolso eliminado";
        });

    private static decimal Importe(string texto)
        => EntradaImporte.TryParse(texto, out var importe)
            ? importe
            : throw new ApiException(System.Net.HttpStatusCode.BadRequest, EntradaImporte.MensajeInvalido);

    private static string? Texto(string concepto) => string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim();

    /// <summary>
    /// Ejecuta una operación y recarga el estado; los errores de la API quedan en <see cref="Error"/>. Mientras hay una en curso se
    /// ignoran las demás. Devuelve el aviso a mostrar, o null si falló o se ignoró.
    /// </summary>
    private async Task<string?> EjecutarAsync(Func<Task<string>> accion)
    {
        if (Enviando) return null;
        Enviando = true;
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
            Enviando = false;
        }
    }
}
