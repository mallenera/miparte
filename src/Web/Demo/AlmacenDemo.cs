using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Web.Demo;

/// <summary>
/// Estado en memoria del hogar de ejemplo del modo demo: las mismas entidades de dominio que persiste Core.Api. Los repositorios
/// de <see cref="RepositoriosEnMemoria"/> exponen estas listas a los casos de uso reales de <c>Core.Application</c>, así que las
/// reglas que se ven en la demo son las del servidor y no una copia.
/// </summary>
internal sealed class AlmacenDemo
{
    /// <summary>Hogar de ejemplo, con las funciones de cuenta común y ahorro activadas.</summary>
    public Hogar Hogar { get; }

    /// <summary>Miembros del hogar.</summary>
    public List<Miembro> Miembros { get; } = [];

    /// <summary>Miembro que hace de la sesión local (quien ve los gastos personales en la demo), o null.</summary>
    public Guid? MiembroActual => Miembros.FirstOrDefault(m => m.UserId == Guid.Parse(CuentaDemo.IdUsuarioLocal))?.Id;

    /// <summary>Perfiles de reparto con su detalle.</summary>
    public List<PerfilReparto> Perfiles { get; } = [];

    /// <summary>Categorías de gasto.</summary>
    public List<Categoria> Categorias { get; } = [];

    /// <summary>Gastos con su reparto.</summary>
    public List<Gasto> Gastos { get; } = [];

    /// <summary>Plantillas de gasto recurrente.</summary>
    public List<GastoRecurrente> Recurrentes { get; } = [];

    /// <summary>Aportaciones a la cuenta común.</summary>
    public List<AportacionCuenta> Aportaciones { get; } = [];

    /// <summary>Reembolsos de la cuenta común.</summary>
    public List<ReembolsoCuenta> Reembolsos { get; } = [];
    /// <summary>Productos de la lista de la compra.</summary>
    public List<ProductoListaCompra> Productos { get; } = [];

    /// <summary>Enlaces QR de la lista de la compra.</summary>
    public List<EnlaceListaCompra> EnlacesListaCompra { get; } = [];

    /// <summary>Movimientos de deuda de las personas con la cuenta común o su ahorro.</summary>
    public List<MovimientoDeudaCuenta> Deudas { get; } = [];

    /// <summary>Depósitos de ahorro.</summary>
    public List<DepositoAhorro> Depositos { get; } = [];
    public List<IngresoCuentaComun> Ingresos { get; } = [];

    /// <summary>Retiradas de ahorro.</summary>
    public List<RetiradaAhorro> Retiradas { get; } = [];

    /// <summary>Pagos de liquidación.</summary>
    public List<PagoLiquidacionRegistro> Pagos { get; } = [];

    /// <summary>Ciclos de liquidación cerrados (modo por ciclos).</summary>
    public List<CicloLiquidacion> Ciclos { get; } = [];

    /// <summary>Meses cerrados.</summary>
    public List<MesCerrado> Cierres { get; } = [];

    /// <summary>Invitaciones creadas (en la demo nadie puede aceptarlas).</summary>
    public List<InvitacionHogar> Invitaciones { get; } = [];

    /// <summary>Crea el almacén vacío de un hogar.</summary>
    /// <param name="hogar">Hogar de ejemplo.</param>
    public AlmacenDemo(Hogar hogar) => Hogar = hogar;

    /// <summary>Nombre de cada miembro por id.</summary>
    public Dictionary<Guid, string> Nombres() => Miembros.ToDictionary(m => m.Id, m => m.Nombre);

    /// <summary>Adultos activos ordenados por nombre y id.</summary>
    public List<Miembro> AdultosActivos()
        => Miembros.Where(m => m.Activo && m.Tipo == TipoMiembro.Adulto).OrderBy(m => m.Nombre).ThenBy(m => m.Id).ToList();

    /// <summary>
    /// Añade un gasto de ejemplo con su reparto calculado por el dominio; se omite si cae después de <paramref name="hoy"/>.
    /// </summary>
    public void SembrarGasto(
        Guid hogarId, DateOnly hoy, DateOnly mes, int dia, decimal importe, Guid categoria, Guid? pagador, Guid perfil, string concepto,
        Guid? recurrente = null)
    {
        var fecha = new DateOnly(mes.Year, mes.Month, dia);
        if (fecha > hoy) return;

        var p = Perfiles.First(x => x.Id == perfil);
        var gasto = new Gasto
        {
            Id = Guid.NewGuid(), HogarId = hogarId, Fecha = fecha, Importe = importe, CategoriaId = categoria, PagadoPor = pagador,
            PerfilRepartoId = perfil, Concepto = concepto, GastoRecurrenteId = recurrente,
            ACargoCuentaComun = ModosDeCuenta.EsDeCuentaComun(p.Modo), PagadoDesdeAhorro = p.Modo == ModoReparto.CuentaComunAhorro,
        };
        RepartoGasto.Aplicar(gasto, RepartoGasto.Repartir(p, AdultosActivos(), importe, pagador ?? Guid.Empty, out _) ?? []);
        Gastos.Add(gasto);
    }
}
