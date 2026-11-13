// SKOULOU – Pädagogik: Editor (Quill 2 + KaTeX, selbst gehostet unter /lib), Formeln, Hochladen in Teilen,
// gesprochene Rückmeldung (MP3 « Vivienne »), Vorlesen (Browser-Stimme) und Examens-Uhr.
// Als ES-Modul geladen (import) – keine globalen Skripte in App.razor nötig.

const geladen = {};
function skript(src) {
    return geladen[src] ??= new Promise((ok, fehler) => {
        const s = document.createElement('script');
        s.src = src;
        s.onload = ok;
        s.onerror = () => { delete geladen[src]; fehler(new Error('Chargement impossible : ' + src)); };
        document.head.appendChild(s);
    });
}
function stil(href) {
    if (document.querySelector(`link[href="${href}"]`)) return;
    const l = document.createElement('link');
    l.rel = 'stylesheet';
    l.href = href;
    document.head.appendChild(l);
}

/** KaTeX laden (Anzeige von Formeln). */
export async function katexLaden() {
    stil('/lib/katex/katex.min.css');
    await skript('/lib/katex/katex.min.js');
}

/** KaTeX + Quill laden (Editor). */
async function quillLaden() {
    await katexLaden();
    stil('/lib/quill/quill.snow.css');
    await skript('/lib/quill/quill.js');
}

// ---------------------------------------------------------------- Formeln

/** Rendert alle Formeln (span.ql-formula[data-value]) innerhalb eines Elements. */
export async function formelnRendern(el) {
    if (!el) return;
    const spans = el.querySelectorAll('span.ql-formula[data-value]');
    if (spans.length === 0) return;
    await katexLaden();
    spans.forEach(span => {
        if (span.dataset.skGerendert === span.dataset.value) return;
        try {
            window.katex.render(span.dataset.value, span, { throwOnError: false, displayMode: false });
            span.dataset.skGerendert = span.dataset.value;
        } catch { span.textContent = span.dataset.value; }
    });
}

/** Live-Vorschau im Formeldialog; liefert eine Fehlermeldung oder null. */
export async function formelVorschau(el, latex) {
    if (!el) return null;
    await katexLaden();
    if (!latex || !latex.trim()) { el.innerHTML = ''; return null; }
    try {
        window.katex.render(latex, el, { throwOnError: true, displayMode: true });
        return null;
    } catch (e) {
        el.textContent = latex;
        return (e && e.message ? e.message : 'Formule invalide').replace('KaTeX parse error: ', '');
    }
}

// ---------------------------------------------------------------- Editor

const editoren = new Map();
let zaehler = 0;

function istIntern(src) {
    try { return new URL(src, location.href).origin === location.origin && new URL(src, location.href).pathname.startsWith('/medien/'); }
    catch { return false; }
}

/**
 * Erstellt einen Quill-Editor. Änderungen gehen verzögert (400 ms) an .NET (HtmlGeaendert);
 * Bild- und Formelknopf fragen .NET (BildWaehlen / FormelOeffnen), das die Dialoge zeigt.
 */
