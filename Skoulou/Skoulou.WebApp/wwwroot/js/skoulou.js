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
        // Zweiter Durchgang (unten über requestSubmit ausgelöst): jetzt wirklich senden.
        if (form.dataset.skGezeichnet === '1') { delete form.dataset.skGezeichnet; return; }
        // Doppeltipp oder Entertaste während des Sendens: nichts ein zweites Mal absenden.
        if (form.dataset.skSendet === '1') { e.preventDefault(); e.stopImmediatePropagation(); return; }
        if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;
        sperren(form, e.submitter);
        // Erst zeichnen, dann senden (Nutzer 19.11.2026: « Se connecter » zeigte nur am Handy keinen Ladekreis). Safari am
        // iPhone zeichnet nach dem Start des Absendens nichts mehr – der Kreis kam dort nie auf den Bildschirm.
        e.preventDefault();
        var absender = e.submitter && e.submitter.form === form ? e.submitter : null;
        var gesendet = false;
        function senden() {
            if (gesendet) return;
            gesendet = true;
            form.dataset.skGezeichnet = '1';
            if (typeof form.requestSubmit === 'function') form.requestSubmit(absender || undefined);
            else HTMLFormElement.prototype.submit.call(form);
            delete form.dataset.skGezeichnet;   // bei einem Prüffehler des Browsers kam kein zweiter Durchgang
        }
        requestAnimationFrame(function () { setTimeout(senden, 40); });
        setTimeout(senden, 400);   // falls kein Bild gezeichnet wird (Tab im Hintergrund)
    }, true);

    // Formular nach der Prüfung in Blazor klassisch absenden (Cookie-Änderungen brauchen eine HTTP-Antwort).
    // Nur einmal: der Ladekreis bleibt stehen, bis die neue Seite da ist.
    window.skFormularSenden = function (id) {
        var form = document.getElementById(id);
        if (!form || form.dataset.skGesendet === '1') return;
        if (form.hasAttribute('data-ladeanzeige') && form.dataset.skSendet !== '1') sperren(form, null);
        form.dataset.skGesendet = '1';
        if (window.skFormularMerken) window.skFormularMerken(form);   // form.submit() löst kein « submit » aus
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

// Eingaben klassischer Formulare <form data-sk-merken="schlüssel"> beim Absenden merken (21.11.2026, Registrierung bei
// langsamem Netz): lehnt der Server ab und leitet mit « ?erreur= » zurück, setzt die Seite sie wieder ein – sofort in die
// vorab gezeichneten Felder (der Block oben trägt sie in die interaktive Seite), Auswahl und Datum setzt Blazor selbst.
// Nie gemerkt: Passwörter, Antiforgery-Token, Honigtopf. Nur sessionStorage (Reiter); nach « ?envoye= » gelöscht.
(function () {
    var PRAEFIX = 'sk-formular:';
    var NIE = { motdepasse: 1, confirmation: 1, site: 1, __RequestVerificationToken: 1 };

    function lesen(schluessel) {
        try { var t = sessionStorage.getItem(PRAEFIX + schluessel); return t ? JSON.parse(t) : null; } catch (fehler) { return null; }
    }

    window.skFormularMerken = function (form) {
        var schluessel = form && form.getAttribute ? form.getAttribute('data-sk-merken') : null;
        if (!schluessel) return;
        var werte = {};
        Array.prototype.forEach.call(form.elements, function (f) {
            if (!f.name || NIE[f.name] || f.type === 'password' || f.type === 'file' || f.type === 'submit' || f.type === 'button') return;
            if (f.type === 'checkbox' || f.type === 'radio') { if (f.checked) werte[f.name] = f.value; return; }
            if (f.value) werte[f.name] = f.value;
        });
        try { sessionStorage.setItem(PRAEFIX + schluessel, JSON.stringify(werte)); } catch (fehler) { /* privat/voll: dann eben nicht */ }
    };
    window.skFormularGemerkt = lesen;
    window.skFormularVergessen = function (schluessel) {
        try { sessionStorage.removeItem(PRAEFIX + schluessel); } catch (fehler) { /* ohne Speicher nichts zu tun */ }
    };

    document.addEventListener('submit', function (e) { window.skFormularMerken(e.target); }, true);

    var adresse = new URLSearchParams(location.search);
    if (adresse.has('envoye')) {
        try {
            Object.keys(sessionStorage).forEach(function (k) { if (k.indexOf(PRAEFIX) === 0) sessionStorage.removeItem(k); });
        } catch (fehler) { /* ohne Speicher nichts zu tun */ }
        return;
    }
    if (!adresse.has('erreur')) return;
    document.querySelectorAll('form[data-sk-merken]').forEach(function (form) {
        var werte = lesen(form.getAttribute('data-sk-merken'));
        if (!werte) return;
        if (werte.type && form.elements.type && form.elements.type.value !== werte.type) return;
        Array.prototype.forEach.call(form.elements, function (f) {
            if (!f.name || !Object.prototype.hasOwnProperty.call(werte, f.name) || NIE[f.name] || f.type === 'hidden' || f.type === 'password') return;
            if (f.type === 'checkbox') {
                if (!f.checked && adresse.get('erreur') !== 'agb') { f.checked = true; f.dispatchEvent(new Event('change', { bubbles: true })); }
                return;
            }
            if (f.value && f.value !== f.defaultValue) return;
            f.value = werte[f.name];
            f.dispatchEvent(new Event('input', { bubbles: true }));
        });
    });
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

// Aufklapplisten und Kalender bleiben im Bildschirm (Handy): ragt ein Fenster rechts hinaus, wird es nach links
// geschoben – sonst verbreitert es die ganze Seite und seine rechte Hälfte ist nicht erreichbar.
(function () {
    if (typeof MutationObserver === 'undefined' || !document.body) return;
    var AUSWAHL = '.sk-auswahl-popup, .sk-datum-popup', RAND = 8;

    function einpassen(fenster) {
        var breite = document.documentElement.clientWidth;
        var r = fenster.getBoundingClientRect();
        var ueber = r.right - (breite - RAND);
        if (ueber > 0) fenster.style.left = -Math.min(ueber, Math.max(0, r.left - RAND)) + 'px';
    }

    new MutationObserver(function (aenderungen) {
        aenderungen.forEach(function (a) {
            for (var i = 0; i < a.addedNodes.length; i++) {
                var knoten = a.addedNodes[i];
                if (knoten.nodeType !== 1) continue;
                if (knoten.matches(AUSWAHL)) einpassen(knoten);
                else Array.prototype.forEach.call(knoten.querySelectorAll(AUSWAHL), einpassen);
            }
        });
    }).observe(document.body, { childList: true, subtree: true });
})();

// « Vidéos d'aide »: Position der Seite merken, solange ein Video offen ist. Am Handy dreht das Vollbild ins Querformat;
// dabei ordnet sich die Seite um und stünde nach dem Schließen ganz oben statt beim angesehenen Video.
// Fehler 16.11. (am Handy): « beim Herunterrollen springt die Seite wieder nach oben ». Jedes Ein-/Ausblenden der Adressleiste
// ist ein « resize » – blieb das Horchen an (Dialog ohne « Fermer » verlassen, z. B. Zurück-Taste) oder rollte der Nutzer in den
// 2,5 s nach dem Schließen, setzte jede Bewegung die Seite zurück. Darum: nur solange der Spieler da ist bzw. kurz danach, und
// sobald der Nutzer die Seite nach dem Schließen berührt, gehört die Position ihm.
window.skVideoPosition = (function () {
    var SPIELER = '.sk-video-spieler';
    var gemerkt = null, timer = null, aktiv = false, lauf = 0, schliessend = false;
    function rollend() {
        return [document.scrollingElement, document.querySelector('.sk-main')].filter(function (e) { return !!e; });
    }
    function setzen() {
        if (!gemerkt) return;
        var html = document.documentElement, vorher = html.style.scrollBehavior;
        html.style.scrollBehavior = 'auto';   // « smooth » würde sichtbar von oben herunterfahren
        rollend().forEach(function (e, i) {
            if (typeof gemerkt[i] === 'number' && Math.abs(e.scrollTop - gemerkt[i]) > 2) e.scrollTop = gemerkt[i];
        });
        html.style.scrollBehavior = vorher;
    }
    function aus() { horchen(false); clearTimeout(timer); gemerkt = null; schliessend = false; }
    function spaeter() {
        if (!schliessend && !document.querySelector(SPIELER)) { aus(); return; }   // Video ist weg, ohne dass zurueck() kam
        clearTimeout(timer); timer = setTimeout(setzen, 200);
    }
    function nutzer() { if (schliessend) aus(); }
    function horchen(an) {
        if (an === aktiv) return;
        aktiv = an;
        var f = an ? 'addEventListener' : 'removeEventListener';
        window[f]('resize', spaeter);
        window[f]('orientationchange', spaeter);
        document[f]('fullscreenchange', spaeter);
        document[f]('webkitfullscreenchange', spaeter);
        ['touchstart', 'wheel', 'pointerdown', 'keydown'].forEach(function (t) { window[f](t, nutzer, { capture: true, passive: true }); });
    }
    return {
        merken: function () { lauf++; schliessend = false; gemerkt = rollend().map(function (e) { return e.scrollTop; }); horchen(true); },
        zurueck: function () {
            var meiner = lauf;   // wird gleich das nächste Video geöffnet, darf dieses Aufräumen dessen Stand nicht löschen
            schliessend = true;
            setzen();
            // Das Zurückdrehen ins Hochformat kann erst nach dem Schließen fertig sein: noch kurz weiter horchen.
            [150, 500, 1200].forEach(function (ms) { setTimeout(function () { if (meiner === lauf) setzen(); }, ms); });
            setTimeout(function () { if (meiner === lauf) aus(); }, 2500);
        }
    };
})();

// Klicks vor dem Start der Blazor-Verbindung nachholen (Klicktest 21.11.2026: bei 700 ms Verzögerung ging der Klick auf
// « Encaisser un paiement » verloren – der Ladekreis lief, der Dialog kam nie). Ein Klick auf einen Knopf der vorab
// gezeichneten Seite wird gemerkt; sobald Blazor die Seite interaktiv neu zeichnet (der alte Knopf verschwindet), wird
// das eindeutige Gegenstück geklickt. Links und klassische Formulare arbeiten ohnehin ohne Blazor.
(function () {
    if (typeof MutationObserver === 'undefined' || window.__skKlickNachholen) return;
    window.__skKlickNachholen = true;
    var ZIELE = 'button, [role="button"], [role="tab"]', MAX_MS = 15000;
    var vorab = new WeakSet();
    document.querySelectorAll(ZIELE).forEach(function (el) { vorab.add(el); });
    var gemerkt = null, beobachter = null;
    function kennung(el) { return el.tagName + '|' + (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim(); }
    document.addEventListener('click', function (e) {
        var el = e.target && e.target.closest ? e.target.closest(ZIELE) : null;
        if (!el || !vorab.has(el) || el.disabled) return;
        var form = el.form || el.closest('form');
        if (form && (el.type === 'submit' || !el.type) && form.getAttribute('action')) return;   // klassisches Formular sendet selbst
        gemerkt = { el: el, kennung: kennung(el), zeit: Date.now() };
        if (beobachter) return;
        beobachter = new MutationObserver(function () {
            if (!gemerkt || document.body.contains(gemerkt.el)) return;   // noch die vorab gezeichnete Seite
            var g = gemerkt; gemerkt = null; beobachter.disconnect(); beobachter = null;
            if (Date.now() - g.zeit > MAX_MS) return;
            // Die interaktive Seite entsteht oft erst in einer späteren Änderung: kurz nach dem Gegenstück suchen.
            var versuche = 0;
            (function suche() {
                var treffer = document.querySelectorAll(ZIELE), fund = null, doppelt = false;
                for (var i = 0; i < treffer.length; i++) {
                    if (vorab.has(treffer[i]) || kennung(treffer[i]) !== g.kennung) continue;
                    if (fund) { doppelt = true; break; }
                    fund = treffer[i];
                }
                if (doppelt) return;   // nicht eindeutig: lieber nichts auslösen
                if (fund && !fund.disabled) { fund.click(); return; }
                if (++versuche < 30) setTimeout(suche, 100);
            })();
        });
        beobachter.observe(document.body, { childList: true, subtree: true });
    }, true);
})();

// Sofortige Rückmeldung für alle Knöpfe der interaktiven Seiten (16.11.2026): der Ladekreis erscheint beim Tippen, ohne
// auf die Antwort des Servers zu warten – bei langsamer Verbindung verging sonst spürbar Zeit, in der nichts geschah
// (der Kreis des Servers kommt erst mit seiner Antwort). Er verschwindet mit der ersten Änderung der Seite nach dem
// Tippen (= Antwort des Servers), spätestens nach einigen Sekunden. Nur über das Attribut data-sk-laedt (CSS) – der
// Inhalt des Knopfs bleibt unberührt, weil Blazor ihn verwaltet. Ausnahme je Knopf: data-ohne-ladekreis.
(function () {
    if (window.__skKlickRing) return;
    window.__skKlickRing = true;
    var VERZUG_MS = 120, NOTFALL_MS = 6000, wartend = null, timer = null, notfall = null, beobachter = null;

    function frei() {
        clearTimeout(timer); clearTimeout(notfall);
        if (beobachter) { beobachter.disconnect(); beobachter = null; }
        if (wartend) {
            if (wartend.hasAttribute('data-sk-klick')) { wartend.removeAttribute('data-sk-laedt'); wartend.removeAttribute('data-sk-klick'); }
            wartend = null;
        }
    }

    function passt(knopf) {
        if (!knopf || knopf.disabled || knopf.hasAttribute('data-sk-laedt') || knopf.hasAttribute('data-ohne-ladekreis')) return false;
        if (knopf.hasAttribute('onclick')) return false;   // reine Browser-Aktion (z. B. Drucken): keine Antwort des Servers
        if (knopf.tagName === 'A') {
            var ziel = knopf.getAttribute('href') || '';
            if (!ziel || ziel.charAt(0) === '#' || knopf.target || knopf.hasAttribute('download') || /\/pdf(\?|$)|\.pdf(\?|$)|\/export/.test(ziel)) return false;
        }
        var form = knopf.form || knopf.closest('form');
        if (form && form.hasAttribute('data-ladeanzeige')) return false;   // klassische Formulare: eigene Ladeanzeige (oben)
        return true;
    }

    document.addEventListener('click', function (e) {
        var knopf = e.target && e.target.closest ? e.target.closest('.sk-btn') : null;
        if (!passt(knopf)) return;
        frei();
        wartend = knopf;
        // Jede Änderung der Seite nach dem Tippen ist die Antwort des Servers.
        beobachter = new MutationObserver(function (liste) {
            for (var i = 0; i < liste.length; i++) {
                var m = liste[i];
                if (m.type === 'attributes' && m.target === wartend && (m.attributeName === 'data-sk-laedt' || m.attributeName === 'data-sk-klick')) continue;
                // Fokuswechsel beim Tippen (die Überschrift verliert ihr vorübergehendes tabindex) ist keine Antwort des Servers.
                if (m.type === 'attributes' && m.attributeName === 'tabindex') continue;
                frei();
                return;
            }
        });
        beobachter.observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });
        timer = setTimeout(function () {
            if (!wartend || !document.body.contains(wartend)) { frei(); return; }
            wartend.setAttribute('data-sk-klick', '1');
            wartend.setAttribute('data-sk-laedt', (wartend.textContent || '').replace(/\s+/g, ' ').trim());
        }, VERZUG_MS);
        notfall = setTimeout(frei, NOTFALL_MS);
    }, true);
    window.addEventListener('pageshow', frei);
})();

// Ladebalken für JEDES Tippen (17.11.2026, Nutzer: « einfach überall prüfen »): Blazor Server antwortet erst nach der Rundreise
// zum Server – bei langsamer Verbindung geschah nach dem Tippen auf Chips, Reiter, Karten, Listenzeilen, Symbolknöpfe usw.
// sichtbar nichts. Bleibt die Seite nach einem Klick oder einer Auswahl länger als VERZUG_MS unverändert, läuft oben ein
// schmaler Balken, bis sich die Seite ändert (= Antwort des Servers), spätestens nach NOTFALL_MS. Knöpfe behalten zusätzlich
// ihren Ladekreis. Eingaben (Tippen in Felder) lösen nichts aus; Ausnahme je Element: data-ohne-ladekreis.
(function () {
    if (window.__ladebalken) return;
    window.__ladebalken = true;
    var VERZUG_MS = 150, NOTFALL_MS = 8000, timer = null, notfall = null, beobachter = null, balken = null;

    function holeBalken() {
        if (balken && document.body.contains(balken)) return balken;
        balken = document.createElement('div');
        balken.className = 'app-ladebalken';
        balken.setAttribute('aria-hidden', 'true');
        document.body.appendChild(balken);
        return balken;
    }

    function aus() {
        clearTimeout(timer); clearTimeout(notfall); timer = notfall = null;
        if (beobachter) { beobachter.disconnect(); beobachter = null; }
        if (balken) balken.classList.remove('aktiv');
    }

    function ignorieren(el) {
        if (!el || !el.closest) return true;
        if (el.closest('[data-ohne-ladekreis]')) return true;
        // Reine Browser-Aktionen ohne Server: Datei-Links, neue Tabs, Anker, Telefon/Mail, Video-Steuerung, Formularfelder zum Tippen
        var a = el.closest('a');
        if (a) {
            var ziel = a.getAttribute('href') || '';
            if (a.target || a.hasAttribute('download') || ziel.charAt(0) === '#' || /^(tel|mailto|whatsapp|https?):/i.test(ziel) && !ziel.startsWith(location.origin)) return true;
        }
        if (el.closest('video, audio, summary, input[type=text], input[type=number], input[type=password], input[type=email], input[type=tel], input[type=search], textarea')) return true;
        return false;
    }

    function starte(e) {
        var el = e.target;
        if (ignorieren(el)) return;
        // Nur echte Bedienelemente (Knopf, Link, Auswahl, Rolle « button ») oder Elemente, die wie solche aussehen (Zeiger).
        var bedien = el.closest && el.closest('button, a[href], select, label, [role=button], [role=radio], [role=checkbox], [role=tab], [tabindex]');
        if (!bedien && getComputedStyle(el).cursor !== 'pointer') return;
        aus();
        beobachter = new MutationObserver(function (liste) {
            for (var i = 0; i < liste.length; i++) {
                var m = liste[i];
                if (balken && (m.target === balken || (m.addedNodes && m.addedNodes[0] === balken))) continue;
                if (m.type === 'attributes' && (m.attributeName === 'tabindex' || m.attributeName === 'data-enote-wartet' || m.attributeName === 'data-sk-wartet' || m.attributeName === 'aria-busy')) continue;
                aus();
                return;
            }
        });
        beobachter.observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });
        timer = setTimeout(function () { holeBalken().classList.add('aktiv'); }, VERZUG_MS);
        notfall = setTimeout(aus, NOTFALL_MS);
    }

    document.addEventListener('click', starte, true);
    document.addEventListener('change', function (e) { if (e.target && e.target.matches && e.target.matches('select, input[type=checkbox], input[type=radio], input[type=date]')) starte(e); }, true);
    window.addEventListener('pageshow', aus);
})();

