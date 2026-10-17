--
-- Name: crear_hogar(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.crear_hogar(p_nombre_hogar text, p_nombre_miembro text) RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
declare
    v_hogar    uuid;
    v_miembro  uuid;
    v_partes   uuid;
    v_porcent  uuid;
    v_indiv    uuid;
    v_cuenta   uuid;
begin
    if auth.uid() is null then
        raise exception 'Usuario no autenticado';
    end if;

    -- Esta función solo crea hogares de tipo «hogar»; el resto de tipos se crea desde la API (ServicioHogares).
    insert into public.hogar (nombre, tipo) values (p_nombre_hogar, 'hogar') returning id into v_hogar;
    insert into public.miembro (hogar_id, nombre, tipo, user_id, rol)
    values (v_hogar, p_nombre_miembro, 'adulto', auth.uid(), 'admin')
    returning id into v_miembro;

    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Cuenta común', 'cuenta_comun') returning id into v_cuenta;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Por partes', 'partes') returning id into v_partes;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Porcentaje fijo', 'porcentaje') returning id into v_porcent;
    insert into public.perfil_reparto (hogar_id, nombre, modo)
    values (v_hogar, 'Individual', 'individual') returning id into v_indiv;

    insert into public.perfil_reparto_detalle (hogar_id, perfil_id, miembro_id, valor) values
        (v_hogar, v_partes,  v_miembro, 1),
        (v_hogar, v_porcent, v_miembro, 100);

    insert into public.categoria (hogar_id, nombre, perfil_reparto_id) values
        (v_hogar, 'Hipoteca/Alquiler',       v_partes),
        (v_hogar, 'Alimentación',            v_partes),
        (v_hogar, 'Suministros',             v_partes),
        (v_hogar, 'Gastos varios de casa',   v_partes),
        (v_hogar, 'Hijo',                    v_partes),
        (v_hogar, 'Ocio personal',           v_indiv);

    return v_hogar;
end
$$;


--
-- Name: FUNCTION crear_hogar(p_nombre_hogar text, p_nombre_miembro text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.crear_hogar(p_nombre_hogar text, p_nombre_miembro text) FROM PUBLIC;
