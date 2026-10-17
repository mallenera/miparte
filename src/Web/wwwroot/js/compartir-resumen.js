// Compartir el resumen de liquidación: copiar al portapapeles y Web Share. Va en un fichero aparte porque la CSP no admite scripts en línea.
window.compartirResumen = {
    // Web Share solo existe en algunos navegadores (sobre todo móviles); si no, el botón se oculta.
    puedeCompartir: function () {
        return typeof navigator.share === 'function';
    },

    // Si el portapapeles no está disponible o falla, deja seleccionado el texto del área indicada para que la persona lo copie a mano.
    copiar: async function (idArea, texto) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(texto);
                return true;
            }
        } catch (e) {
            // Sin permiso del navegador: se usa el plan B de abajo.
        }

        const area = document.getElementById(idArea);
        if (area) {
            area.focus();
            area.select();
            area.setSelectionRange(0, area.value.length);
        }
        return false;
    },

    // Devuelve false si la persona cierra la hoja de compartir o el navegador no deja compartir.
    compartir: async function (titulo, texto) {
        try {
            await navigator.share({ title: titulo, text: texto });
            return true;
        } catch (e) {
            return false;
        }
    }
};
