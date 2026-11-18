// Intersection Observer for reveal animations
const revealElements = document.querySelectorAll('.reveal');

const revealObserver = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.classList.add('active');
        }
    });
}, {
    threshold: 0.1
});

revealElements.forEach(el => revealObserver.observe(el));

// Sticky Header
const header = document.querySelector('header');
if (header) {
    window.addEventListener('scroll', () => {
        if (window.scrollY > 50) {
            header.style.padding = '0.8rem 2rem';
            header.style.background = 'rgba(255, 255, 255, 0.95)';
            header.style.boxShadow = '0 10px 30px rgba(0,0,0,0.05)';
        } else {
            header.style.padding = '1.5rem 2rem';
            header.style.background = 'rgba(255, 255, 255, 0.8)';
            header.style.boxShadow = 'none';
        }
    });
}

// Origen de la visita (primer contacto, 90 días): utm del link y sitio desde el que llegó. Va con la consulta
// para saber qué publicación o canal trae clientes. No se guarda nada personal.
const ORIGEN_KEY = 'deeparq.origen';
function leerOrigen() {
    try {
        const o = JSON.parse(localStorage.getItem(ORIGEN_KEY) || 'null');
        return o && Date.now() - o.t < 90 * 864e5 ? o : null;
    } catch { return null; }
}
(() => {
    const p = new URLSearchParams(location.search);
    const ref = document.referrer && !document.referrer.startsWith(location.origin) ? document.referrer : '';
    const conUtm = p.has('utm_source') || p.has('utm_campaign');
    // Un link con utm pisa al anterior (nueva campaña); una visita directa no borra el origen guardado.
    if (!conUtm && leerOrigen()) return;
    try {
        localStorage.setItem(ORIGEN_KEY, JSON.stringify({
            t: Date.now(),
            fuente: p.get('utm_source') || (p.has('fbclid') ? 'facebook' : null),
            medio: p.get('utm_medium'),
            campania: p.get('utm_campaign'),
            referente: ref || null,
            pagina: location.pathname
        }));
    } catch { /* modo privado: sin origen */ }
})();

// Contact form: guarda la consulta en el panel del estudio y abre WhatsApp (comportamiento original).
const contactForm = document.querySelector('.contact-form');
if (contactForm) {
    contactForm.addEventListener('submit', (e) => {
        e.preventDefault();

        const name = contactForm.querySelector('[name="nombre"]').value.trim();
        const email = contactForm.querySelector('[name="email"]').value.trim();
        const message = contactForm.querySelector('[name="mensaje"]').value.trim();
        const honeypot = contactForm.querySelector('[name="sitio"]')?.value ?? '';

        // keepalive: el POST se completa aunque el foco pase a la pestaña de WhatsApp.
        // No se espera la respuesta para que el navegador no bloquee el window.open como popup.
        fetch('/api/consultas', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                nombre: name,
                email: email,
                mensaje: message,
                area: contactForm.dataset.area || 'General',
                sitio: honeypot,
                origen: leerOrigen()
            }),
            keepalive: true
        }).catch(() => { /* si falla, igual queda el WhatsApp */ });

        const whatsappNumber = contactForm.dataset.whatsapp || '5493543618202';
        const text = `Hola Gaston! Mi nombre es ${name}. Mi correo es ${email}. Te escribo por lo siguiente: ${message}`;
        window.open(`https://wa.me/${whatsappNumber}?text=${encodeURIComponent(text)}`, '_blank');

        contactForm.reset();
    });
}
