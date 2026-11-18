// App del cliente: registra el service worker, ofrece instalarla y recuerda el nombre para los formularios.
(() => {
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/mi-obra/sw.js', { scope: '/mi-obra/' }).catch(() => { });
  }

  // Botón «Instalar» (Android / Chrome) e instrucciones para iPhone.
  const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  let pedido = null;
  addEventListener('beforeinstallprompt', e => {
    e.preventDefault();
    pedido = e;
    const caja = document.getElementById('instalar');
    if (caja) caja.hidden = false;
  });
  document.addEventListener('DOMContentLoaded', () => {
    const boton = document.getElementById('instalar-boton');
    if (boton) boton.addEventListener('click', async () => {
      if (!pedido) return;
      pedido.prompt();
      await pedido.userChoice;
      pedido = null;
      document.getElementById('instalar').hidden = true;
    });
    const ios = /iphone|ipad|ipod/i.test(navigator.userAgent);
    const cajaIos = document.getElementById('instalar-ios');
    if (cajaIos && ios && !standalone) cajaIos.hidden = false;

    // Nombre de quien escribe o responde (pueden usarla varias personas de la familia): queda en este teléfono.
    document.querySelectorAll('[data-recordar]').forEach(el => {
      const clave = 'mi-obra-' + el.dataset.recordar;
      if (!el.value) el.value = localStorage.getItem(clave) || '';
      el.form && el.form.addEventListener('submit', () => localStorage.setItem(clave, el.value.trim()));
    });

    // Evita el doble envío (tocar dos veces «Enviar» con poca señal).
    document.querySelectorAll('form').forEach(f => f.addEventListener('submit', e => {
      if (f.dataset.enviando) { e.preventDefault(); return; }
      f.dataset.enviando = '1';
      // Se deshabilitan después: el botón tocado (Apruebo / No apruebo) ya viajó con el formulario.
      setTimeout(() => f.querySelectorAll('button[type=submit]').forEach(b => b.disabled = true), 0);
    }));

    // Al cerrar sesión se borra lo guardado para ver sin señal.
    document.querySelectorAll('form[data-salir]').forEach(f => f.addEventListener('submit', () => {
      navigator.serviceWorker && navigator.serviceWorker.controller && navigator.serviceWorker.controller.postMessage('salir');
      localStorage.removeItem('mi-obra-nombre');
    }));

    // El chat abre mostrando lo último.
    const fin = document.getElementById('chat-fin');
    if (fin && !location.hash) fin.scrollIntoView({ block: 'end' });
  });
})();