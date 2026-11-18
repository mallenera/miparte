// Graba una nota de voz para el asistente con MediaRecorder. Va en un fichero aparte porque la CSP no admite scripts en línea.
// El audio no se guarda en el navegador: al parar se entrega en base64 a Blazor, que lo envía a Assistant.Api y lo olvida.
(function () {
    // Formatos que acepta el servidor, en orden de preferencia (Chrome y Edge: WebM; Firefox: Ogg; Safari: MP4).
    const formatos = ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4'];

    let grabadora = null;
    let flujo = null;
    let trozos = [];
    let temporizador = null;
    let componente = null;
    let cancelada = false;

    function liberar() {
        if (temporizador) clearTimeout(temporizador);
        temporizador = null;
        if (flujo) flujo.getTracks().forEach(function (pista) { pista.stop(); }); // apaga el indicador de micrófono
        flujo = null;
        grabadora = null;
        trozos = [];
    }

    function aBase64(blob) {
        return new Promise(function (resolver, rechazar) {
            const lector = new FileReader();
            lector.onload = function () { resolver(String(lector.result).split(',')[1] || ''); };
            lector.onerror = function () { rechazar(lector.error); };
            lector.readAsDataURL(blob);
        });
    }

    window.grabacionAudio = {
        // Pide el micrófono y empieza a grabar. Devuelve '' si ha empezado o el motivo del fallo para mostrarlo.
        // Al parar (a mano o al llegar a maxSegundos) llama a referencia.AlTerminarGrabacion(base64, tipo).
        iniciar: async function (referencia, maxSegundos) {
            if (grabadora) return 'Ya hay una grabación en curso.';
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || !window.MediaRecorder)
                return 'Este navegador no permite grabar audio.';
            const tipo = formatos.find(function (f) { return MediaRecorder.isTypeSupported(f); });
            if (!tipo) return 'Este navegador no graba en un formato que el asistente entienda.';
            try {
                flujo = await navigator.mediaDevices.getUserMedia({ audio: true });
            } catch (e) {
                liberar();
                return e && e.name === 'NotAllowedError'
                    ? 'No hay permiso para usar el micrófono. Actívalo en los ajustes del navegador.'
                    : 'No se ha podido abrir el micrófono.';
            }

            componente = referencia;
            cancelada = false;
            trozos = [];
            grabadora = new MediaRecorder(flujo, { mimeType: tipo });
            grabadora.ondataavailable = function (e) { if (e.data && e.data.size > 0) trozos.push(e.data); };
            grabadora.onstop = async function () {
                const blob = new Blob(trozos, { type: tipo });
                const destino = componente;
                const descartar = cancelada;
                liberar();
                componente = null;
                if (descartar || !destino) return;
                try {
                    await destino.invokeMethodAsync('AlTerminarGrabacion', await aBase64(blob), tipo);
                } catch (e) {
                    // El componente ya no existe (se cerró el panel): no hay a quién entregar el audio.
                }
            };
            grabadora.start();
            temporizador = setTimeout(function () { window.grabacionAudio.detener(); }, maxSegundos * 1000);
            return '';
        },

        // Para la grabación y entrega el audio.
        detener: function () {
            if (grabadora && grabadora.state !== 'inactive') grabadora.stop();
        },

        // Para la grabación sin entregar nada (p. ej. al cerrar el panel).
        cancelar: function () {
            cancelada = true;
            if (grabadora && grabadora.state !== 'inactive') grabadora.stop();
            else liberar();
        }
    };
})();
