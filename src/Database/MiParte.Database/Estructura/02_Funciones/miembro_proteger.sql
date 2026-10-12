--
-- Name: miembro_proteger(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.miembro_proteger() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO ''
    AS $$
begin
    -- Solo se restringe a los clientes con JWT; servicio y funciones definer pasan.
    if current_user not in ('authenticated', 'anon') then
        if tg_op = 'DELETE' then return old; end if;
        return new;
    end if;

    if tg_op = 'INSERT' then
        if new.user_id is not null then
            raise exception 'El usuario de un miembro solo se vincula mediante una invitación';
        end if;
        return new;
    end if;

    if tg_op = 'DELETE' then
        if old.rol = 'admin' and old.activo and old.user_id is not null
           and not public.hogar_tiene_otro_admin(old.hogar_id, old.id) then
            raise exception 'El hogar debe conservar al menos un administrador';
        end if;
        return old;
    end if;

    -- UPDATE
    if new.hogar_id <> old.hogar_id then
        raise exception 'No se puede mover un miembro a otro hogar';
    end if;

    if new.user_id is distinct from old.user_id
       and not (new.user_id is null and public.es_admin_hogar(old.hogar_id)) then
        raise exception 'El usuario de un miembro solo se vincula mediante una invitación';
    end if;

    if not public.es_admin_hogar(old.hogar_id) then
        if new.rol is distinct from old.rol
           or new.activo is distinct from old.activo
           or new.tipo is distinct from old.tipo
           or new.responsable_id is distinct from old.responsable_id then
            raise exception 'Solo un administrador puede cambiar el rol o el estado de un miembro';
        end if;
    end if;

    if old.rol = 'admin' and old.activo and old.user_id is not null
       and (new.rol <> 'admin' or not new.activo or new.user_id is null)
       and not public.hogar_tiene_otro_admin(old.hogar_id, old.id) then
        raise exception 'El hogar debe conservar al menos un administrador';
    end if;

    return new;
end
$$;


--
-- Name: FUNCTION miembro_proteger(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.miembro_proteger() FROM PUBLIC;
