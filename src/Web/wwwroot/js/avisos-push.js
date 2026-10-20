// Avisos del navegador (Notification + Push). Lo llama AvisosPush.razor por interop: va en un fichero propio porque la CSP
// no admite scripts en línea. Las suscripciones se devuelven aplanadas (endpoint, p256dh, auth) para leerlas desde C#.
window.miParteAvisos = {
    soportado: () => 'Notification' in window && 'serviceWorker' in navigator && 'PushManager' in window,

    // «granted», «denied» o «default». Sin Notification (iPhone fuera de la PWA instalada) devuelve «denied».
    permiso: () => ('Notification' in window) ? Notification.permission : 'denied',

    suscripcionActual: async () => {
        const registro = await navigator.serviceWorker.ready;
        const suscripcion = await registro.pushManager.getSubscription();
        return suscripcion ? aplanar(suscripcion) : null;
    },

    // Pide permiso si hace falta y se suscribe con la clave VAPID pública del servidor. Devuelve null si el usuario lo rechaza.
    activar: async (clavePublica) => {
        const permiso = await Notification.requestPermission();
        if (permiso !== 'granted') return null;

        const registro = await navigator.serviceWorker.ready;
        let suscripcion = await registro.pushManager.getSubscription();
        suscripcion ??= await registro.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: aBytes(clavePublica),
        });
        return aplanar(suscripcion);
    },

    // Cancela la suscripción del navegador y devuelve su endpoint para que el servidor la olvide; null si no había ninguna.
    desactivar: async () => {
        const registro = await navigator.serviceWorker.ready;
        const suscripcion = await registro.pushManager.getSubscription();
        if (!suscripcion) return null;
        const endpoint = suscripcion.endpoint;
        await suscripcion.unsubscribe();
        return endpoint;
    },
};

function aplanar(suscripcion) {
    const datos = suscripcion.toJSON();
    return { endpoint: datos.endpoint, p256dh: datos.keys.p256dh, auth: datos.keys.auth };
}

// La clave VAPID llega en base64url; PushManager.subscribe necesita bytes.
function aBytes(base64url) {
    const relleno = '='.repeat((4 - (base64url.length % 4)) % 4);
    const base64 = (base64url + relleno).replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
}
