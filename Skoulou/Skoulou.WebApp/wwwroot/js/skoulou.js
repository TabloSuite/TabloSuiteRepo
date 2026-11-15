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

// Ladeanzeige für klassische Formulare: <form data-ladeanzeige="Texte…">. Ein globaler Handler, damit es auch
// nach einer Blazor-Navigation funktioniert. Der Ladekreis erscheint sofort beim Tippen (ohne Umweg über den
// Server) und bleibt bis zur neuen Seite. Angezeigt wird er nur über das Attribut data-sk-laedt (CSS) – der
// Inhalt des Knopfs bleibt unberührt, weil Blazor ihn verwaltet.
(function () {
    var NOTFALL_MS = 30000;

    // Der Absende-Knopf: der getippte, sonst der des Formulars (auch außerhalb, über form="id").
    function knopfVon(form, absender) {
        return absender || form.querySelector('[type="submit"]')
            || (form.id ? document.querySelector('[type="submit"][form="' + form.id + '"]') : null);
    }

    function sperren(form, absender) {
        var knopf = knopfVon(form, absender);
        form.dataset.skSendet = '1';
        if (knopf) {
            knopf.setAttribute('data-sk-laedt', form.getAttribute('data-ladeanzeige') || knopf.textContent.trim());
            knopf.setAttribute('aria-busy', 'true');
            form.skKnopf = knopf;
        }
        // Kommt keine Antwort (Verbindung weg), gibt sich der Knopf selbst wieder frei.
        clearTimeout(form.skNotfall);
        form.skNotfall = setTimeout(function () { freigeben(form); }, NOTFALL_MS);
    }

    function freigeben(form) {
        clearTimeout(form.skNotfall);
        delete form.dataset.skSendet;
        delete form.dataset.skGesendet;
        if (form.skKnopf) {
            form.skKnopf.removeAttribute('data-sk-laedt');
            form.skKnopf.removeAttribute('aria-busy');
            form.skKnopf = null;
        }
    }

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || !form.hasAttribute || !form.hasAttribute('data-ladeanzeige')) return;
        // Doppeltipp oder Entertaste während des Sendens: nichts ein zweites Mal absenden.
        if (form.dataset.skSendet === '1') { e.preventDefault(); e.stopImmediatePropagation(); return; }
        if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;
        sperren(form, e.submitter);
    }, true);

    // Formular nach der Prüfung in Blazor klassisch absenden (Cookie-Änderungen brauchen eine HTTP-Antwort).
    // Nur einmal: der Ladekreis bleibt stehen, bis die neue Seite da ist.
    window.skFormularSenden = function (id) {
        var form = document.getElementById(id);
        if (!form || form.dataset.skGesendet === '1') return;
        if (form.hasAttribute('data-ladeanzeige') && form.dataset.skSendet !== '1') sperren(form, null);
        form.dataset.skGesendet = '1';
        form.submit();
    };

    // Die Prüfung in Blazor hat einen Fehler gemeldet: Knopf wieder freigeben.
    window.skFormularFrei = function (id) {
        var form = document.getElementById(id);
        if (form && form.dataset.skGesendet !== '1') freigeben(form);
    };

    // « Zurück » am Handy (bfcache) zeigt sonst einen dauerhaft gesperrten Knopf.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('form[data-sk-sendet], form[data-sk-gesendet]').forEach(freigeben);
    });
})();

