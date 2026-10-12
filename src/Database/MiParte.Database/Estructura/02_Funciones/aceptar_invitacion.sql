--
-- Name: aceptar_invitacion(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.aceptar_invitacion(p_token text, p_nombre text DEFAULT NULL::text) RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
declare
    v_user uuid := auth.uid();
    v_inv  public.invitacion_hogar;
    v_miembro public.miembro;
begin
    if v_user is null then
        raise exception 'Usuario no autenticado';
    end if;
    if p_token is null or length(p_token) = 0 then
        raise exception 'Invitación no válida';
    end if;

    -- Bloqueo de fila: dos aceptaciones simultáneas no pueden usar el mismo token.
    select * into v_inv
      from public.invitacion_hogar i
     where i.token_hash = encode(sha256(convert_to(p_token, 'UTF8')), 'hex')
     for update;

    if not found then
        raise exception 'Invitación no válida';
    end if;
    if v_inv.usada_en is not null then
        raise exception 'La invitación ya fue utilizada';
    end if;
    if v_inv.caduca_en <= now() then
        raise exception 'La invitación ha caducado';
    end if;

    -- Un usuario no puede figurar dos veces en el mismo hogar (activo o no).
    if exists (select 1 from public.miembro m
                where m.hogar_id = v_inv.hogar_id and m.user_id = v_user) then
        raise exception 'Ya perteneces a este hogar';
    end if;

    if v_inv.miembro_id is not null then
        select * into v_miembro
          from public.miembro m
         where m.hogar_id = v_inv.hogar_id and m.id = v_inv.miembro_id
         for update;

        if not found or not v_miembro.activo then
            raise exception 'El miembro de la invitación ya no está disponible';
        end if;
        if v_miembro.user_id is not null then
            raise exception 'El miembro de la invitación ya está vinculado a un usuario';
        end if;

        update public.miembro set user_id = v_user where id = v_miembro.id;
    else
        if p_nombre is null or length(btrim(p_nombre)) = 0 then
            raise exception 'Hay que indicar un nombre para unirse al hogar';
        end if;

        insert into public.miembro (hogar_id, nombre, tipo, user_id, rol)
        values (v_inv.hogar_id, btrim(p_nombre), 'adulto', v_user, 'miembro');
    end if;

    update public.invitacion_hogar
       set usada_en = now(), usada_por = v_user
     where id = v_inv.id;

    return v_inv.hogar_id;
end
$$;


--
-- Name: FUNCTION aceptar_invitacion(p_token text, p_nombre text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.aceptar_invitacion(p_token text, p_nombre text) FROM PUBLIC;
