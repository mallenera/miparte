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
    desde_ahorro boolean DEFAULT false NOT NULL,
    CONSTRAINT reembolso_cuenta_importe_check CHECK ((importe > (0)::numeric))
);


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
-- Name: reembolso_cuenta_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX reembolso_cuenta_fecha_idx ON public.reembolso_cuenta USING btree (hogar_id, fecha);
