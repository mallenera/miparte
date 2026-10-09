using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Un permiso que un admin puede conceder o quitar a un miembro.</summary>
/// <param name="Clave">Identificador estable (se guarda en <c>miembro.permisos</c>).</param>
/// <param name="Etiqueta">Texto para la persona, en español.</param>
/// <param name="Grupo">Agrupación en la pantalla de permisos.</param>
/// <param name="PorDefecto">Si lo tiene un miembro nuevo: reproduce lo que podía hacer un miembro antes de existir los permisos.</param>
public sealed record Permiso(string Clave, string Etiqueta, string Grupo, bool PorDefecto);

/// <summary>
/// Catálogo de permisos por miembro y cálculo de los efectivos. El admin los tiene todos siempre; gestionar miembros e
/// invitaciones, activar cuenta común y ahorro, y editar permisos son solo de admin y no son permisos del catálogo.
/// </summary>
public static class CatalogoPermisos
{
    /// <summary>Crear gastos.</summary>
    public const string GastosCrear = "gastos.crear";
    /// <summary>Editar gastos.</summary>
    public const string GastosEditar = "gastos.editar";
    /// <summary>Borrar gastos.</summary>
    public const string GastosBorrar = "gastos.borrar";
    /// <summary>Gestionar y generar gastos recurrentes.</summary>
    public const string RecurrentesGestionar = "recurrentes.gestionar";
    /// <summary>Registrar y borrar pagos de liquidación.</summary>
    public const string PagosRegistrar = "pagos.registrar";
    /// <summary>Cerrar un mes.</summary>
    public const string MesCerrar = "mes.cerrar";
    /// <summary>Reabrir un mes cerrado.</summary>
    public const string MesReabrir = "mes.reabrir";
    /// <summary>Aportaciones y reembolsos de la cuenta común.</summary>
    public const string CuentaMovimientos = "cuenta.movimientos";
    /// <summary>Ingresos y retiradas de ahorro.</summary>
    public const string AhorroMovimientos = "ahorro.movimientos";
    /// <summary>Crear, editar y borrar categorías.</summary>
    public const string CategoriasGestionar = "categorias.gestionar";
    /// <summary>Crear, editar y borrar perfiles de reparto.</summary>
    public const string PerfilesGestionar = "perfiles.gestionar";
    /// <summary>Ver el historial de cambios del hogar.</summary>
    public const string HistorialVer = "historial.ver";

    /// <summary>Todos los permisos, en el orden en que se muestran.</summary>
    public static IReadOnlyList<Permiso> Todos { get; } =
    [
        new(GastosCrear, "Crear gastos", "Gastos", true),
        new(GastosEditar, "Editar gastos", "Gastos", true),
        new(GastosBorrar, "Borrar gastos", "Gastos", true),
        new(RecurrentesGestionar, "Gestionar y generar gastos recurrentes", "Gastos", true),
        new(PagosRegistrar, "Registrar y borrar pagos de liquidación", "Liquidación", true),
        new(MesCerrar, "Cerrar un mes", "Liquidación", true),
        new(MesReabrir, "Reabrir un mes cerrado", "Liquidación", false),
        new(CuentaMovimientos, "Aportaciones y reembolsos de la cuenta común", "Cuenta común", true),
        new(AhorroMovimientos, "Ingresos y retiradas de ahorro", "Cuenta común", true),
        new(CategoriasGestionar, "Crear, editar y borrar categorías", "Reparto", true),
        new(PerfilesGestionar, "Crear, editar y borrar perfiles de reparto", "Reparto", true),
        new(HistorialVer, "Ver el historial de cambios", "Hogar", false),
    ];

    /// <summary>Claves que recibe un miembro nuevo.</summary>
    public static IReadOnlyList<string> PorDefecto { get; } = Todos.Where(p => p.PorDefecto).Select(p => p.Clave).ToList();

    /// <summary>Si la clave existe en el catálogo.</summary>
    /// <param name="clave">Clave a comprobar.</param>
    public static bool Existe(string clave) => Todos.Any(p => p.Clave == clave);

    /// <summary>
    /// Permisos que tiene un miembro: un admin todos; un adulto con cuenta los que tiene concedidos; los demás (sin cuenta o a cargo)
    /// ninguno, porque no acceden a la aplicación.
    /// </summary>
    /// <param name="miembro">Miembro a evaluar.</param>
    public static IReadOnlySet<string> Efectivos(Miembro miembro)
    {
        if (miembro.Rol == RolMiembro.Admin) return Todos.Select(p => p.Clave).ToHashSet();
        if (miembro.Tipo != TipoMiembro.Adulto || miembro.UserId is null) return new HashSet<string>();
        return miembro.Permisos.Where(Existe).ToHashSet();
    }
}
