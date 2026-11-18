// Con sesión guardada, index.html muestra solo la pantalla de carga en lugar de la portada pública (que es para
// buscadores y visitas nuevas). Va en <head> para decidirlo antes de pintar. Clave: ServicioSesion.ClaveAlmacen.
try { if (localStorage.getItem('miparte.sesion')) document.documentElement.classList.add('con-sesion'); } catch (e) { }
