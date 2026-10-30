using Microsoft.EntityFrameworkCore;
using MiParte.Core.Domain;

namespace MiParte.Core.Infrastructure.Persistencia;

/// <summary>
/// Lo que necesita el cálculo de la cuenta común (<see cref="CuentaComun.Calcular"/>), leído de la base de datos en una sola
/// consulta. Antes cada endpoint cargaba por separado, y completas, las tablas de gastos a cargo, reembolsos, retiradas y
/// depósitos del hogar: una consulta por tabla y tantas filas como historia tuviera el hogar. Aquí el histórico llega ya
/// agrupado (un par de filas por miembro) y acotado al fin del mes pedido, que es lo único que el cálculo usa; solo el
/// detalle del mes (<paramref name="detalleDelMes"/>) y las aportaciones, que son pocas, llegan fila a fila.
/// </summary>
public sealed class HistoricoCuentaComun
{
    private const int TipoAportacion = 0, TipoReembolsoMes = 1, TipoRetiradaMes = 2, TipoDepositoMes = 3, TipoReembolsos = 4,
        TipoGastos = 5, TipoGastosMes = 6, TipoRetiradas = 7, TipoDepositos = 8, TipoNombre = 9, TipoFunciones = 10, TipoIngresos = 11,
        TipoDeudaAdelanto = 12, TipoDeudaDevolucion = 13, TipoDeudaCompensacion = 14,
        TipoDeudaMesAdelanto = 15, TipoDeudaMesDevolucion = 16, TipoDeudaMesCompensacion = 17;

    /// <summary>
    /// Fila común a todas las partes de la consulta: EF las une con <c>UNION ALL</c> y para eso necesitan la misma forma. Los
    /// campos sin sentido en una parte llevan un valor neutro (no <c>null</c>) para que PostgreSQL pueda tipar la unión.
    /// </summary>
    public sealed class Fila
    {
        /// <summary>Qué parte de la consulta produjo la fila.</summary>
        public int Tipo { get; set; }
        /// <summary>Identificador del movimiento (solo en las filas de detalle).</summary>
        public Guid Id { get; set; }
        /// <summary>Miembro al que se refiere; <see cref="Guid.Empty"/> si es la cuenta o no aplica.</summary>
        public Guid MiembroId { get; set; }
        /// <summary>Fecha del movimiento (en las agrupadas, un día cualquiera del tramo que representan).</summary>
        public DateOnly Fecha { get; set; }
        /// <summary>Importe del movimiento o suma del grupo.</summary>
        public decimal Importe { get; set; }
        /// <summary>Parte de ahorro de una aportación.</summary>
        public decimal Ahorro { get; set; }
        /// <summary>Marca propia de cada tipo: pagado desde el ahorro, depósito del mes o cuenta común activa.</summary>
        public bool Marca { get; set; }
        /// <summary>Segunda marca: ahorro activo (solo en la fila de funciones).</summary>
        public bool Marca2 { get; set; }
        /// <summary>Concepto o nombre; cadena vacía si no hay.</summary>
        public string Texto { get; set; } = "";
    }

    private readonly List<Fila> _filas;
    private readonly DateOnly _inicio;
    // Frecuencia de aportación del hogar: se copia en cada aportación para que el cálculo sepa en qué meses cobra.
    private readonly FrecuenciaAportacion _frecuencia;

    /// <summary>Frecuencia con la que aporta el hogar a la cuenta común.</summary>
    public FrecuenciaAportacion Frecuencia => _frecuencia;

    private HistoricoCuentaComun(List<Fila> filas, DateOnly inicio, SaldoInicialCuenta? saldoInicial, FrecuenciaAportacion frecuencia)
    {
        _filas = filas;
        _inicio = inicio;
        SaldoInicial = saldoInicial;
        _frecuencia = frecuencia;
    }

    /// <summary>Saldo inicial fijado para la cuenta, o null; lo usan el estado de la cuenta y el resumen del mes.</summary>
    public SaldoInicialCuenta? SaldoInicial { get; }

