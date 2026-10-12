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
-- Name: miembro_user_id_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX miembro_user_id_idx ON public.miembro USING btree (user_id);
