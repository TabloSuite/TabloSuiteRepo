// Passwort-Sichtbarkeit umschalten (Auge-Button). Funktioniert auch in
// statisch gerenderten Seiten ohne Blazor-Interaktivität.
window.enoteTogglePassword = function (button) {
    var wrap = button.closest('.enote-pw-wrap');
    if (!wrap) return;
    var input = wrap.querySelector('input');
    if (!input) return;

    var istText = input.type === 'text';
    input.type = istText ? 'password' : 'text';
    button.textContent = istText ? '👁' : '🙈';
    button.setAttribute('aria-label', istText ? 'Afficher' : 'Masquer');
};

// User-Menü (Avatar-Dropdown) schließt bei Klick außerhalb.
window.enoteOeffneTab = function (url) {
    window.open(url, '_blank');
};

window.enoteRegisterUserMenu = function (dotNetRef) {
    if (window.__enoteUserMenuHandler) return; // nur einmal registrieren
    window.__enoteUserMenuHandler = function (e) {
        var menu = document.querySelector('.enote-usermenu');
        if (menu && !menu.contains(e.target)) {
            dotNetRef.invokeMethodAsync('SchliesseUserMenu');
        }
    };
    document.addEventListener('click', window.__enoteUserMenuHandler);
};

// Generischer Popup-Closer: registriert ein offenes Element (datepicker/select2).
// Bei Klick außerhalb wird das .NET-Objekt benachrichtigt.
window.enoteRegisterElementCloser = function (dotNetRef, cssKlasse) {
    // Pro Instanz ein Handler; wir merken sie in einer Map.
    window.__enoteClosers = window.__enoteClosers || [];
    window.__enoteClosers.push({ dotNetRef: dotNetRef, cssKlasse: cssKlasse });

    if (window.__enoteCloserGlobalHandler) return;
    window.__enoteCloserGlobalHandler = function (e) {
        (window.__enoteClosers || []).forEach(function (c) {
            var el = document.querySelector('.enote-' + c.cssKlasse + '.offen');
            if (el && !el.contains(e.target)) {
                c.dotNetRef.invokeMethodAsync('SchliesseVonAussen');
            }
        });
    };
    document.addEventListener('click', window.__enoteCloserGlobalHandler);
};

// Schneidet ein Bild quadratisch zu (Mitte) und skaliert es. Gibt eine JPEG-Data-URL zurück.
// Wird vor dem Upload aufgerufen, damit Kacheln in der Kasse einheitlich aussehen.
window.enoteSchneideBildZu = function (dataUrl, groesse) {
    return new Promise(function (resolve, reject) {
        var img = new Image();
        img.onload = function () {
            try {
                var seite = Math.min(img.width, img.height);
                var sx = (img.width - seite) / 2;
                var sy = (img.height - seite) / 2;

                var canvas = document.createElement('canvas');
                canvas.width = groesse;
                canvas.height = groesse;
                var ctx = canvas.getContext('2d');
                ctx.drawImage(img, sx, sy, seite, seite, 0, 0, groesse, groesse);
                resolve(canvas.toDataURL('image/jpeg', 0.85));
            } catch (err) {
                reject(err);
            }
        };
        img.onerror = reject;
        img.src = dataUrl;
    });
};

// Liest eine Datei als Data-URL (für die Vorschau vor dem Zuschneiden).
window.enoteLeseDateiAlsDataUrl = function (inputElement) {
    return new Promise(function (resolve, reject) {
        var datei = inputElement && inputElement.files && inputElement.files[0];
        if (!datei) { resolve(null); return; }
        var reader = new FileReader();
        reader.onload = function (e) { resolve(e.target.result); };
        reader.onerror = reject;
        reader.readAsDataURL(datei);
    });
};
