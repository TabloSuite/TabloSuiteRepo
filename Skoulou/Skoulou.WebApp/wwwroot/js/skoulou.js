// SKOULOU – kleine Hilfsfunktionen für Blazor (ohne Bibliotheken).

// Popups (Auswahl, Kalender, Benutzermenü) schließen bei Klick außerhalb ihres Elements [data-sk-closer="id"].
(function () {
    var offen = {};
    document.addEventListener('click', function (e) {
        Object.keys(offen).forEach(function (id) {
            var el = document.querySelector('[data-sk-closer="' + id + '"]');
            if (!el) { delete offen[id]; return; }
            if (!el.contains(e.target)) offen[id].invokeMethodAsync('SchliesseVonAussen').catch(function () { });
        });
    }, true);
    window.skCloserRegistrieren = function (ref, id) { offen[id] = ref; };
    window.skCloserEntfernen = function (id) { delete offen[id]; };
})();

// Formular nach der Prüfung in Blazor klassisch absenden (Cookie-Änderungen brauchen eine HTTP-Antwort).
window.skFormularSenden = function (id) {
    var form = document.getElementById(id);
    if (form) form.submit();
};

// Ladeanzeige für klassische Formulare: <form data-ladeanzeige="Texte…">. Ein globaler Handler,
// damit es auch nach einer Blazor-Navigation funktioniert.
(function () {
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || !form.hasAttribute || !form.hasAttribute('data-ladeanzeige') || e.defaultPrevented) return;
        if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;
        var knopf = e.submitter || form.querySelector('[type="submit"]');
        if (!knopf || knopf.classList.contains('sk-laedt')) return;
        knopf.classList.add('sk-laedt');
        knopf.setAttribute('aria-busy', 'true');
        knopf.dataset.skInhalt = knopf.innerHTML;
        var spinner = document.createElement('span');
        spinner.className = 'sk-spinner';
        var text = document.createElement('span');
        text.textContent = form.getAttribute('data-ladeanzeige') || knopf.textContent.trim();
        knopf.innerHTML = '';
        knopf.appendChild(spinner);
        knopf.appendChild(text);
        setTimeout(function () { knopf.disabled = true; }, 0);
    }, true);

    // « Zurück » am Handy (bfcache) zeigt sonst einen dauerhaft gesperrten Knopf.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('.sk-laedt').forEach(function (knopf) {
            knopf.classList.remove('sk-laedt');
            knopf.removeAttribute('aria-busy');
            knopf.disabled = false;
            if (knopf.dataset.skInhalt !== undefined) { knopf.innerHTML = knopf.dataset.skInhalt; delete knopf.dataset.skInhalt; }
        });
    });
})();
