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
