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
        // Zweiter Durchgang (unten über requestSubmit ausgelöst): jetzt wirklich senden.
        if (form.dataset.enoteGezeichnet === '1') { delete form.dataset.enoteGezeichnet; return; }
        if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;
        var btn = e.submitter || form.querySelector('[type="submit"]');
        if (!btn) return;
        if (btn.classList.contains('enote-laedt')) { e.preventDefault(); return; }   // Doppeltipp: nur einmal senden

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
        // Erst zeichnen, dann senden (Nutzer 19.11.2026: « Se connecter » zeigte nur am Handy keinen Ladekreis). Safari am iPhone
        // zeichnet nach dem Start des Absendens nichts mehr – der Kreis kam dort nie auf den Bildschirm. Deshalb das Absenden
        // anhalten, ein Bild zeichnen lassen und dann mit demselben Knopf erneut absenden (requestSubmit behält name/value).
        e.preventDefault();
        var absender = e.submitter && e.submitter.form === form ? e.submitter : null;
        var gesendet = false;
        function senden() {
            if (gesendet) return;
            gesendet = true;
            form.dataset.enoteGezeichnet = '1';
            if (typeof form.requestSubmit === 'function') form.requestSubmit(absender || undefined);
            else HTMLFormElement.prototype.submit.call(form);
            delete form.dataset.enoteGezeichnet;   // bei einem Prüffehler des Browsers kam kein zweiter Durchgang
            // Erst nach dem Absenden sperren, sonst verwerfen manche Browser den Submit.
            setTimeout(function () { btn.disabled = true; }, 0);
        }
        requestAnimationFrame(function () { setTimeout(senden, 40); });
        setTimeout(senden, 400);   // falls kein Bild gezeichnet wird (Tab im Hintergrund)
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

