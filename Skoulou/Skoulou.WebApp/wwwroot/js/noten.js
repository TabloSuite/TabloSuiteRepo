// SKOULOU – Notenraster: Tastaturnavigation (Entrée/↓ nächster Schüler, ↑ vorheriger, ←/→ am Rand der Eingabe Nachbarzelle).
// Das Speichern übernimmt Blazor beim Verlassen der Zelle (change-Ereignis).
export function registriere(tabelle) {
    if (!tabelle || tabelle.dataset.ntBereit) return;
    tabelle.dataset.ntBereit = '1';
    tabelle.addEventListener('keydown', function (e) {
        var el = e.target;
        if (!el || !el.classList || !el.classList.contains('nt-zelle')) return;
        var z = parseInt(el.dataset.z, 10), s = parseInt(el.dataset.s, 10), ziel = null;
        var anfang = el.selectionStart === 0 && el.selectionEnd === 0;
        var ende = el.selectionStart === el.value.length;
        if (e.key === 'Enter' || e.key === 'ArrowDown') ziel = [z + 1, s];
        else if (e.key === 'ArrowUp') ziel = [z - 1, s];
        else if (e.key === 'ArrowRight' && ende) ziel = [z, s + 1];
        else if (e.key === 'ArrowLeft' && (anfang || el.selectionStart === el.value.length && el.selectionEnd === el.value.length && el.value.length === 0)) ziel = [z, s - 1];
        if (!ziel) return;
        var naechste = tabelle.querySelector('.nt-zelle[data-z="' + ziel[0] + '"][data-s="' + ziel[1] + '"]');
        e.preventDefault();
        if (naechste) { naechste.focus(); naechste.select(); }
        else if (e.key === 'Enter') el.blur();
    });
    tabelle.addEventListener('focusin', function (e) {
        if (e.target && e.target.classList && e.target.classList.contains('nt-zelle')) {
            try { e.target.select(); } catch (x) { }
            if (window.matchMedia && matchMedia('(max-width: 640px)').matches) e.target.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'smooth' });
        }
    });
}

// Fokus auf eine Zelle setzen (nach einem Status-Knopf bleibt man in der Zelle).
export function fokus(tabelle, z, s) {
    if (!tabelle) return;
    var el = tabelle.querySelector('.nt-zelle[data-z="' + z + '"][data-s="' + s + '"]');
    if (el) { el.focus(); }
}