    /// <summary>
    /// Lee en una consulta lo necesario para calcular la cuenta al final del mes que empieza en <paramref name="inicio"/>.
    /// </summary>
    /// <param name="db">Contexto (filtrado por hogar).</param>
    /// <param name="inicio">Primer día del mes.</param>
    /// <param name="detalleDelMes">Si también hacen falta los reembolsos, retiradas y depósitos del mes uno a uno y los nombres de los miembros.</param>
    /// <param name="ct">Cancelación.</param>
    public static async Task<HistoricoCuentaComun> LeerAsync(MiParteDbContext db, DateOnly inicio, bool detalleDelMes, CancellationToken ct)
    {
        var fin = inicio.AddMonths(1);
        // Variable y no Guid.Empty: así EF la envía como parámetro uuid. Un literal sin tipo haría que PostgreSQL resolviera la unión como texto.
        var vacio = Guid.Empty;

        // Gastos a cargo de la cuenta hasta el fin del mes: lo gastado y lo que se debe a quien los adelantó solo necesitan la suma por pagador.
        var gastos = db.Gastos.Where(g => g.ACargoCuentaComun && g.Fecha < fin)
            .GroupBy(g => new { g.PagadoPor, g.PagadoDesdeAhorro })
            .Select(g => new Fila
            {
                Tipo = TipoGastos, Id = vacio, MiembroId = g.Key.PagadoPor ?? vacio, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = g.Key.PagadoDesdeAhorro, Marca2 = false, Texto = "",
            });
        // Del mes, por pagador y origen: el extracto necesita separar lo que paga la cuenta, lo que adelanta alguien y lo que sale del ahorro.
        var gastosMes = db.Gastos.Where(g => g.ACargoCuentaComun && g.Fecha >= inicio && g.Fecha < fin)
            .GroupBy(g => new { g.PagadoPor, g.PagadoDesdeAhorro })
            .Select(g => new Fila
            {
                Tipo = TipoGastosMes, Id = vacio, MiembroId = g.Key.PagadoPor ?? vacio, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = g.Key.PagadoDesdeAhorro, Marca2 = false, Texto = "",
            });
        var reembolsos = db.ReembolsosCuenta.Where(r => r.Fecha < fin)
            .GroupBy(r => new { r.MiembroId, r.DesdeAhorro })
            .Select(g => new Fila
            {
                Tipo = TipoReembolsos, Id = vacio, MiembroId = g.Key.MiembroId, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = g.Key.DesdeAhorro, Marca2 = false, Texto = "",
            });
        var retiradas = db.RetiradasAhorro.Where(r => r.Fecha < fin)
            .GroupBy(r => true)
            .Select(g => new Fila
            {
                Tipo = TipoRetiradas, Id = vacio, MiembroId = vacio, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = false, Marca2 = false, Texto = "",
            });
        // Los depósitos se separan en «del mes» y «anteriores» porque el cálculo distingue lo ingresado este mes.
        var depositos = db.DepositosAhorro.Where(d => d.Fecha < fin)
            .GroupBy(d => new { d.MiembroId, EnMes = d.Fecha >= inicio })
            .Select(g => new Fila
            {
                Tipo = TipoDepositos, Id = vacio, MiembroId = g.Key.MiembroId, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = g.Key.EnMes, Marca2 = false, Texto = "",
            });
        // Los ingresos sueltos, igual que los depósitos: «del mes» y «anteriores», porque el extracto muestra los del mes.
        var ingresos = db.IngresosCuentaComun.Where(i => i.Fecha < fin)
            .GroupBy(i => i.Fecha >= inicio)
            .Select(g => new Fila
            {
                Tipo = TipoIngresos, Id = vacio, MiembroId = vacio, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = g.Key, Marca2 = false, Texto = "",
            });
        // Deudas con la cuenta por tipo, agrupadas por persona y fondo; Marca2 indica si son del mes (para fecharlas como los depósitos).
        var deudas = DeudasAgrupadas(db, inicio, fin, vacio, TipoMovimientoDeuda.Adelanto, TipoDeudaAdelanto)
            .Concat(DeudasAgrupadas(db, inicio, fin, vacio, TipoMovimientoDeuda.Devolucion, TipoDeudaDevolucion))
            .Concat(DeudasAgrupadas(db, inicio, fin, vacio, TipoMovimientoDeuda.Compensacion, TipoDeudaCompensacion));
        var aportaciones = db.AportacionesCuenta.Select(a => new Fila
        {
            Tipo = TipoAportacion, Id = a.Id, MiembroId = a.MiembroId, Fecha = a.Desde,
            Importe = a.Importe, Ahorro = a.Ahorro, Marca = false, Marca2 = false, Texto = "",
        });
        var funciones = db.Hogares.Select(h => new Fila
        {
            Tipo = TipoFunciones, Id = vacio, MiembroId = vacio, Fecha = inicio,
            Importe = 0m, Ahorro = 0m, Marca = h.CuentaComunActiva, Marca2 = h.AhorroActivo, Texto = "",
        });

        var consulta = gastos.Concat(gastosMes).Concat(reembolsos).Concat(retiradas).Concat(depositos).Concat(ingresos).Concat(aportaciones).Concat(funciones)
            .Concat(deudas);

        if (detalleDelMes)
        {
            consulta = consulta
                .Concat(db.ReembolsosCuenta.Where(r => r.Fecha >= inicio && r.Fecha < fin).Select(r => new Fila
                {
                    Tipo = TipoReembolsoMes, Id = r.Id, MiembroId = r.MiembroId, Fecha = r.Fecha,
                    Importe = r.Importe, Ahorro = 0m, Marca = r.DesdeAhorro, Marca2 = false, Texto = r.Concepto ?? "",
                }))
                .Concat(db.RetiradasAhorro.Where(r => r.Fecha >= inicio && r.Fecha < fin).Select(r => new Fila
                {
                    Tipo = TipoRetiradaMes, Id = r.Id, MiembroId = r.MiembroId, Fecha = r.Fecha,
                    Importe = r.Importe, Ahorro = 0m, Marca = false, Marca2 = false, Texto = r.Concepto ?? "",
                }))
                .Concat(db.DepositosAhorro.Where(d => d.Fecha >= inicio && d.Fecha < fin).Select(d => new Fila
                {
                    Tipo = TipoDepositoMes, Id = d.Id, MiembroId = d.MiembroId, Fecha = d.Fecha,
                    Importe = d.Importe, Ahorro = 0m, Marca = false, Marca2 = false, Texto = d.Concepto ?? "",
                }))
                .Concat(DeudasDelMesFilas(db, inicio, fin, TipoMovimientoDeuda.Adelanto, TipoDeudaMesAdelanto))
                .Concat(DeudasDelMesFilas(db, inicio, fin, TipoMovimientoDeuda.Devolucion, TipoDeudaMesDevolucion))
                .Concat(DeudasDelMesFilas(db, inicio, fin, TipoMovimientoDeuda.Compensacion, TipoDeudaMesCompensacion))
                .Concat(db.Miembros.Select(m => new Fila
                {
                    Tipo = TipoNombre, Id = m.Id, MiembroId = m.Id, Fecha = inicio,
                    Importe = 0m, Ahorro = 0m, Marca = false, Marca2 = false, Texto = m.Nombre,
                }));
        }

        var filas = await consulta.AsNoTracking().ToListAsync(ct);
        var saldo = await db.Hogares.Select(x => new { x.SaldoInicialFecha, x.SaldoInicialImporte, x.AportacionFrecuencia }).FirstOrDefaultAsync(ct);
        var saldoInicial = saldo?.SaldoInicialFecha is { } fecha && saldo.SaldoInicialImporte is { } importe
            ? new SaldoInicialCuenta(fecha, importe) : null;
        return new HistoricoCuentaComun(filas, inicio, saldoInicial, saldo?.AportacionFrecuencia ?? FrecuenciaAportacion.Mensual);
    }