export async function editorErstellen(host, dotnet, html, kompakt, platzhalter) {
    await quillLaden();
    // Eigener Container: Quill fügt die Werkzeugleiste als Geschwister ein – nie in von Blazor verwaltete Knoten.
    host.innerHTML = '';
    const el = document.createElement('div');
    host.appendChild(el);
    const Quill = window.Quill;
    const toolbar = kompakt
        ? [['bold', 'italic', 'underline'], [{ color: [] }], [{ list: 'ordered' }, { list: 'bullet' }], ['image', 'formula'], ['clean']]
        : [[{ header: [2, 3, false] }], ['bold', 'italic', 'underline'], [{ color: [] }, { background: [] }],
           [{ list: 'ordered' }, { list: 'bullet' }], [{ align: [] }], ['link', 'image', 'formula'], ['clean']];
    const id = 'ed' + (++zaehler);
    const quill = new Quill(el, {
        theme: 'snow',
        placeholder: platzhalter || '',
        modules: {
            toolbar: {
                container: toolbar,
                handlers: {
                    image: () => { eintrag.bereich = quill.getSelection(true); dotnet.invokeMethodAsync('BildWaehlen').catch(() => { }); },
                    formula: () => { eintrag.bereich = quill.getSelection(true); eintrag.ersetzen = null; dotnet.invokeMethodAsync('FormelOeffnen', null).catch(() => { }); }
                }
            }
        }
    });
    // Eingefügte Bilder nur aus der eigenen Mediathek (keine externen Bilder, keine Daten-URLs).
    const Delta = Quill.import('delta');
    quill.clipboard.addMatcher('IMG', (node, delta) => istIntern(node.getAttribute('src')) ? delta : new Delta());
    if (html) quill.clipboard.dangerouslyPasteHTML(0, html, 'silent');

    const eintrag = { quill, dotnet, timer: null, bereich: null, ersetzen: null };
    editoren.set(id, eintrag);

    const senden = () => {
        clearTimeout(eintrag.timer);
        eintrag.timer = null;
        return dotnet.invokeMethodAsync('HtmlGeaendert', quill.root.innerHTML).catch(() => { });
    };
    eintrag.senden = senden;
    quill.on('text-change', () => { clearTimeout(eintrag.timer); eintrag.timer = setTimeout(senden, 400); });
    quill.root.addEventListener('blur', () => { if (eintrag.timer) senden(); });
    // Klick auf eine Formel öffnet sie zum Bearbeiten.
    quill.root.addEventListener('click', e => {
        const span = e.target.closest && e.target.closest('span.ql-formula');
        if (!span || !quill.isEnabled()) return;
        const blot = Quill.find(span);
        if (!blot) return;
        eintrag.ersetzen = quill.getIndex(blot);
        eintrag.bereich = { index: eintrag.ersetzen, length: 0 };
        dotnet.invokeMethodAsync('FormelOeffnen', span.dataset.value || '').catch(() => { });
    });
    return id;
}

/** Gibt ausstehende Änderungen sofort an .NET (vor dem Speichern). */
export async function editorSichern(id) {
    const e = editoren.get(id);
    if (e && e.timer) await e.senden();
}

export async function alleEditorenSichern() {
    await Promise.all([...editoren.values()].filter(e => e.timer).map(e => e.senden()));
}

export function editorHtml(id) {
    const e = editoren.get(id);
    return e ? e.quill.root.innerHTML : '';
}

export function editorSperren(id, gesperrt) {
    const e = editoren.get(id);
    if (e) e.quill.enable(!gesperrt);
}

export function editorEntfernen(id) {
    const e = editoren.get(id);
    if (!e) return;
    clearTimeout(e.timer);
    editoren.delete(id);
}

function einfuegeStelle(e) {
    const len = e.quill.getLength();
    const b = e.bereich ?? e.quill.getSelection() ?? { index: len - 1, length: 0 };
    return Math.min(b.index, len - 1);
}

export function bildEinfuegen(id, url) {
    const e = editoren.get(id);
    if (!e || !istIntern(url)) return;
    const index = einfuegeStelle(e);
    e.quill.insertEmbed(index, 'image', url, 'user');
    e.quill.setSelection(index + 1, 0, 'silent');
}

export function formelEinfuegen(id, latex) {
    const e = editoren.get(id);
    if (!e || !latex) return;
    let index = einfuegeStelle(e);
    if (e.ersetzen !== null && e.ersetzen !== undefined) {
        index = e.ersetzen;
        e.quill.deleteText(index, 1, 'user');
        e.ersetzen = null;
    }
    e.quill.insertEmbed(index, 'formula', latex, 'user');
    e.quill.insertText(index + 1, ' ', 'user');
    e.quill.setSelection(index + 2, 0, 'silent');
}

// ---------------------------------------------------------------- Hochladen in Teilen

const TEIL = 4 * 1024 * 1024;

