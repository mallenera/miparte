-- Cierre de mes: serialización con las escrituras de gasto.
--
-- Core.Api comprueba mes_cerrado antes de guardar un gasto, pero entre la comprobación y el commit otra petición
-- puede cerrar el mes y el gasto se guardaría en un mes ya cerrado. La garantía tiene que estar en la base de datos:
-- cada escritura de gasto (y de su reparto) y cada cierre toman el mismo bloqueo advisory de transacción por
-- hogar + mes, y la escritura vuelve a comprobar mes_cerrado ya con el bloqueo. Con el bloqueo, o el cierre se confirma
-- antes (y la escritura lo ve y se rechaza) o la escritura se confirma antes (y el cierre espera a que termine).
-- Un trigger que solo comprobara, sin bloquear, no bastaría: dos transacciones concurrentes no se ven.
--
-- El rechazo es un error con SQLSTATE 'MP409' que Core.Api traduce al 409 «Ese mes está cerrado…».

-- Bloquea (hasta el final de la transacción) el par hogar + mes. mes se normaliza al primer día del mes.
create function public.bloquear_mes_hogar(p_hogar_id uuid, p_fecha date)
returns void
language sql
security definer
set search_path = ''
as $$
    select pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended('mes_cerrado:' || p_hogar_id::text || ':' || pg_catalog.date_trunc('month', p_fecha)::date::text, 0));
$$;

-- Bloquea los meses de las fechas indicadas (de menor a mayor, para que dos transacciones que toquen los mismos meses
-- no se bloqueen entre sí) y rechaza si alguno está cerrado. No hace nada si el hogar ya no existe: es el borrado en cascada.
create function public.exigir_mes_abierto(p_hogar_id uuid, p_fechas date[])
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_mes date;
begin
    if not exists (select 1 from public.hogar where id = p_hogar_id) then
        return;
    end if;
    for v_mes in
        select distinct pg_catalog.date_trunc('month', f)::date from pg_catalog.unnest(p_fechas) as f order by 1
    loop
        perform public.bloquear_mes_hogar(p_hogar_id, v_mes);
        -- Consulta después del bloqueo: en READ COMMITTED ve ya el cierre confirmado por la otra transacción.
        if exists (select 1 from public.mes_cerrado where hogar_id = p_hogar_id and mes = v_mes) then
            raise exception 'Ese mes está cerrado: un administrador debe reabrirlo para cambiar sus gastos.'
                using errcode = 'MP409', hint = pg_catalog.to_char(v_mes, 'YYYY-MM');
        end if;
    end loop;
end;
$$;

-- Trigger de gasto: mira la fecha antigua (update/delete) y la nueva (insert/update), de modo que no se puede sacar un
-- gasto de un mes cerrado ni meterlo en uno.
create function public.gasto_mes_abierto()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if tg_op = 'DELETE' then
        perform public.exigir_mes_abierto(old.hogar_id, array[old.fecha]);
        return old;
    elsif tg_op = 'INSERT' then
        perform public.exigir_mes_abierto(new.hogar_id, array[new.fecha]);
    else
        perform public.exigir_mes_abierto(new.hogar_id, array[old.fecha, new.fecha]);
    end if;
    return new;
end;
$$;

create trigger gasto_mes_abierto
    before insert or update or delete on public.gasto
    for each row execute function public.gasto_mes_abierto();

-- Trigger de gasto_reparto: un PUT que cambia solo el reparto (misma fecha e importe) no toca la fila de gasto.
create function public.gasto_reparto_mes_abierto()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_hogar uuid := case when tg_op = 'INSERT' then new.hogar_id else old.hogar_id end;
    v_gasto uuid := case when tg_op = 'INSERT' then new.gasto_id else old.gasto_id end;
    v_fecha date;
begin
    select fecha into v_fecha from public.gasto where hogar_id = v_hogar and id = v_gasto;
    -- Sin gasto (borrado en cascada de un gasto ya eliminado): el trigger de gasto ya lo comprobó.
    if v_fecha is not null then
        perform public.exigir_mes_abierto(v_hogar, array[v_fecha]);
    end if;
    return case when tg_op = 'DELETE' then old else new end;
end;
$$;

create trigger gasto_reparto_mes_abierto
    before insert or update or delete on public.gasto_reparto
    for each row execute function public.gasto_reparto_mes_abierto();

-- Trigger de mes_cerrado: el cierre toma el mismo bloqueo, así espera a las escrituras de gasto en curso de ese mes.
-- Reabrir (delete) no necesita bloqueo: una escritura concurrente solo puede ser rechazada o aceptada por una de las dos
-- versiones del estado, ambas válidas.
create function public.mes_cerrado_bloquear()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    perform public.bloquear_mes_hogar(new.hogar_id, new.mes);
    return new;
end;
$$;

create trigger mes_cerrado_bloquear
    before insert on public.mes_cerrado
    for each row execute function public.mes_cerrado_bloquear();

-- Todas son SECURITY DEFINER: la comprobación ve mes_cerrado y hogar sea cual sea el rol (y su RLS) que escribe el gasto.
-- Son funciones internas de los triggers: ningún rol de cliente las llama.
revoke execute on function
    public.bloquear_mes_hogar(uuid, date), public.exigir_mes_abierto(uuid, date[]),
    public.gasto_mes_abierto(), public.gasto_reparto_mes_abierto(), public.mes_cerrado_bloquear()
    from public, anon, authenticated;
