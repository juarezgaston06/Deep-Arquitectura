// Ayudas del panel que necesitan el navegador.

// Ubicación del celular (relevamiento en el terreno). Devuelve [lat, lon] o null.
window.deepUbicacion = () => new Promise(resolve => {
    if (!navigator.geolocation) return resolve(null);
    navigator.geolocation.getCurrentPosition(
        p => resolve([p.coords.latitude, p.coords.longitude]),
        () => resolve(null),
        { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 });
});
