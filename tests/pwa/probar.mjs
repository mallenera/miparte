// Prueba de humo del front publicado con su service worker real, servido como en Cloudflare Pages.
// Comprueba lo que los tests de bUnit no ven: que la app abre en la primera visita, en las siguientes (servida por
// el worker), en una ruta profunda y sin conexión, que una versión nueva se aplica sin dejarla rota y que la CSP
// no bloquea nada.
// Uso: node tests/pwa/probar.mjs <carpeta wwwroot publicada>
import { chromium } from 'playwright';
import { cp, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { arrancar } from './servidor.mjs';

const origen = process.argv[2];
if (!origen) { console.error('Uso: node tests/pwa/probar.mjs <carpeta wwwroot publicada>'); process.exit(2); }

// Se trabaja sobre una copia porque el paso de actualización modifica el manifiesto de recursos.
const raiz = await mkdtemp(path.join(tmpdir(), 'miparte-pwa-'));
await cp(origen, raiz, { recursive: true });
const { servidor, url } = await arrancar(raiz);
const navegador = await chromium.launch();
const contexto = await navegador.newContext({ serviceWorkers: 'allow' });
const pagina = await contexto.newPage();

const problemas = [];
pagina.on('pageerror', e => problemas.push(`Error de JavaScript: ${e.message}`));
pagina.on('console', m => { if (/Content Security Policy|Refused to/i.test(m.text())) problemas.push(`CSP: ${m.text()}`); });

let fallos = 0;
async function paso(nombre, accion) {
    try { await accion(); console.log(`ok    ${nombre}`); }
    catch (e) { fallos++; console.log(`FALLA ${nombre}\n      ${e.message.split('\n')[0]}`); }
}

// La app ha arrancado cuando Blazor sustituye la pantalla de carga y pinta un título (sin sesión, el de login).
async function abrir(ruta) {
    await pagina.goto(url + ruta, { waitUntil: 'load' });
    await pagina.waitForSelector('#app h1', { timeout: 60_000 });
    if (await pagina.isVisible('#blazor-error-ui')) throw new Error('Blazor muestra «Se ha producido un error inesperado»');
}
const controlada = () => pagina.evaluate(() => !!navigator.serviceWorker.controller);

await paso('primera visita (sin service worker)', async () => {
    await abrir('/');
    await pagina.evaluate(() => navigator.serviceWorker.ready);
});
await paso('segunda visita, servida por el service worker', async () => {
    await abrir('/');
    if (!(await controlada())) throw new Error('El service worker no controla la página');
});
await paso('ruta profunda (/gastos) desde el service worker', () => abrir('/gastos'));
// Sin conexión de verdad: se apaga el servidor. contexto.setOffline no sirve, porque no corta las peticiones que
// hace el propio service worker y la prueba pasaría aunque faltara algo en su caché.
await paso('sin conexión', async () => {
    const puerto = servidor.address().port;
    servidor.closeAllConnections();
    await new Promise(r => servidor.close(r));
    try { await abrir('/login'); }
    finally { await new Promise(r => servidor.listen(puerto, '127.0.0.1', r)); }
});

await paso('versión nueva desplegada: aviso, «Actualizar» y la app sigue abriendo', async () => {
    // Un despliegue cambia la versión del manifiesto: el navegador ve un worker distinto y lo instala en espera.
    const manifiesto = path.join(raiz, 'service-worker-assets.js');
    const texto = await readFile(manifiesto, 'utf8');
    const nueva = texto.replace(/("version":\s*")([^"]+)"/, '$1$2-nueva"');
    if (nueva === texto) throw new Error('No se encuentra la versión en service-worker-assets.js');
    await writeFile(manifiesto, nueva);

    await abrir('/');
    const boton = pagina.getByRole('button', { name: 'Actualizar' });
    await boton.waitFor({ timeout: 60_000 });
    await Promise.all([pagina.waitForEvent('load', { timeout: 60_000 }), boton.click()]);
    await pagina.waitForSelector('#app h1', { timeout: 60_000 });
    const cachés = await pagina.evaluate(() => caches.keys());
    if (cachés.length !== 1 || !cachés[0].endsWith('-nueva')) throw new Error(`Cachés tras actualizar: ${cachés.join(', ')}`);
    await abrir('/');
    if (!(await controlada())) throw new Error('El service worker nuevo no controla la página');
});

await paso('sin errores de JavaScript ni violaciones de CSP', async () => {
    if (problemas.length) throw new Error(problemas.join(' | '));
});

await navegador.close();
servidor.close();
if (fallos) { console.error(`\n${fallos} comprobación(es) fallida(s).`); process.exit(1); }
console.log('\nLa PWA abre en todas las situaciones probadas.');
