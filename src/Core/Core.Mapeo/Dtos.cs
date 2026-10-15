using MiParte.Contracts;
using MiParte.Core.Application.Cierres;
using MiParte.Core.Application.CuentaComun;
using MiParte.Core.Domain;
using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Mapeo;

/// <summary>
/// Conversión de entidades y resultados de los casos de uso a los DTO del contrato HTTP. Es la única copia: la usan tanto
/// <c>Core.Api</c> como el servidor del modo demo del front, de modo que ambos responden exactamente igual.
/// </summary>
public static class Dtos
{
    /// <summary>Gasto con su reparto, ordenado por miembro.</summary>
    public static GastoResponse ADto(this Gasto g) => new(
        g.Id, g.Fecha, g.Importe, g.CategoriaId, g.PagadoPor, g.PerfilRepartoId, g.Concepto, g.GastoRecurrenteId,
        g.Repartos.OrderBy(r => r.MiembroId).Select(r => new RepartoGastoDto(r.MiembroId, r.ImporteAsumido)).ToList(),
        g.ACargoCuentaComun, g.PagadoDesdeAhorro, g.EsPersonal);

    /// <summary>
    /// Miembro con tipo «adulto»/«a_cargo», rol «admin»/«miembro», si está vinculado a un usuario, si es el usuario autenticado
    /// (<paramref name="usuarioActual"/>) y sus permisos efectivos.
    /// </summary>
    public static MiembroDto ADto(this Miembro m, Guid? usuarioActual = null) => new(
        m.Id, m.Nombre, m.Tipo == TipoMiembro.Adulto ? TiposMiembro.Adulto : TiposMiembro.ACargo, m.ResponsableId, m.Activo,
        m.Rol == RolMiembro.Admin ? "admin" : "miembro", m.UserId is not null,
        usuarioActual is not null && m.UserId == usuarioActual,
        CatalogoPermisos.Todos.Select(p => p.Clave).Where(CatalogoPermisos.Efectivos(m).Contains).ToList());

    /// <summary>Perfil de reparto con el detalle ordenado por miembro y el modo en texto.</summary>
    public static PerfilRepartoDto ADto(this PerfilReparto p) => new(
        p.Id, p.Nombre, TextoModoReparto.ATexto(p.Modo),
        p.Detalles.OrderBy(d => d.MiembroId).Select(d => new PerfilDetalleDto(d.MiembroId, d.Valor)).ToList());

    /// <summary>Categoría de gasto.</summary>
    public static CategoriaDto ADto(this Categoria c) => new(c.Id, c.Nombre, c.CategoriaPadreId, c.PerfilRepartoId, c.ACargoCuentaComun);

    /// <summary>Plantilla de gasto recurrente.</summary>
    public static GastoRecurrenteResponse ADto(this GastoRecurrente r)
        => new(r.Id, r.Importe, r.CategoriaId, r.PagadoPor, r.PerfilRepartoId, r.DiaMes, r.Concepto, r.Activo);

    /// <summary>Hogar con las funciones activadas.</summary>
    public static HogarResumen ADto(this Hogar h) => new(h.Id, h.Nombre, h.CuentaComunActiva, h.AhorroActivo);

    /// <summary>Pago de liquidación.</summary>
    public static PagoLiquidacionDto ADto(this PagoLiquidacionRegistro p)
        => new(p.Id, p.Mes, p.DeMiembroId, p.AMiembroId, p.Importe, p.Fecha, p.Concepto);

    /// <summary>Aportación mensual a la cuenta común.</summary>
    public static AportacionCuentaDto ADto(this AportacionCuenta a) => new(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro);