async function anfrage(url, optionen) {
    for (let versuch = 1; ; versuch++) {
        try {
            const antwort = await fetch(url, { credentials: 'same-origin', ...optionen, headers: { 'X-Skoulou-Upload': '1', ...(optionen.headers || {}) } });
            const daten = await antwort.json().catch(() => ({}));
            if (antwort.ok) return daten;
            if (antwort.status < 500 || versuch >= 3) throw new Error(daten.meldung || 'L\'envoi a échoué.');
        } catch (e) {
            if (versuch >= 3 || (e && e.message && !e.message.startsWith('Failed') && !e.message.includes('NetworkError'))) throw e;
        }
        await new Promise(r => setTimeout(r, 1500 * versuch));
    }
}

/** Lädt die gewählte Datei eines &lt;input type=file&gt; hoch; meldet den Fortschritt (0–100) an .NET. */
export async function hochladen(input, art, dotnet) {
    const datei = input && input.files && input.files[0];
    if (!datei) throw new Error('Choisissez un fichier.');
    try {
        const start = await anfrage('/api/paedagogik/medien/start', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ art, name: datei.name, groesse: datei.size })
        });
        let offset = 0;
        while (offset < datei.size) {
            const teil = datei.slice(offset, offset + TEIL);
            const r = await anfrage(`/api/paedagogik/medien/${start.id}?offset=${offset}`, { method: 'PUT', body: teil });
            offset = r.empfangen;
            dotnet.invokeMethodAsync('UploadFortschritt', Math.round(offset * 100 / datei.size)).catch(() => { });
        }
        return await anfrage(`/api/paedagogik/medien/${start.id}/fin`, { method: 'POST' });
    } finally {
        input.value = '';
    }
}

/** Name und Größe der gewählten Datei (Vorprüfung im Browser). */
export function dateiInfo(input) {
    const d = input && input.files && input.files[0];
    return d ? { name: d.name, groesse: d.size, typ: d.type } : null;
}

// ---------------------------------------------------------------- Rückmeldung (Ton + Stimme)

const SAETZE = {
    richtig: ['richtig-1', 'richtig-2', 'richtig-3', 'richtig-4', 'richtig-5', 'richtig-6'],
    falsch: ['falsch-1', 'falsch-2', 'falsch-3', 'falsch-4', 'falsch-5', 'falsch-6'],
    weiter: ['weiter'],
    gesendet: ['gesendet']
};
const ERSATZ = { richtig: 'Bravo, très bien !', falsch: 'Oups, essaie encore !', weiter: 'Exercice suivant !', gesendet: 'Réponse envoyée !' };
let kontext = null;
const puffer = new Map();
let vorladen = null;
const zuletzt = {};

function audio() {
    if (!kontext) {
        const Typ = window.AudioContext || window.webkitAudioContext;
        if (!Typ) return null;
        kontext = new Typ();
    }
    if (kontext.state === 'suspended') kontext.resume().catch(() => { });
    return kontext;
}

/** Beim ersten Tippen aufrufen: schaltet den Ton frei (iPhone) und lädt die Sätze vor. */
export function feedbackVorbereiten() {
    const c = audio();
    if (!c || vorladen) return;
    vorladen = Promise.all(Object.values(SAETZE).flat().map(async name => {
        try {
            const r = await fetch(`/audio/antworten/${name}.mp3`, { cache: 'force-cache' });
            if (!r.ok) return;
            const daten = await r.arrayBuffer();
            puffer.set(name, await new Promise((ok, f) => c.decodeAudioData(daten, ok, f)));
        } catch { /* Ersatz: Browser-Stimme */ }
    }));
}

function ton(frequenzen, art, dauer) {
    const c = audio();
    if (!c) return 0;
    let t = c.currentTime;
    frequenzen.forEach(f => {
        const o = c.createOscillator(), g = c.createGain();
        o.type = art; o.frequency.value = f;
        g.gain.setValueAtTime(0.0001, t);
        g.gain.exponentialRampToValueAtTime(0.12, t + 0.02);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dauer);
        o.connect(g).connect(c.destination);
        o.start(t); o.stop(t + dauer + 0.02);
        t += dauer * 0.8;
    });
    return (t - c.currentTime) * 1000;
}

