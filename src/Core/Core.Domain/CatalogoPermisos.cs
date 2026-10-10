using MiParte.Core.Domain.Entidades;

namespace MiParte.Core.Domain;

/// <summary>Un permiso que un admin puede conceder o quitar a un miembro.</summary>
/// <param name="Clave">Identificador estable (se guarda en <c>miembro.permisos</c>).</param>
/// <param name="Etiqueta">Texto para la persona, en español.</param>
/// <param name="Grupo">Agrupación en la pantalla de permisos.</param>
/// <param name="PorDefecto">Si lo tiene un miembro que no tiene permisos propios: reproduce lo que podía hacer un miembro antes de existir los permisos.</param>
public sealed record Permiso(string Clave, string Etiqueta, string Grupo, bool PorDefecto);

/// <summary>Conjunto predefinido de permisos para asignar de golpe y ajustar después casilla a casilla.</summary>
/// <param name="Nombre">Nombre corto.</param>
/// <param name="Descripcion">Qué permite.</param>
/// <param name="Claves">Permisos que incluye.</param>
public sealed record PlantillaPermisos(string Nombre, string Descripcion, IReadOnlyList<string> Claves);

/// <summary>
/// Catálogo de permisos por miembro y cálculo de los efectivos. Un miembro sin lista propia (<c>miembro.permisos</c> nulo) sigue la
/// plantilla de su rol: el admin los tiene todos y el miembro los marcados por defecto; con lista propia tiene exactamente esos,
/// también un admin. Siempre debe quedar al menos un adulto activo con cuenta que pueda gestionar permisos.
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
    /// <summary>Añadir, renombrar, desactivar y reasignar a otros miembros.</summary>
    public const string MiembrosGestionar = "miembros.gestionar";
    /// <summary>Crear invitaciones.</summary>
    public const string InvitacionesCrear = "invitaciones.crear";
    /// <summary>Habilitar o deshabilitar la cuenta común y el ahorro del hogar.</summary>
    public const string HogarFunciones = "hogar.funciones";
    /// <summary>Cambiar los permisos de los miembros.</summary>
    public const string PermisosGestionar = "permisos.gestionar";
    /// <summary>Eliminar el hogar y todos sus datos, sin vuelta atrás.</summary>
    public const string HogarEliminar = "hogar.eliminar";

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
        new(MiembrosGestionar, "Añadir, renombrar y desactivar miembros", "Hogar", false),
        new(InvitacionesCrear, "Crear invitaciones", "Hogar", false),
        new(HogarFunciones, "Habilitar la cuenta común y el ahorro", "Hogar", false),
        new(PermisosGestionar, "Cambiar los permisos de los miembros", "Hogar", false),
        new(HogarEliminar, "Eliminar el hogar definitivamente", "Hogar", false),
    ];

    /// <summary>Claves de un miembro sin lista propia de permisos.</summary>
    public static IReadOnlyList<string> PorDefecto { get; } = Todos.Where(p => p.PorDefecto).Select(p => p.Clave).ToList();

    /// <summary>Plantillas para asignar rápido; «Administrador» marca todo y «Solo lectura» nada.</summary>
    public static IReadOnlyList<PlantillaPermisos> Plantillas { get; } =
    [
        new("Solo lectura", "Consulta todo, pero no cambia nada.", []),
        new("Colaborador", "Registra y edita lo del día a día; es lo que tiene un miembro nuevo.", PorDefecto),
        new("Gestor", "Todo lo anterior, reabre meses, ve el historial y gestiona miembros e invitaciones; no habilita funciones, ni cambia permisos, ni elimina el hogar.",
            Todos.Select(p => p.Clave).Where(c => c is not (HogarFunciones or PermisosGestionar or HogarEliminar)).ToList()),
        new("Administrador", "Todos los permisos, incluidos habilitar funciones y cambiar permisos.", Todos.Select(p => p.Clave).ToList()),
    ];


    /// <summary>Si la clave existe en el catálogo.</summary>
    /// <param name="clave">Clave a comprobar.</param>
    public static bool Existe(string clave) => Todos.Any(p => p.Clave == clave);

    /// <summary>
    /// Permisos que tiene un miembro: los de su lista propia si la tiene; si no, los de la plantilla de su rol (admin todos, miembro los
    /// marcados por defecto). Los adultos sin cuenta y los a cargo ninguno, porque no acceden a la aplicación.
    /// </summary>
    /// <param name="miembro">Miembro a evaluar.</param>
    public static IReadOnlySet<string> Efectivos(Miembro miembro)
    {
        if (miembro.Tipo != TipoMiembro.Adulto || miembro.UserId is null) return new HashSet<string>();
        if (miembro.Permisos is { } propios) return propios.Where(Existe).ToHashSet();
        return (miembro.Rol == RolMiembro.Admin ? Todos.Select(p => p.Clave) : PorDefecto).ToHashSet();
    }
}