    /// <summary>
    /// Deudas de un tipo hasta el fin del mes, sumadas por persona, fondo y si son del mes (<c>Marca2</c>): el cálculo necesita el
    /// total, no cada movimiento.
    /// </summary>
    private static IQueryable<Fila> DeudasAgrupadas(
        MiParteDbContext db, DateOnly inicio, DateOnly fin, Guid vacio, TipoMovimientoDeuda tipo, int tipoFila)
        => db.MovimientosDeudaCuenta.Where(d => d.Tipo == tipo && d.Fecha < fin)
            .GroupBy(d => new { d.MiembroId, d.DesdeAhorro, EnMes = d.Fecha >= inicio })
            .Select(g => new Fila
            {
                Tipo = tipoFila, Id = vacio, MiembroId = g.Key.MiembroId, Fecha = inicio,
                Importe = g.Sum(x => x.Importe), Ahorro = 0m, Marca = g.Key.DesdeAhorro, Marca2 = g.Key.EnMes, Texto = "",
            });

    /// <summary>Movimientos de deuda de un tipo con fecha dentro del mes, uno a uno (para mostrarlos y poder borrarlos).</summary>
    private static IQueryable<Fila> DeudasDelMesFilas(MiParteDbContext db, DateOnly inicio, DateOnly fin, TipoMovimientoDeuda tipo, int tipoFila)
        => db.MovimientosDeudaCuenta.Where(d => d.Tipo == tipo && d.Fecha >= inicio && d.Fecha < fin).Select(d => new Fila
        {
            Tipo = tipoFila, Id = d.Id, MiembroId = d.MiembroId, Fecha = d.Fecha, Importe = d.Importe, Ahorro = 0m,
            Marca = d.DesdeAhorro, Marca2 = false, Texto = d.Concepto ?? "",
        });

