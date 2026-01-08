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

// Mobile Menu (Simple toggle for now)
const mobileMenuBtn = document.getElementById('mobile-menu');
if (mobileMenuBtn) {
    mobileMenuBtn.addEventListener('click', () => {
        // Implement menu toggle logic if needed
        console.log('Mobile menu clicked');
    });
}

// Form submission (Prevent default)
const contactForm = document.querySelector('.contact-form');
if (contactForm) {
    contactForm.addEventListener('submit', (e) => {
        e.preventDefault();

        // Get form values
        const name = contactForm.querySelector('input[type="text"]').value;
        const email = contactForm.querySelector('input[type="email"]').value;
        const message = contactForm.querySelector('textarea').value;

        // Construct WhatsApp message
        const whatsappNumber = "5493543618202";
        const text = `Hola Gaston! Mi nombre es ${name}. Mi correo es ${email}. Te escribo por lo siguiente: ${message}`;
        const encodedText = encodeURIComponent(text);

        // Redirect to WhatsApp
        window.open(`https://wa.me/${whatsappNumber}?text=${encodedText}`, '_blank');

        contactForm.reset();
    });
}