// Eingaben vor dem Start der Blazor-Verbindung behalten. Beim Start zeichnet Blazor die Seite interaktiv neu und
// entfernt die vorab gezeichnete: ohne das hier wäre bei langsamer Verbindung (Handy) schon Getipptes weg und das
// Feld verlöre den Fokus. Felder werden über ihren Namen, sonst über Beschriftung/Platzhalter wiedergefunden.
(function () {
    var AUSWAHL = 'input:not([type="hidden"]):not([type="file"]), textarea';
    var vorab = new WeakSet();      // Felder der vorab gezeichneten Seite
    var stand = [];                 // je Feld: altes Element, Schlüssel, Startwert, getippter und übertragener Wert
    var fokus = null;               // Eintrag des Felds, in dem gerade getippt wird
    var zurueck = null;             // nach dem Start wiederhergestellter Fokus (für kurze Zeit geschützt)

    function istHaken(feld) { return feld.type === 'checkbox' || feld.type === 'radio'; }

    function schluessel(feld) {
        if (feld.name) return 'n:' + feld.name;
        var label = feld.id ? document.querySelector('label[for="' + feld.id + '"]') : null;
        var text = feld.getAttribute('aria-label') || feld.getAttribute('placeholder') || (label ? label.textContent.trim() : '');
        return text ? 't:' + feld.tagName + ':' + text : null;
    }

    document.querySelectorAll(AUSWAHL).forEach(function (feld) {
        vorab.add(feld);
        var key = schluessel(feld);
        if (key) stand.push({ alt: feld, key: key, start: istHaken(feld) ? feld.checked : feld.value, wert: undefined, gesetzt: undefined });
    });
    if (stand.length === 0 || typeof MutationObserver === 'undefined') return;

    function eintrag(feld) {
        for (var i = 0; i < stand.length; i++) if (stand[i].alt === feld) return stand[i];
        return null;
    }

    // Das Gegenstück in der interaktiven Seite – nur wenn es eindeutig ist.
    function neuesFeld(key) {
        var treffer = document.querySelectorAll(AUSWAHL), fund = null;
        for (var i = 0; i < treffer.length; i++) {
            if (vorab.has(treffer[i]) || schluessel(treffer[i]) !== key) continue;
            if (fund) return null;
            fund = treffer[i];
        }
        return fund;
    }

    function melde(feld, ereignis) { feld.dispatchEvent(new Event(ereignis, { bubbles: true })); }

    function uebertrage(s) {
        if (s.wert === undefined || s.wert === s.gesetzt) return;
        var feld = neuesFeld(s.key);
        if (!feld) return;
        if (istHaken(feld)) {
            if (feld.checked !== s.wert) { feld.checked = s.wert; melde(feld, 'change'); }
        } else {
            // Nicht überschreiben, was im neuen Feld schon selbst getippt wurde.
            if (feld.value !== s.start && feld.value !== s.gesetzt && feld.value !== '') return;
            feld.value = s.wert;
            melde(feld, 'input');
            melde(feld, 'change');
        }
        s.gesetzt = s.wert;
    }

    function wiederherstellen() {
        stand.forEach(uebertrage);
        if (fokus && !fokus.alt.isConnected) {
            var ziel = neuesFeld(fokus.key);
            if (!ziel) return;
            var aktiv = document.activeElement;
            if (!aktiv || aktiv === document.body || aktiv.tagName === 'H1') {
                ziel.focus();
                try { ziel.setSelectionRange(ziel.value.length, ziel.value.length); } catch (fehler) { /* E-Mail-Felder kennen keine Auswahl */ }
                zurueck = { feld: ziel, bis: Date.now() + 5000 };
            }
            fokus = null;
        }
    }

    // Feld mit « autofocus » oder schon angetippt, bevor dieses Skript lief.
    if (document.activeElement) fokus = eintrag(document.activeElement);

    function merke(e) {
        var s = e.target ? eintrag(e.target) : null;
        if (!s) return;
        s.wert = istHaken(s.alt) ? s.alt.checked : s.alt.value;
        wiederherstellen();
    }
    document.addEventListener('input', merke, true);
    document.addEventListener('change', merke, true);

    document.addEventListener('focusin', function (e) {
        var feld = e.target, s = feld ? eintrag(feld) : null;
        if (s) { fokus = s; return; }
        // Blazor setzt nach dem Start den Fokus auf die Überschrift – wer gerade tippt, bleibt in seinem Feld.
        if (zurueck && feld && feld.tagName === 'H1' && Date.now() < zurueck.bis && zurueck.feld.isConnected) zurueck.feld.focus();
    }, true);
    document.addEventListener('focusout', function (e) {
        var feld = e.target;
        // Echtes Verlassen (Feld steht noch in der Seite) – nicht das Entfernen beim Start der Verbindung.
        setTimeout(function () { if (fokus && fokus.alt === feld && feld.isConnected && document.activeElement !== feld) fokus = null; }, 0);
    }, true);

    var beobachter = new MutationObserver(wiederherstellen);
    beobachter.observe(document.body, { childList: true, subtree: true });
    // Die Verbindung steht spätestens nach dieser Zeit (sonst zeigt Blazor ohnehin sein Fenster « Reconnexion »).
    setTimeout(function () { beobachter.disconnect(); stand = []; fokus = null; }, 30000);
})();

// Fenster « Connexion interrompue » (VerbindungsFenster.razor): Blazor zeigt und verbirgt es über Klassen; hier nur die
// Knöpfe. Kennt der Server die Sitzung nicht mehr (z. B. nach einem Neustart), wird die Seite neu geladen.
(function () {
    var fenster = document.getElementById('components-reconnect-modal');
    if (!fenster) return;

    function neuLaden() { location.reload(); }

    function erneutVerbinden() {
        if (!window.Blazor || typeof Blazor.reconnect !== 'function') { neuLaden(); return; }
        Promise.resolve(Blazor.reconnect()).then(function (erfolg) {
            if (erfolg) return;
            // Server erreicht, Sitzung aber nicht mehr da: Sitzung fortsetzen oder neu laden.
            if (typeof Blazor.resumeCircuit !== 'function') { neuLaden(); return; }
            Promise.resolve(Blazor.resumeCircuit()).then(function (ok) { if (!ok) neuLaden(); }, neuLaden);
        }, function () { /* Server noch nicht erreichbar – das Fenster bleibt mit « Réessayer » stehen */ });
    }

    function fortsetzen() {
        if (!window.Blazor || typeof Blazor.resumeCircuit !== 'function') { neuLaden(); return; }
        Promise.resolve(Blazor.resumeCircuit()).then(function (ok) { if (!ok) neuLaden(); }, neuLaden);
    }

    fenster.addEventListener('click', function (e) {
        var knopf = e.target && e.target.closest ? e.target.closest('[data-sk-verbindung]') : null;
        if (!knopf) return;
        var was = knopf.getAttribute('data-sk-verbindung');
        if (was === 'reessayer') erneutVerbinden();
        else if (was === 'reprendre') fortsetzen();
        else neuLaden();
    });

    fenster.addEventListener('components-reconnect-state-changed', function (e) {
        var zustand = e.detail && e.detail.state;
        if (zustand === 'rejected') neuLaden();
        // Handy wieder entsperrt oder Reiter wieder sichtbar: sofort neu versuchen statt auf den Knopf zu warten.
        else if (zustand === 'failed') document.addEventListener('visibilitychange', beiSichtbar);
        else if (zustand === 'hide') document.removeEventListener('visibilitychange', beiSichtbar);
    });

    function beiSichtbar() { if (document.visibilityState === 'visible') erneutVerbinden(); }
})();
