--
-- Name: deuda_cuenta; Type: ROW SECURITY; Schema: public; Owner: -
--

--
-- Name: deuda_cuenta; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.deuda_cuenta (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hogar_id uuid NOT NULL,
    miembro_id uuid NOT NULL,
    fecha date NOT NULL,
    importe numeric(12,2) NOT NULL,
    tipo text NOT NULL,
    desde_ahorro boolean DEFAULT false NOT NULL,
    concepto text,
    CONSTRAINT deuda_cuenta_importe_check CHECK ((importe > (0)::numeric)),
    CONSTRAINT deuda_cuenta_tipo_check CHECK ((tipo = ANY (ARRAY['adelanto'::text, 'devolucion'::text, 'compensacion'::text])))
);


--
-- Name: deuda_cuenta deuda_cuenta_concepto_longitud; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.deuda_cuenta
    ADD CONSTRAINT deuda_cuenta_concepto_longitud CHECK ((length(concepto) <= 200)) NOT VALID;


--
-- Name: deuda_cuenta deuda_cuenta_hogar_id_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deuda_cuenta
    ADD CONSTRAINT deuda_cuenta_hogar_id_id_key UNIQUE (hogar_id, id);


--
-- Name: deuda_cuenta deuda_cuenta_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deuda_cuenta
    ADD CONSTRAINT deuda_cuenta_pkey PRIMARY KEY (id);


--
-- Name: deuda_cuenta_miembro_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX deuda_cuenta_miembro_idx ON public.deuda_cuenta USING btree (hogar_id, miembro_id, desde_ahorro);