// Prüfung der Formation: die gewählte Antwort sofort zeigen (die Bestätigung des Servers folgt; Blazor übernimmt danach den Zustand).
(function () {
    if (window.__foAuswahlSofort) return;
    window.__foAuswahlSofort = true;
    document.addEventListener('click', function (e) {
        var k = e.target && e.target.closest ? e.target.closest('.fo-antwort') : null;
        if (!k || k.disabled) return;
        if (k.classList.contains('mehrfach')) { k.classList.toggle('gewaehlt'); k.setAttribute('aria-checked', k.classList.contains('gewaehlt') ? 'true' : 'false'); return; }
        var gruppe = k.closest('.fo-antworten');
        if (gruppe) gruppe.querySelectorAll('.fo-antwort.gewaehlt').forEach(function (x) { if (x !== k) { x.classList.remove('gewaehlt'); x.setAttribute('aria-checked', 'false'); } });
        k.classList.add('gewaehlt');
        k.setAttribute('aria-checked', 'true');
    }, true);
})();

// Passwort-Auge auf statischen Seiten (Connexion ohne Blazor-Verbindung): dort läuft @onclick nicht – Umschalten im Browser.
(function () {
    if (window.__skAugeStatisch) return;
    window.__skAugeStatisch = true;
    document.addEventListener('click', function (e) {
        var auge = e.target && e.target.closest ? e.target.closest('.sk-passwort-auge') : null;
        if (!auge || !auge.closest('[data-statisch]')) return;
        var feld = auge.parentElement.querySelector('input');
        if (!feld) return;
        var zeigen = feld.type === 'password';
        feld.type = zeigen ? 'text' : 'password';
        auge.setAttribute('aria-label', zeigen ? 'Masquer le mot de passe' : 'Afficher le mot de passe');
        var use = auge.querySelector('use');
        if (use) use.setAttribute('href', use.getAttribute('href').replace(/#eye(-off)?$/, zeigen ? '#eye-off' : '#eye'));
    });
})();

// « Poser une question » (19.11.2026): die Antwort liegt am Handy unter dem Formular – nach dem Eintreffen sichtbar machen.
window.skZeige = function (id) {
    var el = document.getElementById(id);
    if (el && el.scrollIntoView) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

// Menü-Blatt oder Fenster offen: Wischen geht nicht an die Seite dahinter (Nutzer 20.11.2026). Sonst rollt die Seite mit,
// und der Handy-Browser (Safari, Google-App …) lädt beim Ziehen nach unten die Seite neu. Das CSS (overflow,
// overscroll-behavior) reicht dafür in iPhone-Browsern nicht immer. Gerollt wird nur noch ein Bereich unter dem Finger,
// der in diese Richtung wirklich weiterrollen kann; Eingabefelder und Editoren bleiben unberührt. Der blockierende
// touchmove-Handler hängt nur, solange eine solche Ebene offen ist – sonst würde er jedes Rollen der App bremsen.
(function () {
    if (window.__skWischSperre) return;
    window.__skWischSperre = true;
    var EBENEN = '.sk-blatt-hintergrund, .sk-modal-hintergrund';
    var x = 0, y = 0, aktiv = false;

    function kannRollen(el, senkrecht, richtung) {
        var st = getComputedStyle(el), ov = senkrecht ? st.overflowY : st.overflowX;
        if (ov !== 'auto' && ov !== 'scroll') return false;
        if (senkrecht) {
            if (el.scrollHeight <= el.clientHeight + 1) return false;
            return richtung > 0 ? el.scrollTop > 0 : el.scrollTop + el.clientHeight < el.scrollHeight - 1;
        }
        if (el.scrollWidth <= el.clientWidth + 1) return false;
        return richtung > 0 ? el.scrollLeft > 0 : el.scrollLeft + el.clientWidth < el.scrollWidth - 1;
    }

    function beiStart(e) {
        if (e.touches.length === 1) { x = e.touches[0].clientX; y = e.touches[0].clientY; }
    }

    function beiBewegung(e) {
        if (!e.cancelable || e.touches.length !== 1) return;
        var t = e.touches[0], dx = t.clientX - x, dy = t.clientY - y;
        x = t.clientX; y = t.clientY;
        var ziel = e.target instanceof Element ? e.target : null;
        if (ziel && ziel.closest('input, textarea, select, [contenteditable="true"]')) return;
        var senkrecht = Math.abs(dy) >= Math.abs(dx), richtung = senkrecht ? dy : dx; // > 0: Finger nach unten/rechts
        if (richtung === 0) return;
        for (var el = ziel; el && el !== document.body && el !== document.documentElement; el = el.parentElement)
            if (kannRollen(el, senkrecht, richtung)) return;
        e.preventDefault();
    }

    function pruefe() {
        var offen = !!document.querySelector(EBENEN);
        if (offen === aktiv) return;
        aktiv = offen;
        if (offen) {
            document.addEventListener('touchstart', beiStart, { passive: true });
            document.addEventListener('touchmove', beiBewegung, { passive: false });
        } else {
            document.removeEventListener('touchstart', beiStart, { passive: true });
            document.removeEventListener('touchmove', beiBewegung, { passive: false });
        }
    }

    new MutationObserver(pruefe).observe(document.documentElement, { childList: true, subtree: true });
    pruefe();
})();
