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

// Ladeanzeige für klassische Formulare (Anmelden, Abmelden …): <form data-ladeanzeige="Texte…">.
// Ein einziger, global registrierter Handler – Inline-Skripte in Seiten laufen nach einer
// Blazor-Navigation (z. B. Startseite → Connexion auf dem Handy) nicht und fehlten deshalb.
(function () {
    if (window.__enoteLadeanzeige) return;
    window.__enoteLadeanzeige = true;

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || !form.hasAttribute || !form.hasAttribute('data-ladeanzeige') || e.defaultPrevented) return;
        if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;
        var btn = e.submitter || form.querySelector('[type="submit"]');
        if (!btn || btn.classList.contains('enote-laedt')) return;

        btn.classList.add('enote-laedt');
        btn.setAttribute('aria-busy', 'true');
        btn.dataset.enoteInhalt = btn.innerHTML;
        var spinner = document.createElement('span');
        spinner.className = 'enote-spinner';
        spinner.setAttribute('aria-hidden', 'true');
        var text = document.createElement('span');
        text.textContent = form.getAttribute('data-ladeanzeige') || btn.textContent.trim();
        btn.innerHTML = '';
        btn.appendChild(spinner);
        btn.appendChild(text);
        // Erst nach dem Absenden sperren, sonst verwerfen manche Browser den Submit.
        setTimeout(function () { btn.disabled = true; }, 0);
    }, true);

    // « Zurück » auf dem Handy (bfcache) zeigt sonst einen dauerhaft drehenden, gesperrten Knopf.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('.enote-laedt').forEach(function (btn) {
            btn.classList.remove('enote-laedt');
            btn.removeAttribute('aria-busy');
            btn.disabled = false;
            if (btn.dataset.enoteInhalt !== undefined) { btn.innerHTML = btn.dataset.enoteInhalt; delete btn.dataset.enoteInhalt; }
        });
    });
})();

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

// ---------- Startseite: sanftes Einblenden beim Scrollen ----------
// Elemente mit .lp-reveal erscheinen, sobald sie ins Bild kommen. Neue Elemente (Blazor-Render)
// werden automatisch erfasst. Bei „Bewegung reduzieren“ ist alles sofort sichtbar.
(function () {
    var ruhig = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
    var beobachter = null;
    function zeige(el) { el.classList.add('sichtbar'); }
    function erfasse(wurzel) {
        var liste = (wurzel || document).querySelectorAll ? (wurzel || document).querySelectorAll('.lp-reveal:not(.sichtbar)') : [];
        for (var i = 0; i < liste.length; i++) {
            if (ruhig || !beobachter) zeige(liste[i]); else beobachter.observe(liste[i]);
        }
    }
    if (!ruhig && 'IntersectionObserver' in window) {
        beobachter = new IntersectionObserver(function (eintraege) {
            eintraege.forEach(function (e) { if (e.isIntersecting) { zeige(e.target); beobachter.unobserve(e.target); } });
        }, { rootMargin: '0px 0px -8% 0px', threshold: 0.12 });
        document.documentElement.classList.add('lp-js');
    }
    window.enoteStartseite = function () { erfasse(document); };
    new MutationObserver(function () { erfasse(document); })
        .observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', function () { erfasse(document); });
    // Sicherheitsnetz: nach 4 s ist alles sichtbar, auch wenn ein Beobachter nicht feuert.
    setTimeout(function () { document.querySelectorAll('.lp-reveal:not(.sichtbar)').forEach(function (el) {
        var r = el.getBoundingClientRect(); if (r.top < window.innerHeight) zeige(el); }); }, 4000);
})();


// Barcode-Scanner (USB/Bluetooth im Tastatur-Modus): tippt den Code in wenigen Millisekunden und drückt Enter.
// In der Kasse wird diese Folge überall erkannt – auch wenn gerade eine Kachel den Fokus hat. Das Enter des
// Scanners wird abgefangen, damit es keine zuletzt angetippte Kachel erneut auslöst. Normales Tippen in
// Eingabefeldern (PIN, Beträge, Suche) bleibt unberührt.
window.enoteScanner = (function () {
    var ref = null, puffer = '', letzte = 0, abstaende = [];
    var MAX_ABSTAND = 60, MIN_LAENGE = 4;
    function istEingabe(el) {
        if (!el) return false;
        var t = (el.tagName || '').toUpperCase();
        return t === 'INPUT' || t === 'TEXTAREA' || t === 'SELECT' || el.isContentEditable;
    }
    function zuruecksetzen() { puffer = ''; abstaende = []; }
    function handler(e) {
        if (!ref) return;
        if (istEingabe(document.activeElement)) { zuruecksetzen(); return; }
        var jetzt = performance.now();
        if (e.key === 'Enter') {
            var schnell = abstaende.length >= MIN_LAENGE - 1 && abstaende.every(function (a) { return a < MAX_ABSTAND; });
            if (puffer.length >= MIN_LAENGE && schnell) {
                e.preventDefault(); e.stopPropagation();
                // In einem offenen Dialog (Zahlung, Abschluss …) nichts buchen
                if (!document.querySelector('.enote-modal-backdrop')) ref.invokeMethodAsync('ScanErkannt', puffer);
            }
            zuruecksetzen();
            return;
        }
        if (e.key && e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
            if (puffer && jetzt - letzte > MAX_ABSTAND) zuruecksetzen();
            if (puffer) abstaende.push(jetzt - letzte);
            puffer += e.key; letzte = jetzt;
            if (puffer.length >= 2) e.preventDefault(); // z. B. Leertaste löst keine Kachel aus
        }
    }
    return {
        start: function (dotNetRef) { ref = dotNetRef; zuruecksetzen(); document.addEventListener('keydown', handler, true); },
        stop: function () { ref = null; document.removeEventListener('keydown', handler, true); }
    };
})();