    private IEnumerable<Fila> De(int tipo) => _filas.Where(f => f.Tipo == tipo);

    private static Guid? Nulo(Guid id) => id == Guid.Empty ? null : id;

    private static string? Texto(Fila f) => f.Texto.Length == 0 ? null : f.Texto;

    /// <summary>Si el hogar tiene activada la cuenta común.</summary>
    public bool CuentaActiva => De(TipoFunciones).Any(f => f.Marca);

    /// <summary>Si el hogar tiene activado el ahorro.</summary>
    public bool AhorroActivo => De(TipoFunciones).Any(f => f.Marca2);

    /// <summary>Todas las aportaciones del hogar, ordenadas por miembro y mes.</summary>
    public List<Fila> Aportaciones => De(TipoAportacion).OrderBy(f => f.MiembroId).ThenBy(f => f.Fecha).ToList();

    /// <summary>Las aportaciones en la forma que usa el cálculo del dominio.</summary>
    public List<AportacionVigente> Vigentes => De(TipoAportacion)
        .Select(f => new AportacionVigente(f.MiembroId, f.Fecha, f.Importe, f.Ahorro, _frecuencia)).ToList();

    /// <summary>Gastos a cargo de la cuenta hasta el fin del mes, agrupados por pagador y origen (una fila por grupo).</summary>
    public List<GastoDeCuenta> Gastos => De(TipoGastos).Select(f => new GastoDeCuenta(Nulo(f.MiembroId), _inicio, f.Importe, f.Marca)).ToList();

    /// <summary>Total de los gastos a cargo de la cuenta con fecha dentro del mes (también los pagados desde el ahorro).</summary>
    public decimal GastadoMes => De(TipoGastosMes).Sum(f => f.Importe);

    /// <summary>Gastos a cargo de la cuenta con fecha en el mes, agrupados por pagador y origen (una fila por grupo).</summary>
    public List<GastoDeCuenta> GastosDelMes => De(TipoGastosMes).Select(f => new GastoDeCuenta(Nulo(f.MiembroId), _inicio, f.Importe, f.Marca)).ToList();

    /// <summary>Reembolsos hasta el fin del mes, agrupados por miembro.</summary>
    public List<ReembolsoDeCuenta> Reembolsos => De(TipoReembolsos).Select(f => new ReembolsoDeCuenta(f.MiembroId, _inicio, f.Importe, f.Marca)).ToList();

    /// <summary>Total retirado del ahorro hasta el fin del mes (una sola fila, o ninguna si no hay).</summary>
    public List<RetiradaDeAhorro> Retiradas => De(TipoRetiradas).Select(f => new RetiradaDeAhorro(_inicio, f.Importe)).ToList();

