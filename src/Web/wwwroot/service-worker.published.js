// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
// La aplicación pide activar la versión nueva cuando el usuario pulsa «Actualizar» (ver js/registrar-sw.js).
self.addEventListener('message', event => { if (event.data === 'SKIP_WAITING') self.skipWaiting(); });
// Avisos del navegador: el servicio push despierta este worker con {titulo, cuerpo, ruta} (ver EnviadorNotificacionesPush).
self.addEventListener('push', event => event.waitUntil(mostrarAviso(event)));
self.addEventListener('notificationclick', event => event.waitUntil(abrirAviso(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /.woff2?$/, /\.png$/, /\.svg$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
// appsettings.json sí se precarga (Blazor lo lee al arrancar; sin él la app no abre sin conexión). Por eso el
// workflow de despliegue fija CoreUrl y AssistantUrl ANTES de publicar: si lo tocara después, su hash no
// coincidiría con el del manifiesto y la instalación del service worker fallaría por SRI.
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    // No vale cache.addAll: Cloudflare Pages responde a /index.html con un 308 a «/», la respuesta queda marcada como
    // redirigida y el navegador rechaza servirla a una navegación (la app no abre a partir de la segunda visita).
    // Se descarga todo primero (si algo falla, la versión anterior sigue intacta) y se guarda una copia limpia.
    const respuestas = await Promise.all(assetsRequests.map(async request => {
        const response = await fetch(request);
        if (!response.ok) throw new TypeError(`Service worker: ${request.url} respondió ${response.status}`);
        return sinRedireccion(response);
    }));
    const cache = await caches.open(cacheName);
    await Promise.all(assetsRequests.map((request, i) => cache.put(request, respuestas[i])));
}

// Copia de la respuesta sin la marca «redirected», que impide usarla para responder a una navegación.
async function sinRedireccion(response) {
    if (!response.redirected) return response;
    return new Response(await response.blob(), { status: response.status, statusText: response.statusText, headers: response.headers });
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        // Una caché dañada no debe impedir cargar la app: si falla, se pide a la red como si no hubiera caché.
        try {
            const cache = await caches.open(cacheName);
            cachedResponse = await cache.match(request);
        } catch (error) {
            console.warn('Service worker: caché no disponible, se usa la red', error);
        }
    }

    // Cachés guardadas por versiones anteriores del worker pueden contener el index.html redirigido.
    if (cachedResponse) return sinRedireccion(cachedResponse);
    try {
        return await fetch(event.request);
    } catch (error) {
        // Sin red, una navegación cae a la app en caché en vez de la página de error de Chrome (ERR_FAILED).
        if (event.request.mode === 'navigate') {
            const indice = await caches.match('index.html').catch(() => undefined);
            if (indice) return sinRedireccion(indice);
        }
        throw error;
    }
}

async function mostrarAviso(event) {
    let datos = {};
    try { datos = event.data ? event.data.json() : {}; } catch { /* carga no JSON: se muestra el aviso genérico */ }
    await self.registration.showNotification(datos.titulo || 'Mi parte, tu parte', {
        body: datos.cuerpo || '',
        icon: '/icon-192.png',
        badge: '/icon-192.png',
        data: { ruta: datos.ruta || '/' },
    });
}

async function abrirAviso(event) {
    event.notification.close();
    const url = new URL(event.notification.data?.ruta || '/', self.registration.scope).href;
    const ventanas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const abierta = ventanas.find(v => 'focus' in v);
    if (abierta) {
        await abierta.focus();
        return abierta.navigate ? abierta.navigate(url) : undefined;
    }
    return self.clients.openWindow(url);
}
