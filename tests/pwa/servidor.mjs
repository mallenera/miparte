// Servidor estático que se comporta como Cloudflare Pages en lo que afecta al service worker: redirige con 308
// las rutas .html a su versión sin extensión (/index.html -> /), sirve index.html en cualquier ruta desconocida
// (fallback de SPA) y aplica las cabeceras del fichero _headers publicado (CSP incluida).
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

const tipos = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
    '.css': 'text/css', '.wasm': 'application/wasm', '.png': 'image/png', '.svg': 'image/svg+xml', '.ico': 'image/x-icon',
    '.woff2': 'font/woff2', '.woff': 'font/woff', '.webmanifest': 'application/manifest+json', '.txt': 'text/plain',
};

/** Lee _headers: una línea sin sangría es un patrón de ruta (admite * al final); las sangradas, «Nombre: valor». */
function leerCabeceras(raiz) {
    const fichero = path.join(raiz, '_headers');
    if (!existsSync(fichero)) return [];
    const reglas = [];
    for (const linea of readFileSync(fichero, 'utf8').split('\n')) {
        if (!linea.trim() || linea.trim().startsWith('#')) continue;
        if (!/^\s/.test(linea)) { reglas.push({ patron: linea.trim(), cabeceras: [] }); continue; }
        const i = linea.indexOf(':');
        if (reglas.length && i > 0) reglas.at(-1).cabeceras.push([linea.slice(0, i).trim(), linea.slice(i + 1).trim()]);
    }
    return reglas;
}

function coincide(patron, ruta) {
    return patron.endsWith('*') ? ruta.startsWith(patron.slice(0, -1)) : ruta === patron;
}

/** Arranca el servidor sobre «raiz» y devuelve el servidor y su URL base. */
export async function arrancar(raiz, puerto = 0) {
    const reglas = leerCabeceras(raiz);
    const servidor = createServer(async (peticion, respuesta) => {
        const ruta = decodeURIComponent(new URL(peticion.url, 'http://x').pathname);
        // Pages: /index.html -> /, /a/index.html -> /a/, /b.html -> /b.
        if (ruta.endsWith('.html')) {
            const destino = ruta.endsWith('/index.html') ? ruta.slice(0, -'index.html'.length) : ruta.slice(0, -'.html'.length);
            respuesta.writeHead(308, { Location: destino });
            return respuesta.end();
        }
        let fichero = path.join(raiz, ruta.endsWith('/') ? path.join(ruta, 'index.html') : ruta);
        if (!fichero.startsWith(raiz) || !(await stat(fichero).then(s => s.isFile(), () => false))) fichero = path.join(raiz, 'index.html');
        const cabeceras = { 'Content-Type': tipos[path.extname(fichero)] ?? 'application/octet-stream' };
        for (const regla of reglas) if (coincide(regla.patron, ruta)) for (const [n, v] of regla.cabeceras) cabeceras[n] = v;
        respuesta.writeHead(200, cabeceras);
        respuesta.end(await readFile(fichero));
    });
    await new Promise(r => servidor.listen(puerto, '127.0.0.1', r));
    return { servidor, url: `http://localhost:${servidor.address().port}` };
}

// Uso directo para probar a mano: node tests/pwa/servidor.mjs <carpeta wwwroot publicada> [puerto]
if (import.meta.url === `file://${process.argv[1]}`) {
    const { url } = await arrancar(path.resolve(process.argv[2] ?? '.'), Number(process.argv[3] ?? 8080));
    console.log(`Sirviendo como Cloudflare Pages en ${url}`);
}
