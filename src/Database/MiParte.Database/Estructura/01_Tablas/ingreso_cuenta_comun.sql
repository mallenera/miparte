--
-- Name: ingreso_cuenta_comun; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ingreso_cuenta_comun (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    concepto text,
    CONSTRAINT ingreso_cuenta_comun_concepto_check CHECK ((length(concepto) <= 200)),
    CONSTRAINT ingreso_cuenta_comun_importe_check CHECK ((importe > (0)::numeric))
);


--
-- Name: ingreso_cuenta_comun ingreso_cuenta_comun_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingreso_cuenta_comun
    ADD CONSTRAINT ingreso_cuenta_comun_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: ingreso_cuenta_comun ingreso_cuenta_comun_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingreso_cuenta_comun
    ADD CONSTRAINT ingreso_cuenta_comun_pkey PRIMARY KEY (id);


--
-- Name: ingreso_cuenta_comun_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ingreso_cuenta_comun_fecha_idx ON public.ingreso_cuenta_comun USING btree (hogar_id, fecha);