function satz(gruppe, verzoegerung) {
    const namen = SAETZE[gruppe].filter(n => puffer.has(n));
    if (namen.length === 0) {
        if (!('speechSynthesis' in window)) return 0;
        const u = new SpeechSynthesisUtterance(ERSATZ[gruppe]);
        u.lang = 'fr-FR';
        setTimeout(() => speechSynthesis.speak(u), verzoegerung);
        return verzoegerung + 1500;
    }
    const wahl = namen.length > 1 ? namen.filter(n => n !== zuletzt[gruppe]) : namen;
    const name = wahl[Math.floor(Math.random() * wahl.length)];
    zuletzt[gruppe] = name;
    const c = audio();
    const quelle = c.createBufferSource();
    quelle.buffer = puffer.get(name);
    quelle.connect(c.destination);
    quelle.start(c.currentTime + verzoegerung / 1000);
    return verzoegerung + quelle.buffer.duration * 1000;
}

/** Rückmeldung nach einer Antwort: art = richtig | teilweise | falsch | gesendet. */
export async function feedback(art, stimme, weiter) {
    feedbackVorbereiten();
    if (vorladen) await Promise.race([vorladen, new Promise(r => setTimeout(r, 800))]);
    const gruppe = art === 'richtig' ? 'richtig' : art === 'gesendet' ? 'gesendet' : 'falsch';
    const dauer = gruppe === 'richtig' ? ton([523, 659, 784], 'sine', 0.16)
        : gruppe === 'gesendet' ? ton([600, 900], 'triangle', 0.14)
        : ton([220, 180], 'sawtooth', 0.18);
    if (!stimme) return;
    const ende = satz(gruppe, dauer + 80);
    if (gruppe === 'richtig' && weiter) satz('weiter', ende + 250);
}

// ---------------------------------------------------------------- Vorlesen (Audio-Aufgaben)

/** Liest einen Text mit der Browser-Stimme vor (Sprache der Aufgabe, langsamer für Diktate). */
export function vorlesen(text, sprache, langsam) {
    if (!('speechSynthesis' in window) || !text) return false;
    speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(text);
    u.lang = sprache || 'fr-FR';
    u.rate = langsam ? 0.7 : 0.95;
    const stimme = speechSynthesis.getVoices().find(v => v.lang && v.lang.toLowerCase().startsWith(u.lang.toLowerCase().slice(0, 2)));
    if (stimme) u.voice = stimme;
    speechSynthesis.speak(u);
    return true;
}

export function vorlesenStoppen() {
    if ('speechSynthesis' in window) speechSynthesis.cancel();
}

// ---------------------------------------------------------------- Examens-Uhr

const uhren = new Map();

function format(s) {
    const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sek = s % 60;
    const zwei = n => String(n).padStart(2, '0');
    return h > 0 ? `${h}:${zwei(m)}:${zwei(sek)}` : `${zwei(m)}:${zwei(sek)}`;
}

/** Zählt sichtbar herunter (die Zeit gilt serverseitig); bei 0 → .NET ZeitAbgelaufen. */
export function uhrStarten(el, sekunden, dotnet) {
    uhrStoppen(el);
    if (!el) return;
    const ende = Date.now() + sekunden * 1000;
    let gemeldet = false;
    const tick = () => {
        const rest = Math.max(0, Math.round((ende - Date.now()) / 1000));
        el.textContent = format(rest);
        el.classList.toggle('knapp', rest <= 300);
        el.classList.toggle('kritisch', rest <= 60);
        if (rest === 0 && !gemeldet) {
            gemeldet = true;
            dotnet.invokeMethodAsync('ZeitAbgelaufen').catch(() => { });
        }
    };
    tick();
    uhren.set(el, setInterval(tick, 1000));
}

export function uhrStoppen(el) {
    const t = uhren.get(el);
    if (t) { clearInterval(t); uhren.delete(el); }
}

export function nachOben(el) {
    if (el && el.scrollIntoView) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
}
