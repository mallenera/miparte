--
-- Name: hogar; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hogar (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    nombre text NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    cuenta_comun_activa boolean DEFAULT false NOT NULL,
    ahorro_activo boolean DEFAULT false NOT NULL,
    recurrentes_activas boolean DEFAULT true NOT NULL,
    liquidacion_modo text DEFAULT 'mensual'::text NOT NULL,
    saldo_inicial_fecha date,
    saldo_inicial_importe numeric(12,2),
    tipo text DEFAULT 'hogar'::text NOT NULL,
    periodo_desde date,
    periodo_hasta date,
    periodo_cerrado_en timestamp with time zone,
    aportacion_frecuencia text DEFAULT 'mensual'::text NOT NULL,
    CONSTRAINT hogar_aportacion_frecuencia_check CHECK ((aportacion_frecuencia = ANY (ARRAY['unica'::text, 'mensual'::text, 'bimestral'::text, 'trimestral'::text, 'semestral'::text, 'anual'::text]))),
    CONSTRAINT hogar_liquidacion_modo_check CHECK ((liquidacion_modo = ANY (ARRAY['mensual'::text, 'ciclos'::text]))),
    CONSTRAINT hogar_nombre_check CHECK ((length(btrim(nombre)) > 0)),
    CONSTRAINT hogar_periodo_cerrado_check CHECK (((periodo_cerrado_en IS NULL) OR ((periodo_desde IS NOT NULL) AND (periodo_hasta IS NOT NULL)))),
    CONSTRAINT hogar_periodo_check CHECK ((((periodo_desde IS NULL) AND (periodo_hasta IS NULL)) OR ((periodo_desde IS NOT NULL) AND ((periodo_hasta IS NULL) OR (periodo_hasta >= periodo_desde))))),
    CONSTRAINT hogar_saldo_inicial_check CHECK (((saldo_inicial_fecha IS NULL) = (saldo_inicial_importe IS NULL))),
    CONSTRAINT hogar_saldo_inicial_importe_check CHECK ((saldo_inicial_importe >= (0)::numeric)),
    CONSTRAINT hogar_tipo_check CHECK ((tipo = ANY (ARRAY['hogar'::text, 'piso_compartido'::text, 'viaje'::text, 'evento'::text, 'otro'::text])))
);


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
