-- Permisos asignables a cualquier miembro, también a los admins.
--
-- miembro.permisos pasa a ser nulo por defecto: nulo = sigue la plantilla de su rol (admin todos, miembro los de por defecto),
-- así los miembros e invitados nuevos y los admins existentes no necesitan relleno; una lista propia fija exactamente esos
-- permisos. Se amplía el catálogo con las capacidades que antes eran solo de admin (miembros, invitaciones, funciones del
-- hogar y permisos). El catálogo vive en Core.Domain (CatalogoPermisos); aquí solo se garantiza que no se guarda una clave desconocida.

alter table public.miembro drop constraint miembro_permisos_check;

alter table public.miembro
    alter column permisos drop default,
    alter column permisos drop not null;

-- Hasta ahora nadie tenía una lista propia distinta de la plantilla: todos pasan a seguirla.
update public.miembro set permisos = null;

alter table public.miembro
    add constraint miembro_permisos_check
    check (permisos is null or permisos <@ array[
        'gastos.crear', 'gastos.editar', 'gastos.borrar', 'recurrentes.gestionar', 'pagos.registrar', 'mes.cerrar', 'mes.reabrir',
        'cuenta.movimientos', 'ahorro.movimientos', 'categorias.gestionar', 'perfiles.gestionar', 'historial.ver',
        'miembros.gestionar', 'invitaciones.crear', 'hogar.funciones', 'permisos.gestionar'
    ]);
