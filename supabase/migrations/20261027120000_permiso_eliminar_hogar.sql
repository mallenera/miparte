-- Permiso para eliminar el hogar.
--
-- Se añade 'hogar.eliminar' al catálogo de claves admitidas en miembro.permisos (Core.Domain.CatalogoPermisos). Nadie lo tiene
-- por defecto; un admin sin lista propia lo tiene por su plantilla. Eliminar un hogar borra en cascada todos sus datos,
-- incluida su auditoría (el trigger de solo añadir deja pasar el borrado en cascada desde hogar).

alter table public.miembro drop constraint miembro_permisos_check;

alter table public.miembro
    add constraint miembro_permisos_check
    check (permisos is null or permisos <@ array[
        'gastos.crear', 'gastos.editar', 'gastos.borrar', 'recurrentes.gestionar', 'pagos.registrar', 'mes.cerrar', 'mes.reabrir',
        'cuenta.movimientos', 'ahorro.movimientos', 'categorias.gestionar', 'perfiles.gestionar', 'historial.ver',
        'miembros.gestionar', 'invitaciones.crear', 'hogar.funciones', 'permisos.gestionar', 'hogar.eliminar'
    ]);