    /// <summary>Reembolso de la cuenta común.</summary>
    public static ReembolsoCuentaDto ADto(this ReembolsoCuenta r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Depósito de ahorro.</summary>
    public static DepositoAhorroDto ADto(this DepositoAhorro d) => new(d.Id, d.MiembroId, d.Fecha, d.Importe, d.Concepto);

    /// <summary>Retirada de ahorro.</summary>
    public static RetiradaAhorroDto ADto(this RetiradaAhorro r) => new(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto);

    /// <summary>Mes cerrado con el nombre de quien lo cerró.</summary>
    public static MesCerradoDto ADto(this CierreConAutor c) => new(FormatosApi.FormatoMes(c.Cierre.Mes), c.Cierre.CerradoEn, c.Autor);

    /// <summary>Resumen mensual del hogar.</summary>
    public static ResumenMensualResponse ADto(this ResumenDelMes r) => new(
        FormatosApi.FormatoMes(r.Mes), r.GastosTotales,
        r.PorMiembro.Select(m => new ResumenMiembroDto(m.MiembroId, m.Nombre, m.Pagado, m.Asumido, m.PendienteCuenta, m.Personal)).ToList(),
        r.PorCategoria.Select(c => new ResumenCategoriaDto(
            c.CategoriaId, c.Nombre, c.Total, c.PorMiembro.Select(x => new ImporteMiembroDto(x.MiembroId, x.Importe)).ToList())).ToList(),
        r.Cuenta is { } c2
            ? new ResumenCuentaComunDto(c2.AportadoMes, c2.GastadoMes, c2.Saldo, c2.Efectivo, c2.PendienteReembolso, c2.AhorroMes, c2.AhorroDisponible)
            : null,
        r.Personales);

    /// <summary>Liquidación del mes que empieza en <paramref name="inicio"/>.</summary>
    public static LiquidacionResponse ADto(this LiquidacionCalculada c, DateOnly inicio) => new(
        FormatosApi.FormatoMes(inicio),
        c.Saldos.Select(s => new SaldoMiembroDto(s.MiembroId, c.Nombres.GetValueOrDefault(s.MiembroId, ""), s.Importe)).ToList(),
        c.Transferencias.Select(t => new TransferenciaDto(t.De, t.A, t.Importe)).ToList(),
        c.Pagos.Select(p => p.ADto()).ToList());

    /// <summary>Estado de la cuenta común al final de un mes.</summary>
    public static CuentaComunResponse ADto(this EstadoCuentaDelMes s)
    {
        var e = s.Estado;
        var d = s.Detalle;
        var nombres = d.Nombres;
        return new CuentaComunResponse(
            FormatosApi.FormatoMes(s.Mes), e.AportadoMes, e.Aportado, e.Gastado, e.Saldo,
            e.Pendientes.Select(p => new PendienteCuentaDto(p.MiembroId, nombres.GetValueOrDefault(p.MiembroId, ""), p.Importe)).ToList(),
            e.Efectivo, d.Aportaciones.Select(a => new AportacionCuentaDto(a.Id, a.MiembroId, a.Desde, a.Importe, a.Ahorro)).ToList(),
            d.ReembolsosDelMes.Select(r => new ReembolsoCuentaDto(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList(),
            e.AhorroMes, e.AhorroAcumulado, e.AhorroRetirado, e.AhorroDisponible,
            d.RetiradasDelMes.Select(r => new RetiradaAhorroDto(r.Id, r.MiembroId, r.Fecha, r.Importe, r.Concepto)).ToList(),
            e.AhorroDepositado,
            d.DepositosDelMes.Select(x => new DepositoAhorroDto(x.Id, x.MiembroId, x.Fecha, x.Importe, x.Concepto)).ToList(),
            e.AhorroGastado, d.Historico.CuentaActiva,
            s.Partes.Select(p => new PartePersonaDto(
                p.MiembroId, nombres.GetValueOrDefault(p.MiembroId, ""), p.Aportado, p.Ahorrado, p.PorcentajeGastos, p.PorcentajeAhorro,
                p.ParteSaldo, p.ParteAhorro, p.Pendiente)).ToList(),
            d.AhorroActivo, ADto(s.Extracto), s.SaldoInicial is { } si ? new SaldoInicialDto(si.Fecha, si.Importe) : null);
    }

    /// <summary>Extracto del mes en la forma que devuelve la API.</summary>
    private static ExtractoCuentaComunDto ADto(ExtractoCuentaComun x) => new(
        x.EfectivoInicial, x.Aportaciones, x.SaldoInicialFijado, x.GastosCuenta, x.GastosAdelantadosDelAhorro, x.Reembolsos, x.EfectivoFinal);
}
