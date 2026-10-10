-- Permisos por miembro.
--
-- miembro.permisos: claves de lo que puede hacer un adulto con cuenta que no es admin (el admin lo tiene todo siempre y no
-- las usa). Core.Api responde 403 sin el permiso. El catálogo vive en Core.Domain (CatalogoPermisos); aquí solo se garantiza
-- que no se guarda una clave desconocida. Por defecto reproduce lo que podía hacer un miembro antes de existir los permisos
-- (todo menos reabrir un mes y ver el historial), así que desplegar no cambia el comportamiento de nadie.

alter table public.miembro
    add column permisos text[] not null default array[
        'gastos.crear', 'gastos.editar', 'gastos.borrar', 'recurrentes.gestionar', 'pagos.registrar', 'mes.cerrar',
        'cuenta.movimientos', 'ahorro.movimientos', 'categorias.gestionar', 'perfiles.gestionar'
    ];

alter table public.miembro
    add constraint miembro_permisos_check
    check (permisos <@ array[
        'gastos.crear', 'gastos.editar', 'gastos.borrar', 'recurrentes.gestionar', 'pagos.registrar', 'mes.cerrar', 'mes.reabrir',
        'cuenta.movimientos', 'ahorro.movimientos', 'categorias.gestionar', 'perfiles.gestionar', 'historial.ver'
    ]);
