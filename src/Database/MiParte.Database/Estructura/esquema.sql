--
-- PostgreSQL database dump
--



SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA public;


--
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON SCHEMA public IS 'standard public schema';


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
-- Name: auditoria_inmutable(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.auditoria_inmutable() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO ''
    AS $$
begin
    -- El borrado en cascada desde hogar corre dentro de un trigger de integridad referencial
    -- (profundidad > 1); cualquier otro DELETE o UPDATE, aunque sea del propietario, se rechaza.
    if tg_op = 'DELETE' and pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception 'La auditoría es de solo añadir: no se puede modificar ni borrar';
end
$$;


--
-- Name: auditoria_sin_truncate(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.auditoria_sin_truncate() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO ''
    AS $$
begin
    raise exception 'La auditoría es de solo añadir: no se puede vaciar';
end
$$;


--
-- Name: bloquear_mes_hogar(uuid, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.bloquear_mes_hogar(p_hogar_id uuid, p_fecha date) RETURNS void
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended('mes_cerrado:' || p_hogar_id::text || ':' || pg_catalog.date_trunc('month', p_fecha)::date::text, 0));
$$;


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

    insert into public.hogar (nombre) values (p_nombre_hogar) returning id into v_hogar;
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
-- Name: es_admin_hogar(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.es_admin_hogar(p_hogar uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select exists (
        select 1 from public.miembro m
         where m.hogar_id = p_hogar
           and m.user_id = auth.uid()
           and m.rol = 'admin'
           and m.activo
    );
$$;


--
-- Name: exigir_mes_abierto(uuid, date[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.exigir_mes_abierto(p_hogar_id uuid, p_fechas date[]) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
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


--
-- Name: gasto_mes_abierto(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.gasto_mes_abierto() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
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


--
-- Name: gasto_reparto_mes_abierto(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.gasto_reparto_mes_abierto() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
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


--
-- Name: hogar_tiene_otro_admin(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select exists (
        select 1 from public.miembro m
         where m.hogar_id = p_hogar
           and m.id <> p_excluir
           and m.rol = 'admin'
           and m.activo
           and m.user_id is not null
    );
$$;


--
-- Name: mes_cerrado_bloquear(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.mes_cerrado_bloquear() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO ''
    AS $$
begin
    perform public.bloquear_mes_hogar(new.hogar_id, new.mes);
    return new;
end;
$$;


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
-- Name: mis_hogares(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.mis_hogares() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO ''
    AS $$
    select m.hogar_id
      from public.miembro m
     where m.user_id = auth.uid()
       and m.activo = true;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aportacion_cuenta; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aportacion_cuenta (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    desde date NOT NULL,
    importe numeric(12,2) NOT NULL,
    ahorro numeric(12,2) DEFAULT 0 NOT NULL,
    CONSTRAINT aportacion_cuenta_ahorro_valido CHECK (((ahorro >= (0)::numeric) AND (ahorro <= importe))),
    CONSTRAINT aportacion_cuenta_desde_check CHECK ((EXTRACT(day FROM desde) = (1)::numeric)),
    CONSTRAINT aportacion_cuenta_importe_check CHECK ((importe >= (0)::numeric))
);


--
-- Name: auditoria; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.auditoria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    usuario_id uuid,
    cuando timestamp with time zone DEFAULT now() NOT NULL,
    accion text NOT NULL,
    entidad text NOT NULL,
    entidad_id uuid NOT NULL,
    antes jsonb,
    despues jsonb,
    CONSTRAINT auditoria_accion_check CHECK ((accion = ANY (ARRAY['crear'::text, 'editar'::text, 'borrar'::text, 'vincular'::text, 'usar'::text]))),
    CONSTRAINT auditoria_check CHECK (((antes IS NOT NULL) OR (despues IS NOT NULL))),
    CONSTRAINT auditoria_entidad_check CHECK (((length(entidad) >= 1) AND (length(entidad) <= 40)))
);


--
-- Name: categoria; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.categoria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    nombre text NOT NULL,
    categoria_padre_id uuid,
    perfil_reparto_id uuid,
    a_cargo_cuenta_comun boolean DEFAULT false NOT NULL,
    CONSTRAINT categoria_nombre_check CHECK ((length(btrim(nombre)) > 0))
);


--
-- Name: deposito_ahorro; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.deposito_ahorro (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    concepto text,
    CONSTRAINT deposito_ahorro_concepto_check CHECK ((length(concepto) <= 200)),
    CONSTRAINT deposito_ahorro_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: gasto; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    categoria_id uuid NOT NULL,
    pagado_por uuid,
    perfil_reparto_id uuid NOT NULL,
    concepto text,
    gasto_recurrente_id uuid,
    a_cargo_cuenta_comun boolean DEFAULT false NOT NULL,
    pagado_desde_ahorro boolean DEFAULT false NOT NULL,
    es_personal boolean DEFAULT false NOT NULL,
    CONSTRAINT gasto_es_personal_check CHECK (((NOT es_personal) OR ((pagado_por IS NOT NULL) AND (NOT a_cargo_cuenta_comun) AND (NOT pagado_desde_ahorro)))),
    CONSTRAINT gasto_importe_check CHECK ((importe > (0)::numeric)),
    CONSTRAINT gasto_pagado_desde_ahorro_check CHECK (((NOT pagado_desde_ahorro) OR ((pagado_por IS NULL) AND a_cargo_cuenta_comun))),
    CONSTRAINT gasto_pagado_por_cuenta_comun_check CHECK (((pagado_por IS NOT NULL) OR a_cargo_cuenta_comun))
);


--
-- Name: gasto_recurrente; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto_recurrente (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    importe numeric(12,2) NOT NULL,
    categoria_id uuid NOT NULL,
    pagado_por uuid NOT NULL,
    perfil_reparto_id uuid NOT NULL,
    dia_mes smallint NOT NULL,
    concepto text,
    activo boolean DEFAULT true NOT NULL,
    CONSTRAINT gasto_recurrente_dia_mes_check CHECK (((dia_mes >= 1) AND (dia_mes <= 28))),
    CONSTRAINT gasto_recurrente_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: gasto_reparto; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gasto_reparto (
    gasto_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    hogar_id uuid NOT NULL,
    importe_asumido numeric(12,2) NOT NULL,
    CONSTRAINT gasto_reparto_importe_asumido_check CHECK ((importe_asumido >= (0)::numeric))
);


--
-- Name: hogar; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hogar (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    nombre text NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    cuenta_comun_activa boolean DEFAULT false NOT NULL,
    ahorro_activo boolean DEFAULT false NOT NULL,
    CONSTRAINT hogar_nombre_check CHECK ((length(btrim(nombre)) > 0))
);


--
-- Name: invitacion_hogar; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invitacion_hogar (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid,
    token_hash text NOT NULL,
    creada_por uuid DEFAULT auth.uid() NOT NULL,
    creada_en timestamp with time zone DEFAULT now() NOT NULL,
    caduca_en timestamp with time zone NOT NULL,
    usada_en timestamp with time zone,
    usada_por uuid,
    CONSTRAINT invitacion_hogar_check CHECK ((caduca_en > creada_en)),
    CONSTRAINT invitacion_hogar_token_hash_check CHECK ((token_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT invitacion_hogar_usada_coherente CHECK (((usada_por IS NULL) OR (usada_en IS NOT NULL)))
);


--
-- Name: mes_cerrado; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mes_cerrado (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    mes date NOT NULL,
    cerrado_en timestamp with time zone DEFAULT now() NOT NULL,
    cerrado_por uuid,
    CONSTRAINT mes_cerrado_mes_check CHECK ((EXTRACT(day FROM mes) = (1)::numeric))
);


--
-- Name: miembro; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.miembro (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    nombre text NOT NULL,
    tipo text NOT NULL,
    responsable_id uuid,
    user_id uuid,
    activo boolean DEFAULT true NOT NULL,
    rol text DEFAULT 'miembro'::text NOT NULL,
    permisos text[],
    CONSTRAINT miembro_check CHECK (((tipo = 'a_cargo'::text) = (responsable_id IS NOT NULL))),
    CONSTRAINT miembro_nombre_check CHECK ((length(btrim(nombre)) > 0)),
    CONSTRAINT miembro_permisos_check CHECK (((permisos IS NULL) OR (permisos <@ ARRAY['gastos.crear'::text, 'gastos.editar'::text, 'gastos.borrar'::text, 'recurrentes.gestionar'::text, 'pagos.registrar'::text, 'mes.cerrar'::text, 'mes.reabrir'::text, 'cuenta.movimientos'::text, 'ahorro.movimientos'::text, 'categorias.gestionar'::text, 'perfiles.gestionar'::text, 'historial.ver'::text, 'miembros.gestionar'::text, 'invitaciones.crear'::text, 'hogar.funciones'::text, 'permisos.gestionar'::text, 'hogar.eliminar'::text]))),
    CONSTRAINT miembro_rol_check CHECK ((rol = ANY (ARRAY['admin'::text, 'miembro'::text]))),
    CONSTRAINT miembro_tipo_check CHECK ((tipo = ANY (ARRAY['adulto'::text, 'a_cargo'::text])))
);


--
-- Name: pago_liquidacion; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.pago_liquidacion (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    mes date NOT NULL,
    de_miembro_id uuid NOT NULL,
    a_miembro_id uuid NOT NULL,
    importe numeric(12,2) NOT NULL,
    fecha date NOT NULL,
    concepto text,
    CONSTRAINT pago_liquidacion_check CHECK ((de_miembro_id <> a_miembro_id)),
    CONSTRAINT pago_liquidacion_importe_check CHECK ((importe > (0)::numeric)),
    CONSTRAINT pago_liquidacion_mes_check CHECK ((EXTRACT(day FROM mes) = (1)::numeric))
);


--
-- Name: perfil_reparto; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.perfil_reparto (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    nombre text NOT NULL,
    modo text NOT NULL,
    CONSTRAINT perfil_reparto_modo_check CHECK ((modo = ANY (ARRAY['porcentaje'::text, 'partes'::text, 'cuenta_comun'::text, 'individual'::text]))),
    CONSTRAINT perfil_reparto_nombre_check CHECK ((length(btrim(nombre)) > 0))
);


--
-- Name: perfil_reparto_detalle; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.perfil_reparto_detalle (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    perfil_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    valor numeric(12,4) NOT NULL,
    CONSTRAINT perfil_reparto_detalle_valor_check CHECK ((valor >= (0)::numeric))
);


--
-- Name: reembolso_cuenta; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reembolso_cuenta (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    concepto text,
    CONSTRAINT reembolso_cuenta_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: retirada_ahorro; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.retirada_ahorro (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    concepto text,
    CONSTRAINT retirada_ahorro_concepto_check CHECK ((length(concepto) <= 200)),
    CONSTRAINT retirada_ahorro_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: aportacion_cuenta aportacion_cuenta_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: aportacion_cuenta aportacion_cuenta_hogar_id_miembro_id_desde_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_hogar_id_miembro_id_desde_key UNIQUE (hogar_id, miembro_id, desde);


--
-- Name: aportacion_cuenta aportacion_cuenta_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_pkey PRIMARY KEY (id);


--
-- Name: auditoria auditoria_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auditoria
    ADD CONSTRAINT auditoria_pkey PRIMARY KEY (id);


--
-- Name: categoria categoria_hogar_id_categoria_padre_id_nombre_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_categoria_padre_id_nombre_key UNIQUE NULLS NOT DISTINCT (hogar_id, categoria_padre_id, nombre);


--
-- Name: categoria categoria_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: categoria categoria_nombre_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.categoria
    ADD CONSTRAINT categoria_nombre_longitud CHECK ((length(nombre) <= 100)) NOT VALID;


--
-- Name: categoria categoria_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_pkey PRIMARY KEY (id);


--
-- Name: deposito_ahorro deposito_ahorro_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deposito_ahorro
    ADD CONSTRAINT deposito_ahorro_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: deposito_ahorro deposito_ahorro_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deposito_ahorro
    ADD CONSTRAINT deposito_ahorro_pkey PRIMARY KEY (id);


--
-- Name: gasto gasto_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.gasto
    ADD CONSTRAINT gasto_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: gasto gasto_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: gasto gasto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_pkey PRIMARY KEY (id);


--
-- Name: gasto_recurrente gasto_recurrente_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_pkey PRIMARY KEY (id);


--
-- Name: gasto_reparto gasto_reparto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_pkey PRIMARY KEY (gasto_id, miembro_id);


--
-- Name: hogar hogar_nombre_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.hogar
    ADD CONSTRAINT hogar_nombre_longitud CHECK ((length(nombre) <= 100)) NOT VALID;


--
-- Name: hogar hogar_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hogar
    ADD CONSTRAINT hogar_pkey PRIMARY KEY (id);


--
-- Name: invitacion_hogar invitacion_hogar_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: invitacion_hogar invitacion_hogar_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_pkey PRIMARY KEY (id);


--
-- Name: invitacion_hogar invitacion_hogar_token_hash_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_token_hash_key UNIQUE (token_hash);


--
-- Name: mes_cerrado mes_cerrado_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: mes_cerrado mes_cerrado_hogar_id_mes_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_hogar_id_mes_key UNIQUE (hogar_id, mes);


--
-- Name: mes_cerrado mes_cerrado_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_pkey PRIMARY KEY (id);


--
-- Name: miembro miembro_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: miembro miembro_hogar_id_user_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_hogar_id_user_id_key UNIQUE (hogar_id, user_id);


--
-- Name: miembro miembro_nombre_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.miembro
    ADD CONSTRAINT miembro_nombre_longitud CHECK ((length(nombre) <= 100)) NOT VALID;


--
-- Name: miembro miembro_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_pkey PRIMARY KEY (id);


--
-- Name: pago_liquidacion pago_liquidacion_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: pago_liquidacion pago_liquidacion_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_pkey PRIMARY KEY (id);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_perfil_id_miembro_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_perfil_id_miembro_id_key UNIQUE (perfil_id, miembro_id);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_pkey PRIMARY KEY (id);


--
-- Name: perfil_reparto perfil_reparto_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: perfil_reparto perfil_reparto_hogar_id_nombre_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_hogar_id_nombre_key UNIQUE (hogar_id, nombre);


--
-- Name: perfil_reparto perfil_reparto_nombre_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_nombre_longitud CHECK ((length(nombre) <= 100)) NOT VALID;


--
-- Name: perfil_reparto perfil_reparto_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_pkey PRIMARY KEY (id);


--
-- Name: reembolso_cuenta reembolso_cuenta_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.reembolso_cuenta
    ADD CONSTRAINT reembolso_cuenta_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: reembolso_cuenta reembolso_cuenta_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reembolso_cuenta
    ADD CONSTRAINT reembolso_cuenta_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: reembolso_cuenta reembolso_cuenta_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reembolso_cuenta
    ADD CONSTRAINT reembolso_cuenta_pkey PRIMARY KEY (id);


--
-- Name: retirada_ahorro retirada_ahorro_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: retirada_ahorro retirada_ahorro_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_pkey PRIMARY KEY (id);


--
-- Name: auditoria_entidad_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX auditoria_entidad_idx ON public.auditoria USING btree (hogar_id, entidad, entidad_id);


--
-- Name: auditoria_hogar_cuando_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX auditoria_hogar_cuando_idx ON public.auditoria USING btree (hogar_id, cuando DESC);


--
-- Name: deposito_ahorro_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX deposito_ahorro_fecha_idx ON public.deposito_ahorro USING btree (hogar_id, fecha);


--
-- Name: gasto_categoria_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_categoria_idx ON public.gasto USING btree (hogar_id, categoria_id);


--
-- Name: gasto_mes_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_mes_idx ON public.gasto USING btree (hogar_id, fecha);


--
-- Name: gasto_recurrente_mes_uq; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX gasto_recurrente_mes_uq ON public.gasto USING btree (hogar_id, gasto_recurrente_id, ((date_trunc('month'::text, (fecha)::timestamp without time zone))::date)) WHERE (gasto_recurrente_id IS NOT NULL);


--
-- Name: gasto_reparto_miembro; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gasto_reparto_miembro ON public.gasto_reparto USING btree (hogar_id, miembro_id);


--
-- Name: invitacion_hogar_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX invitacion_hogar_idx ON public.invitacion_hogar USING btree (hogar_id);


--
-- Name: miembro_user_id_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX miembro_user_id_idx ON public.miembro USING btree (user_id);


--
-- Name: pago_liquidacion_mes_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX pago_liquidacion_mes_idx ON public.pago_liquidacion USING btree (hogar_id, mes);


--
-- Name: reembolso_cuenta_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX reembolso_cuenta_fecha_idx ON public.reembolso_cuenta USING btree (hogar_id, fecha);


--
-- Name: retirada_ahorro_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX retirada_ahorro_fecha_idx ON public.retirada_ahorro USING btree (hogar_id, fecha);


--
-- Name: auditoria auditoria_inmutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER auditoria_inmutable BEFORE DELETE OR UPDATE ON public.auditoria FOR EACH ROW EXECUTE FUNCTION public.auditoria_inmutable();


--
-- Name: auditoria auditoria_sin_truncate; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER auditoria_sin_truncate BEFORE TRUNCATE ON public.auditoria FOR EACH STATEMENT EXECUTE FUNCTION public.auditoria_sin_truncate();


--
-- Name: gasto gasto_mes_abierto; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gasto_mes_abierto BEFORE INSERT OR DELETE OR UPDATE ON public.gasto FOR EACH ROW EXECUTE FUNCTION public.gasto_mes_abierto();


--
-- Name: gasto_reparto gasto_reparto_mes_abierto; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gasto_reparto_mes_abierto BEFORE INSERT OR DELETE OR UPDATE ON public.gasto_reparto FOR EACH ROW EXECUTE FUNCTION public.gasto_reparto_mes_abierto();


--
-- Name: mes_cerrado mes_cerrado_bloquear; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER mes_cerrado_bloquear BEFORE INSERT ON public.mes_cerrado FOR EACH ROW EXECUTE FUNCTION public.mes_cerrado_bloquear();


--
-- Name: miembro miembro_proteger; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER miembro_proteger BEFORE INSERT OR DELETE OR UPDATE ON public.miembro FOR EACH ROW EXECUTE FUNCTION public.miembro_proteger();


--
-- Name: aportacion_cuenta aportacion_cuenta_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: aportacion_cuenta aportacion_cuenta_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aportacion_cuenta
    ADD CONSTRAINT aportacion_cuenta_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: auditoria auditoria_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auditoria
    ADD CONSTRAINT auditoria_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: categoria categoria_hogar_id_categoria_padre_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_categoria_padre_id_fkey FOREIGN KEY (hogar_id, categoria_padre_id) REFERENCES public.categoria(hogar_id, id);


--
-- Name: categoria categoria_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: categoria categoria_hogar_id_perfil_reparto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categoria
    ADD CONSTRAINT categoria_hogar_id_perfil_reparto_id_fkey FOREIGN KEY (hogar_id, perfil_reparto_id) REFERENCES public.perfil_reparto(hogar_id, id);


--
-- Name: deposito_ahorro deposito_ahorro_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deposito_ahorro
    ADD CONSTRAINT deposito_ahorro_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: deposito_ahorro deposito_ahorro_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deposito_ahorro
    ADD CONSTRAINT deposito_ahorro_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: gasto gasto_hogar_id_categoria_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_categoria_id_fkey FOREIGN KEY (hogar_id, categoria_id) REFERENCES public.categoria(hogar_id, id);


--
-- Name: gasto gasto_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: gasto gasto_hogar_id_gasto_recurrente_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_gasto_recurrente_id_fkey FOREIGN KEY (hogar_id, gasto_recurrente_id) REFERENCES public.gasto_recurrente(hogar_id, id);


--
-- Name: gasto gasto_hogar_id_pagado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_pagado_por_fkey FOREIGN KEY (hogar_id, pagado_por) REFERENCES public.miembro(hogar_id, id);


--
-- Name: gasto gasto_hogar_id_perfil_reparto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto
    ADD CONSTRAINT gasto_hogar_id_perfil_reparto_id_fkey FOREIGN KEY (hogar_id, perfil_reparto_id) REFERENCES public.perfil_reparto(hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_categoria_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_categoria_id_fkey FOREIGN KEY (hogar_id, categoria_id) REFERENCES public.categoria(hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_pagado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_pagado_por_fkey FOREIGN KEY (hogar_id, pagado_por) REFERENCES public.miembro(hogar_id, id);


--
-- Name: gasto_recurrente gasto_recurrente_hogar_id_perfil_reparto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_recurrente
    ADD CONSTRAINT gasto_recurrente_hogar_id_perfil_reparto_id_fkey FOREIGN KEY (hogar_id, perfil_reparto_id) REFERENCES public.perfil_reparto(hogar_id, id);


--
-- Name: gasto_reparto gasto_reparto_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: gasto_reparto gasto_reparto_hogar_id_gasto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_hogar_id_gasto_id_fkey FOREIGN KEY (hogar_id, gasto_id) REFERENCES public.gasto(hogar_id, id) ON DELETE CASCADE;


--
-- Name: gasto_reparto gasto_reparto_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gasto_reparto
    ADD CONSTRAINT gasto_reparto_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: invitacion_hogar invitacion_hogar_creada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_creada_por_fkey FOREIGN KEY (creada_por) REFERENCES auth.users(id) ON DELETE CASCADE;


--
-- Name: invitacion_hogar invitacion_hogar_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: invitacion_hogar invitacion_hogar_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id) ON DELETE CASCADE;


--
-- Name: invitacion_hogar invitacion_hogar_usada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invitacion_hogar
    ADD CONSTRAINT invitacion_hogar_usada_por_fkey FOREIGN KEY (usada_por) REFERENCES auth.users(id) ON DELETE SET NULL;


--
-- Name: mes_cerrado mes_cerrado_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mes_cerrado
    ADD CONSTRAINT mes_cerrado_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: miembro miembro_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: miembro miembro_hogar_id_responsable_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_hogar_id_responsable_id_fkey FOREIGN KEY (hogar_id, responsable_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: miembro miembro_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.miembro
    ADD CONSTRAINT miembro_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id) ON DELETE SET NULL;


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_a_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_a_miembro_id_fkey FOREIGN KEY (hogar_id, a_miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_de_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_de_miembro_id_fkey FOREIGN KEY (hogar_id, de_miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: pago_liquidacion pago_liquidacion_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pago_liquidacion
    ADD CONSTRAINT pago_liquidacion_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar_id_perfil_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto_detalle
    ADD CONSTRAINT perfil_reparto_detalle_hogar_id_perfil_id_fkey FOREIGN KEY (hogar_id, perfil_id) REFERENCES public.perfil_reparto(hogar_id, id) ON DELETE CASCADE;


--
-- Name: perfil_reparto perfil_reparto_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.perfil_reparto
    ADD CONSTRAINT perfil_reparto_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: reembolso_cuenta reembolso_cuenta_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reembolso_cuenta
    ADD CONSTRAINT reembolso_cuenta_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: reembolso_cuenta reembolso_cuenta_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reembolso_cuenta
    ADD CONSTRAINT reembolso_cuenta_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: retirada_ahorro retirada_ahorro_hogar_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_hogar_id_fkey FOREIGN KEY (hogar_id) REFERENCES public.hogar(id) ON DELETE CASCADE;


--
-- Name: retirada_ahorro retirada_ahorro_hogar_id_miembro_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.retirada_ahorro
    ADD CONSTRAINT retirada_ahorro_hogar_id_miembro_id_fkey FOREIGN KEY (hogar_id, miembro_id) REFERENCES public.miembro(hogar_id, id);


--
-- Name: aportacion_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.aportacion_cuenta ENABLE ROW LEVEL SECURITY;

--
-- Name: aportacion_cuenta aportacion_cuenta_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY aportacion_cuenta_hogar ON public.aportacion_cuenta TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: auditoria; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.auditoria ENABLE ROW LEVEL SECURITY;

--
-- Name: auditoria auditoria_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY auditoria_select ON public.auditoria FOR SELECT TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: categoria; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.categoria ENABLE ROW LEVEL SECURITY;

--
-- Name: categoria categoria_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY categoria_hogar ON public.categoria TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: deposito_ahorro; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.deposito_ahorro ENABLE ROW LEVEL SECURITY;

--
-- Name: deposito_ahorro deposito_ahorro_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY deposito_ahorro_select ON public.deposito_ahorro FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: gasto; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto gasto_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_hogar ON public.gasto TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: gasto_recurrente; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto_recurrente ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto_recurrente gasto_recurrente_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_recurrente_hogar ON public.gasto_recurrente TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: gasto_reparto; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gasto_reparto ENABLE ROW LEVEL SECURITY;

--
-- Name: gasto_reparto gasto_reparto_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY gasto_reparto_hogar ON public.gasto_reparto TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: hogar; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.hogar ENABLE ROW LEVEL SECURITY;

--
-- Name: hogar hogar_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY hogar_select ON public.hogar FOR SELECT TO authenticated USING ((id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: hogar hogar_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY hogar_update ON public.hogar FOR UPDATE TO authenticated USING ((id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: invitacion_hogar; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.invitacion_hogar ENABLE ROW LEVEL SECURITY;

--
-- Name: invitacion_hogar invitacion_hogar_delete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_delete ON public.invitacion_hogar FOR DELETE TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: invitacion_hogar invitacion_hogar_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_insert ON public.invitacion_hogar FOR INSERT TO authenticated WITH CHECK ((public.es_admin_hogar(hogar_id) AND (creada_por = auth.uid())));


--
-- Name: invitacion_hogar invitacion_hogar_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_select ON public.invitacion_hogar FOR SELECT TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: invitacion_hogar invitacion_hogar_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invitacion_hogar_update ON public.invitacion_hogar FOR UPDATE TO authenticated USING (public.es_admin_hogar(hogar_id)) WITH CHECK (public.es_admin_hogar(hogar_id));


--
-- Name: mes_cerrado; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.mes_cerrado ENABLE ROW LEVEL SECURITY;

--
-- Name: mes_cerrado mes_cerrado_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY mes_cerrado_select ON public.mes_cerrado FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: miembro; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.miembro ENABLE ROW LEVEL SECURITY;

--
-- Name: miembro miembro_delete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_delete ON public.miembro FOR DELETE TO authenticated USING (public.es_admin_hogar(hogar_id));


--
-- Name: miembro miembro_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_insert ON public.miembro FOR INSERT TO authenticated WITH CHECK (public.es_admin_hogar(hogar_id));


--
-- Name: miembro miembro_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_select ON public.miembro FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: miembro miembro_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY miembro_update ON public.miembro FOR UPDATE TO authenticated USING ((public.es_admin_hogar(hogar_id) OR ((user_id = auth.uid()) AND (hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))))) WITH CHECK ((public.es_admin_hogar(hogar_id) OR ((user_id = auth.uid()) AND (hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)))));


--
-- Name: pago_liquidacion; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.pago_liquidacion ENABLE ROW LEVEL SECURITY;

--
-- Name: pago_liquidacion pago_liquidacion_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY pago_liquidacion_hogar ON public.pago_liquidacion TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: perfil_reparto; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.perfil_reparto ENABLE ROW LEVEL SECURITY;

--
-- Name: perfil_reparto_detalle; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.perfil_reparto_detalle ENABLE ROW LEVEL SECURITY;

--
-- Name: perfil_reparto_detalle perfil_reparto_detalle_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY perfil_reparto_detalle_hogar ON public.perfil_reparto_detalle TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: perfil_reparto perfil_reparto_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY perfil_reparto_hogar ON public.perfil_reparto TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: reembolso_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reembolso_cuenta ENABLE ROW LEVEL SECURITY;

--
-- Name: reembolso_cuenta reembolso_cuenta_hogar; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reembolso_cuenta_hogar ON public.reembolso_cuenta TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares))) WITH CHECK ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: retirada_ahorro; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.retirada_ahorro ENABLE ROW LEVEL SECURITY;

--
-- Name: retirada_ahorro retirada_ahorro_select; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY retirada_ahorro_select ON public.retirada_ahorro FOR SELECT TO authenticated USING ((hogar_id IN ( SELECT public.mis_hogares() AS mis_hogares)));


--
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: -
--

GRANT USAGE ON SCHEMA public TO anon;
GRANT USAGE ON SCHEMA public TO authenticated;


--
-- Name: FUNCTION aceptar_invitacion(p_token text, p_nombre text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.aceptar_invitacion(p_token text, p_nombre text) FROM PUBLIC;


--
-- Name: FUNCTION auditoria_inmutable(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.auditoria_inmutable() FROM PUBLIC;


--
-- Name: FUNCTION auditoria_sin_truncate(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.auditoria_sin_truncate() FROM PUBLIC;


--
-- Name: FUNCTION bloquear_mes_hogar(p_hogar_id uuid, p_fecha date); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.bloquear_mes_hogar(p_hogar_id uuid, p_fecha date) FROM PUBLIC;


--
-- Name: FUNCTION crear_hogar(p_nombre_hogar text, p_nombre_miembro text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.crear_hogar(p_nombre_hogar text, p_nombre_miembro text) FROM PUBLIC;


--
-- Name: FUNCTION es_admin_hogar(p_hogar uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.es_admin_hogar(p_hogar uuid) FROM PUBLIC;
GRANT ALL ON FUNCTION public.es_admin_hogar(p_hogar uuid) TO authenticated;


--
-- Name: FUNCTION exigir_mes_abierto(p_hogar_id uuid, p_fechas date[]); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.exigir_mes_abierto(p_hogar_id uuid, p_fechas date[]) FROM PUBLIC;


--
-- Name: FUNCTION gasto_mes_abierto(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.gasto_mes_abierto() FROM PUBLIC;


--
-- Name: FUNCTION gasto_reparto_mes_abierto(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.gasto_reparto_mes_abierto() FROM PUBLIC;


--
-- Name: FUNCTION hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid) FROM PUBLIC;
GRANT ALL ON FUNCTION public.hogar_tiene_otro_admin(p_hogar uuid, p_excluir uuid) TO authenticated;


--
-- Name: FUNCTION mes_cerrado_bloquear(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.mes_cerrado_bloquear() FROM PUBLIC;


--
-- Name: FUNCTION miembro_proteger(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.miembro_proteger() FROM PUBLIC;


--
-- Name: FUNCTION mis_hogares(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.mis_hogares() FROM PUBLIC;
GRANT ALL ON FUNCTION public.mis_hogares() TO authenticated;


--
-- Name: TABLE aportacion_cuenta; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.aportacion_cuenta TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.aportacion_cuenta TO authenticated;


--
-- Name: TABLE auditoria; Type: ACL; Schema: public; Owner: -
--

GRANT SELECT ON TABLE public.auditoria TO authenticated;


--
-- Name: TABLE categoria; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.categoria TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.categoria TO authenticated;


--
-- Name: TABLE deposito_ahorro; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.deposito_ahorro TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.deposito_ahorro TO authenticated;


--
-- Name: TABLE gasto; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.gasto TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.gasto TO authenticated;


--
-- Name: TABLE gasto_recurrente; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.gasto_recurrente TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.gasto_recurrente TO authenticated;


--
-- Name: TABLE gasto_reparto; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.gasto_reparto TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.gasto_reparto TO authenticated;


--
-- Name: TABLE hogar; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.hogar TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.hogar TO authenticated;


--
-- Name: TABLE invitacion_hogar; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.invitacion_hogar TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.invitacion_hogar TO authenticated;


--
-- Name: TABLE mes_cerrado; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.mes_cerrado TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.mes_cerrado TO authenticated;


--
-- Name: TABLE miembro; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.miembro TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.miembro TO authenticated;


--
-- Name: TABLE pago_liquidacion; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.pago_liquidacion TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.pago_liquidacion TO authenticated;


--
-- Name: TABLE perfil_reparto; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.perfil_reparto TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.perfil_reparto TO authenticated;


--
-- Name: TABLE perfil_reparto_detalle; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.perfil_reparto_detalle TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.perfil_reparto_detalle TO authenticated;


--
-- Name: TABLE reembolso_cuenta; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.reembolso_cuenta TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.reembolso_cuenta TO authenticated;


--
-- Name: TABLE retirada_ahorro; Type: ACL; Schema: public; Owner: -
--

GRANT MAINTAIN ON TABLE public.retirada_ahorro TO anon;
GRANT SELECT,MAINTAIN ON TABLE public.retirada_ahorro TO authenticated;


--
-- Name: DEFAULT PRIVILEGES FOR TABLES; Type: DEFAULT ACL; Schema: public; Owner: -
--

ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT SELECT,REFERENCES,TRIGGER,MAINTAIN ON TABLES TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT SELECT,REFERENCES,TRIGGER,MAINTAIN ON TABLES TO authenticated;


--
-- PostgreSQL database dump complete
--