// Pflichtfelder statischer Formulare (Registrierung): <form data-pflicht-sperre> – der Absende-Knopf bleibt gesperrt, solange ein
// Feld mit « required » leer bzw. ein Pflicht-Häkchen nicht gesetzt ist (Nutzer 19.11.2026: « Buttons deaktivieren, wenn Angaben
// noch nicht vorhanden sind »). Läuft ohne Blazor-Verbindung. Automatisches Ausfüllen des Browsers meldet nicht immer « input »,
// deshalb zusätzlich regelmäßig nachprüfen (wenige Felder, billig).
(function () {
    if (window.__enotePflichtSperre) return;
    window.__enotePflichtSperre = true;

    function leer(feld) {
        if (feld.disabled) return false;
        if (feld.type === 'checkbox' || feld.type === 'radio') return !feld.checked;
        return !(feld.value || '').trim();
    }

    function pruefe(form) {
        var fehlt = Array.prototype.some.call(form.querySelectorAll('[required]'), leer);
        form.querySelectorAll('button[type="submit"]').forEach(function (knopf) {
            if (knopf.classList.contains('enote-laedt')) return;
            if (knopf.disabled !== fehlt) knopf.disabled = fehlt;
            if (fehlt) knopf.title = "Remplissez d'abord les champs marqués d'un *"; else knopf.removeAttribute('title');
        });
    }

    function alle() { document.querySelectorAll('form[data-pflicht-sperre]').forEach(pruefe); }

    ['input', 'change'].forEach(function (art) {
        document.addEventListener(art, function (e) {
            var form = e.target && e.target.form;
            if (form && form.hasAttribute('data-pflicht-sperre')) pruefe(form);
        }, true);
    });
    window.addEventListener('pageshow', alle);
    setInterval(alle, 800);
    alle();
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
// 18.11.2026: Komponenten melden sich beim Entsorgen ab (enoteUnregisterElementCloser); ein Aufruf an eine bereits
// entsorgte Referenz (z. B. Auswahlliste in einem geschlossenen Fenster) entfernt den Eintrag, statt bei jedem Klick
// « There was an exception invoking 'SchliesseVonAussen' » in die Konsole zu schreiben.
function enoteGleicheRef(a, b) { return a === b || (!!a && !!b && a._id !== undefined && a._id === b._id); }
window.enoteRegisterElementCloser = function (dotNetRef, cssKlasse) {
    window.__enoteClosers = window.__enoteClosers || [];
    if (!window.__enoteClosers.some(function (c) { return enoteGleicheRef(c.dotNetRef, dotNetRef); }))
        window.__enoteClosers.push({ dotNetRef: dotNetRef, cssKlasse: cssKlasse });

    if (window.__enoteCloserGlobalHandler) return;
    window.__enoteCloserGlobalHandler = function (e) {
        (window.__enoteClosers || []).slice().forEach(function (c) {
            var el = document.querySelector('.enote-' + c.cssKlasse + '.offen');
            if (el && !el.contains(e.target)) {
                c.dotNetRef.invokeMethodAsync('SchliesseVonAussen').catch(function () {
                    window.enoteUnregisterElementCloser(c.dotNetRef);
                });
            }
        });
    };
    document.addEventListener('click', window.__enoteCloserGlobalHandler);
};
window.enoteUnregisterElementCloser = function (dotNetRef) {
    window.__enoteClosers = (window.__enoteClosers || []).filter(function (c) { return !enoteGleicheRef(c.dotNetRef, dotNetRef); });
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

// « Vidéos d'aide »: Position der Seite merken, solange ein Video offen ist. Am Handy dreht das Vollbild ins Querformat;
// dabei wechselt die Seite ihre Anordnung und stünde nach dem Schließen ganz oben statt beim angesehenen Video.
// Fehler 16.11. (am Handy): « beim Herunterrollen springt die Seite wieder nach oben ». Jedes Ein-/Ausblenden der Adressleiste
// ist ein « resize » – blieb das Horchen an (Dialog ohne « Fermer » verlassen, z. B. Zurück-Taste) oder rollte der Nutzer in den
// 2,5 s nach dem Schließen, setzte jede Bewegung die Seite zurück. Darum: nur solange der Spieler da ist bzw. kurz danach, und
// sobald der Nutzer die Seite nach dem Schließen berührt, gehört die Position ihm.
window.enoteVideoPosition = (function () {
    var SPIELER = '.vh-spieler';
    var gemerkt = null, timer = null, aktiv = false, lauf = 0, schliessend = false;
    function rollend() {
        return [document.scrollingElement, document.querySelector('.enote-main')].filter(function (e) { return !!e; });
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

// Sofortige Rückmeldung für alle Aktionsknöpfe (16.11.2026): der Ladekreis erscheint beim Tippen, ohne auf die Antwort
// des Servers zu warten – bei langsamer Verbindung verging sonst spürbar Zeit, in der nichts geschah (der Kreis des
// Servers kommt erst mit seiner Antwort). Er verschwindet mit der ersten Änderung der Seite nach dem Tippen (= Antwort
// des Servers), spätestens nach einigen Sekunden. Nur über das Attribut data-enote-wartet (CSS) – der Inhalt des Knopfs
// bleibt unberührt, weil Blazor ihn verwaltet. Kacheln und Tastenfelder der Kasse sind keine « enote-btn » und bleiben
// frei (schnelles Mehrfachtippen). Ausnahme je Knopf: data-ohne-ladekreis.
(function () {
    if (window.__enoteKlickRing) return;
    window.__enoteKlickRing = true;
    var VERZUG_MS = 120, NOTFALL_MS = 6000, wartend = null, timer = null, notfall = null, beobachter = null;

    function frei() {
        clearTimeout(timer); clearTimeout(notfall);
        if (beobachter) { beobachter.disconnect(); beobachter = null; }
        if (wartend) { wartend.removeAttribute('data-enote-wartet'); wartend = null; }
    }

    function passt(knopf) {
        if (!knopf || knopf.disabled || knopf.hasAttribute('data-enote-wartet') || knopf.hasAttribute('data-ohne-ladekreis')) return false;
        if (knopf.classList.contains('enote-laedt') || knopf.hasAttribute('onclick')) return false;   // schon beschäftigt bzw. reine Browser-Aktion
        if (knopf.tagName === 'A') {
            var ziel = knopf.getAttribute('href') || '';
            if (!ziel || ziel.charAt(0) === '#' || knopf.target || knopf.hasAttribute('download') || /\/bon\/|\/etiketten|\/rapport|\.pdf(\?|$)|^tel:|^mailto:/.test(ziel)) return false;
        }
        var form = knopf.form || knopf.closest('form');
        if (form && form.hasAttribute('data-ladeanzeige')) return false;   // klassische Formulare: eigene Ladeanzeige (oben)
        return true;
    }

    document.addEventListener('click', function (e) {
        var knopf = e.target && e.target.closest ? e.target.closest('.enote-btn, .btn-primary') : null;
        if (!passt(knopf)) return;
        frei();
        wartend = knopf;
        // Jede Änderung der Seite nach dem Tippen ist die Antwort des Servers.
        beobachter = new MutationObserver(function (liste) {
            for (var i = 0; i < liste.length; i++) {
                var m = liste[i];
                if (m.type === 'attributes' && m.target === wartend && m.attributeName === 'data-enote-wartet') continue;
                // Fokuswechsel beim Tippen (die Überschrift verliert ihr vorübergehendes tabindex) ist keine Antwort des Servers.
                if (m.type === 'attributes' && m.attributeName === 'tabindex') continue;
                frei();
                return;
            }
        });
        beobachter.observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });
        timer = setTimeout(function () {
            if (!wartend || !document.body.contains(wartend)) { frei(); return; }
            wartend.setAttribute('data-enote-wartet', (wartend.textContent || '').replace(/\s+/g, ' ').trim());
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

// « Poser une question » (19.11.2026): die Antwort liegt am Handy unter dem Formular – nach dem Eintreffen sichtbar machen.
window.enoteZeige = function (id) {
    var el = document.getElementById(id);
    if (el && el.scrollIntoView) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

// Blatt « Plus », Fenster oder Video offen: Wischen geht nicht an die Seite dahinter (Nutzer 20.11.2026). Sonst rollt die
// Seite mit, und der Handy-Browser (Safari, Google-App …) lädt beim Ziehen nach unten die Seite neu. Das CSS (overflow,
// overscroll-behavior) reicht dafür in iPhone-Browsern nicht immer. Gerollt wird nur noch ein Bereich unter dem Finger,
// der in diese Richtung wirklich weiterrollen kann; Eingabefelder bleiben unberührt. Der blockierende touchmove-Handler
// hängt nur, solange eine solche Ebene offen ist – sonst würde er jedes Rollen der App (z. B. in der Kasse) bremsen.
(function () {
    if (window.__enoteWischSperre) return;
    window.__enoteWischSperre = true;
    var EBENEN = '.enote-sheet-backdrop, .enote-modal-backdrop, .lp-video-overlay';
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

// Bis die interaktive Verbindung steht, ist eine Seite nur vorgerendert (Klicktest 20.11.2026, langsames Netz): bei 700 ms
// Verzögerung war die Kasse nach 3 s zu sehen, aber erst nach 13 s bedienbar (bei 300 ms: 1,3 s / 5,9 s). Tippen auf « Ama »,
// PIN-Tasten usw. ging in dieser Zeit still verloren, Eingaben in Felder wurden beim Verbindungsaufbau wieder geleert.
// Routes.razor zeichnet dann [data-enote-vorgerendert] (« Connexion en cours… »); solange es da ist, werden Tippen und
// Tastatureingaben abgefangen und der Hinweis sofort gezeigt. Links (echte Seitenwechsel) und klassische Formulare
// (Connexion, Déconnexion) funktionieren ohne Verbindung und bleiben frei.
(function () {
    if (window.__enoteVorgerendert) return;
    window.__enoteVorgerendert = true;

    function hinweis() { return document.querySelector('[data-enote-vorgerendert]'); }

    function abfangen(e) {
        var h = hinweis();
        if (!h) return;
        var el = e.target;
        if (!el || !el.closest) return;
        if (el.closest('a[href]') && !el.closest('button')) return;
        var form = el.closest('form');
        if (form && form.hasAttribute('data-ladeanzeige')) return;
        if (e.type === 'keydown') {
            if (e.key === 'Tab' || e.key === 'Escape' || e.ctrlKey || e.metaKey || e.altKey) return;
            if (!el.closest('input, textarea, select, [contenteditable]') && e.key !== 'Enter' && e.key !== ' ') return;
        } else if (e.type === 'beforeinput') {
            if (!el.closest('input, textarea, [contenteditable]')) return;
        } else if (!el.closest('button, select, label, input, textarea, [role], [tabindex], [onclick]') && getComputedStyle(el).cursor !== 'pointer') {
            return;
        }
        e.preventDefault();
        e.stopImmediatePropagation();
        h.classList.add('sichtbar');
    }

    ['click', 'keydown', 'beforeinput'].forEach(function (typ) { window.addEventListener(typ, abfangen, true); });
})();