    /// <summary>Depósitos hasta el fin del mes agrupados por miembro; los del mes llevan la fecha del mes y los anteriores, el día previo.</summary>
    public List<DepositoDeAhorro> Depositos => De(TipoDepositos)
        .Select(f => new DepositoDeAhorro(f.Marca ? _inicio : _inicio.AddDays(-1), f.Importe, f.MiembroId)).ToList();

    /// <summary>Ingresos sueltos hasta el fin del mes; los del mes llevan la fecha del mes y los anteriores, el día previo.</summary>
    public List<IngresoDeCuenta> Ingresos => De(TipoIngresos)
        .Select(f => new IngresoDeCuenta(f.Marca ? _inicio : _inicio.AddDays(-1), f.Importe)).ToList();

    /// <summary>
    /// Deudas con la cuenta hasta el fin del mes, por persona, fondo y tipo. Las del mes llevan la fecha del mes y las anteriores,
    /// el día previo, igual que los depósitos.
    /// </summary>
    public List<MovimientoDeuda> Deudas => _filas
        .Where(f => f.Tipo is TipoDeudaAdelanto or TipoDeudaDevolucion or TipoDeudaCompensacion)
        .Select(f => new MovimientoDeuda(
            f.MiembroId, f.Marca2 ? _inicio : _inicio.AddDays(-1), f.Importe,
            f.Tipo switch
            {
                TipoDeudaAdelanto => TipoMovimientoDeuda.Adelanto,
                TipoDeudaDevolucion => TipoMovimientoDeuda.Devolucion,
                _ => TipoMovimientoDeuda.Compensacion,
            },
            f.Marca))
        .ToList();

    /// <summary>Movimientos de deuda con fecha en el mes, uno a uno y con su identificador (solo con <c>detalleDelMes</c>).</summary>
    public List<MovimientoDeuda> DeudasDelMes => _filas
        .Where(f => f.Tipo is TipoDeudaMesAdelanto or TipoDeudaMesDevolucion or TipoDeudaMesCompensacion)
        // En un mismo día, primero lo que genera la deuda y después lo que la salda (adelanto, devolución, compensación).
        .OrderBy(f => f.Fecha).ThenBy(f => f.Tipo).ThenBy(f => f.Id)
        .Select(f => new MovimientoDeuda(
            f.MiembroId, f.Fecha, f.Importe,
            f.Tipo switch
            {
                TipoDeudaMesAdelanto => TipoMovimientoDeuda.Adelanto,
                TipoDeudaMesDevolucion => TipoMovimientoDeuda.Devolucion,
                _ => TipoMovimientoDeuda.Compensacion,
            },
            f.Marca, f.Id, Texto(f)))
        .ToList();

    /// <summary>Nombre de cada miembro por id (solo con <c>detalleDelMes</c>).</summary>
    public Dictionary<Guid, string> Nombres => De(TipoNombre).ToDictionary(f => f.Id, f => f.Texto);

    /// <summary>Reembolsos del mes, uno a uno.</summary>
    public List<MovimientoCuenta> ReembolsosDelMes => De(TipoReembolsoMes).OrderBy(f => f.Fecha).ThenBy(f => f.Id)
        .Select(f => new MovimientoCuenta(f.Id, f.MiembroId, f.Fecha, f.Importe, Texto(f), f.Marca)).ToList();

    /// <summary>Retiradas de ahorro del mes, una a una.</summary>
    public List<MovimientoCuenta> RetiradasDelMes => De(TipoRetiradaMes).OrderBy(f => f.Fecha).ThenBy(f => f.Id)
        .Select(f => new MovimientoCuenta(f.Id, f.MiembroId, f.Fecha, f.Importe, Texto(f))).ToList();

    /// <summary>Depósitos de ahorro del mes, uno a uno.</summary>
    public List<MovimientoCuenta> DepositosDelMes => De(TipoDepositoMes).OrderBy(f => f.Fecha).ThenBy(f => f.Id)
        .Select(f => new MovimientoCuenta(f.Id, f.MiembroId, f.Fecha, f.Importe, Texto(f))).ToList();
}
