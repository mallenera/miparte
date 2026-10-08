// Registra el service worker y avisa a la aplicación (componente AvisoActualizacion) cuando hay una versión nueva
// descargada y lista. Aplicarla es explícito: el worker nuevo espera hasta recibir 'SKIP_WAITING'.
window.miparteActualizacion = (function () {
    var suscriptor = null, pendiente = false, aplicando = false, registro = null;
    var api = {
        // Un solo suscriptor (el componente del layout); si ya había una versión lista, avisa de inmediato.
        suscribir: function (ref) { suscriptor = ref; if (pendiente) avisar(); },
        // Activa el worker en espera; al cambiar el controlador se recarga la página con la versión nueva.
        aplicar: function () {
            if (registro && registro.waiting) { aplicando = true; registro.waiting.postMessage('SKIP_WAITING'); }
            else location.reload();
        }
    };
    if (!('serviceWorker' in navigator)) return api;

    function avisar() { pendiente = true; if (suscriptor) suscriptor.invokeMethodAsync('HayVersionNueva'); }
    // Un worker «installed» con otro ya controlando la página es una actualización, no la primera instalación.
    function vigilar(worker) {
        worker.addEventListener('statechange', function () {
            if (worker.state === 'installed' && navigator.serviceWorker.controller) avisar();
        });
    }

    navigator.serviceWorker.register('service-worker.js').then(function (reg) {
        registro = reg;
        if (reg.waiting && navigator.serviceWorker.controller) avisar();
        if (reg.installing) vigilar(reg.installing);
        reg.addEventListener('updatefound', function () { if (reg.installing) vigilar(reg.installing); });
        // Una PWA abierta días no recomprueba sola: buscar al volver a la pestaña y cada hora.
        var buscar = function () { reg.update().catch(function () { }); };
        document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'visible') buscar(); });
        setInterval(buscar, 60 * 60 * 1000);
    });

    navigator.serviceWorker.addEventListener('controllerchange', function () { if (aplicando) location.reload(); });
    return api;
})();
