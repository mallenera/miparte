// Aplica el tema guardado antes de pintar (sin parpadeo). "sistema" = sin atributo. Clave: ServicioTema.Clave.
window.miparteTema = {
    aplicar: function (id) {
        var r = document.documentElement;
        // Solo temas conocidos (los de ServicioTema.Todos); cualquier otro valor equivale a "sistema".
        if (['claro', 'oscuro', 'oceano', 'bosque', 'medianoche', 'grafito'].indexOf(id) < 0) r.removeAttribute('data-tema'); else r.setAttribute('data-tema', id);
        var estilo = getComputedStyle(r);
        var meta = document.querySelector('meta[name="theme-color"]');
        var color = (estilo.colorScheme.indexOf('dark') >= 0 ? estilo.getPropertyValue('--bg') : estilo.getPropertyValue('--wine')).trim();
        if (meta && color) meta.setAttribute('content', color);
    }
};
try { window.miparteTema.aplicar(localStorage.getItem('miparte.tema')); } catch (e) { }
