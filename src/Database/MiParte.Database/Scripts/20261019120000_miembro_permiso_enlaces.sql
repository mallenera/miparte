-- Permiso enlaces.gestionar (enlaces de grupo reutilizables). La estructura crea la restricción miembro_permisos_check solo
-- en una tabla nueva: en una base ya migrada conserva la lista antigua, así que se rehace aquí con la lista completa.
-- Quitar y volver a crear con la misma definición es idempotente; las listas existentes son subconjuntos de la nueva.
alter table public.miembro drop constraint if exists miembro_permisos_check;

alter table public.miembro add constraint miembro_permisos_check check (
    permisos is null or permisos <@ array[
        'gastos.crear', 'gastos.editar', 'gastos.borrar', 'recurrentes.gestionar', 'pagos.registrar', 'mes.cerrar',
        'mes.reabrir', 'cuenta.movimientos', 'ahorro.movimientos', 'categorias.gestionar', 'perfiles.gestionar',
        'historial.ver', 'miembros.gestionar', 'invitaciones.crear', 'hogar.funciones', 'permisos.gestionar',
        'hogar.eliminar', 'enlaces.gestionar'
    ]::text[]
);
