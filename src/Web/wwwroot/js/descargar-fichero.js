// Descarga un texto generado en la app como fichero (p. ej. el CSV de movimientos). Va en un fichero aparte porque la CSP no admite scripts en línea.
window.descargarFichero = function (nombre, contenido, tipo) {
    const url = URL.createObjectURL(new Blob([contenido], { type: tipo }));
    const enlace = document.createElement('a');
    enlace.href = url;
    enlace.download = nombre;
    document.body.appendChild(enlace);
    enlace.click();
    enlace.remove();
    // Se libera después del clic: algunos navegadores leen la URL de forma asíncrona.
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
};
