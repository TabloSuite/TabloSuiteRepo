const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const root = path.join(__dirname, '..', '..', 'SKOULOUAPP.WebApp', 'wwwroot');
const script = fs.readFileSync(path.join(root, 'js', 'richTextEditor.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'app.css'), 'utf8');
const context = { window: {} };
vm.runInNewContext(script, context);
const editor = context.window.SKOULOUAPPRichText;

function createPicker(expanded = false) {
    const classes = new Set(expanded ? ['ql-expanded'] : []);
    const attributes = new Map();
    const label = {
        setAttribute: (key, value) => attributes.set(key, value),
        removeAttribute: key => attributes.delete(key)
    };
    const items = ['arial', 'inter'].map(value => ({
        getAttribute: () => value,
        classList: { toggle: (name, selected) => { if (name === 'ql-selected') { attributes.set(value, selected); } } }
    }));
    const picker = {
        classList: {
            contains: name => classes.has(name),
            toggle: (name, active) => { if (active) classes.add(name); else classes.delete(name); }
        },
        querySelector: () => label,
        querySelectorAll: () => items
    };
    const select = { value: 'arial' };
    const container = { querySelector: selector => selector.startsWith('select') ? select : picker };
    return { classes, attributes, select, container };
}

test('offenes Schriftarten- und Größenmenü bleibt bei Format-Synchronisierung offen', () => {
    for (const format of ['font', 'size']) {
        const picker = createPicker(true);
        editor.setPickerValue(picker.container, format, 'inter');
        assert.equal(picker.classes.has('ql-expanded'), true);
        assert.equal(picker.attributes.size, 0);
        assert.equal(picker.select.value, 'arial');
    }
});

test('Quill-Bildformat übernimmt die Ausrichtung beim erneuten HTML-Import', () => {
    class Image {
        static formats() { return { width: '240' }; }
        constructor(node) { this.domNode = node; }
        format() {}
    }
    const registered = [];
    context.Quill = {
        import: name => name === 'formats/image' ? Image : { whitelist: [] },
        register: format => registered.push(format)
    };
    editor._formatsRegistered = false;
    editor.ensureFormatsRegistered();
    const AlignedImage = registered.at(-1);
    const classes = new Set();
    const node = {
        classList: {
            contains: name => classes.has(name),
            toggle: (name, enabled) => enabled ? classes.add(name) : classes.delete(name)
        }
    };
    const image = new AlignedImage(node);
    image.format('imageAlign', 'right');
    assert.deepEqual(AlignedImage.formats(node), { width: '240', imageAlign: 'right' });
    image.format('imageAlign', 'center');
    assert.deepEqual(AlignedImage.formats(node), { width: '240', imageAlign: 'center' });
    assert.equal(classes.has('ql-image-align-right'), false);
    assert.match(css, /img\.ql-image-align-right\s*\{[^}]*margin-left:\s*auto/);
    editor._formatsRegistered = false;
});

test('Bild wird im Quill-Dokument samt Breite und Alt-Text verschoben', () => {
    const calls = [];
    class Delta {
        retain(count) { calls.push(['retain', count]); return this; }
        delete(count) { calls.push(['delete', count]); return this; }
        insert(value, attributes) { calls.push(['insert', value, attributes]); return this; }
    }
    context.Quill = { import: () => Delta };
    const image = { insert: { image: '/media-library/image' }, attributes: { width: '240', alt: 'Photo' } };
    const quill = {
        getLeaf: () => [{ domNode: { tagName: 'IMG' } }],
        getContents: () => ({ ops: [image] }),
        getLength: () => 15,
        updateContents: (delta, source) => calls.push(['update', delta instanceof Delta, source])
    };
    assert.equal(editor.moveImage(quill, 3, 9), true);
    assert.deepEqual(calls, [
        ['retain', 3], ['delete', 1], ['retain', 5],
        ['insert', image.insert, image.attributes], ['update', true, 'user']
    ]);
    calls.length = 0;
    assert.equal(editor.moveImage(quill, 9, 2), true);
    assert.deepEqual(calls, [
        ['retain', 2], ['insert', image.insert, image.attributes],
        ['retain', 7], ['delete', 1], ['update', true, 'user']
    ]);
});

test('Bild bleibt bei ungültigem oder gleichem Ablagepunkt unverändert', () => {
    const quill = {
        getLeaf: () => [{ domNode: { tagName: 'IMG' } }],
        getContents: () => ({ ops: [{ insert: { image: '/media-library/image' } }] }),
        getLength: () => 10,
        updateContents: () => { throw new Error('Kein Update erwartet'); }
    };
    for (const target of [-1, 3, 4, 10]) {
        assert.equal(editor.moveImage(quill, 3, target), false);
    }
});

test('Bildausrichtung formatiert ausschließlich das Bild-Embed', () => {
    const calls = [];
    const quill = {
        getLeaf: index => [{ domNode: { tagName: index === 4 ? 'IMG' : 'SPAN' } }],
        formatText: (...args) => calls.push(args)
    };
    assert.equal(editor.alignImage(quill, 4, 'left'), true);
    assert.equal(editor.alignImage(quill, 4, 'center'), true);
    assert.equal(editor.alignImage(quill, 4, 'right'), true);
    assert.equal(editor.alignImage(quill, 4, 'unknown'), false);
    assert.equal(editor.alignImage(quill, 3, 'right'), false);
    assert.deepEqual(calls, [
        [4, 1, 'imageAlign', 'left', 'user'],
        [4, 1, 'imageAlign', 'center', 'user'],
        [4, 1, 'imageAlign', 'right', 'user']
    ]);
});

test('geschlossener Picker zeigt aktuelle Schrift an', () => {
    const picker = createPicker();
    editor.setPickerValue(picker.container, 'font', 'inter');
    assert.equal(picker.select.value, 'inter');
    assert.equal(picker.attributes.get('data-value'), 'inter');
    assert.equal(picker.attributes.get('inter'), true);
    assert.equal(picker.attributes.get('arial'), false);
});

test('Picker entfernt alte Arial-Black-Anzeige bei normalem Text', () => {
    const picker = createPicker();
    editor.setPickerValue(picker.container, 'font', 'arial-black');
    editor.setPickerValue(picker.container, 'font', null);
    assert.equal(picker.attributes.has('data-value'), false);
    assert.equal(picker.select.value, '');
    assert.equal(picker.classes.has('ql-active'), false);
});

test('Schriftart und Größe formatieren den markierten Text statt nur den Picker', () => {
    const calls = [];
    const quill = {
        formatText: (...args) => calls.push(['formatText', ...args]),
        setSelection: (...args) => calls.push(['setSelection', ...args]),
        focus: () => calls.push(['focus']),
        format: (...args) => calls.push(['format', ...args])
    };
    const range = { index: 8, length: 21 };
    editor.applySelectedFormat(quill, 'font', 'arial-black', range);
    editor.applySelectedFormat(quill, 'size', '20px', range);
    assert.deepEqual(calls, [
        ['formatText', 8, 21, 'font', 'arial-black', 'user'],
        ['setSelection', 8, 21, 'silent'],
        ['formatText', 8, 21, 'size', '20px', 'user'],
        ['setSelection', 8, 21, 'silent']
    ]);
});

test('Ziehgriff verändert die Bildbreite durch Ziehen direkt am Bild', () => {
    const events = new Map();
    const handleEvents = new Map();
    const textEvents = new Map();
    let imageIndex = 5;
    let currentImage;
    const image = {
        tagName: 'IMG',
        getAttribute: name => name === 'src' ? '/media-library/image' : '240',
        hasAttribute: () => false,
        getBoundingClientRect: () => ({ left: 40, top: 60, width: 240, height: 120 })
    };
    currentImage = image;
    const handle = {
        setAttribute: () => {},
        addEventListener: (name, callback) => handleEvents.set(name, callback),
        setPointerCapture: () => {}
    };
    const buttons = [];
    const overlay = { style: {}, appendChild: () => {} };
    const marker = { style: {} };
    const alignmentControls = { appendChild: button => buttons.push(button) };
    const container = {
        appendChild: () => {},
        addEventListener: () => {},
        getBoundingClientRect: () => ({ left: 10, top: 20 })
    };
    const calls = [];
    const root = {
        addEventListener: (name, callback) => events.set(name, callback),
        contains: node => node === currentImage,
        querySelectorAll: () => [currentImage],
        classList: { add: () => {}, remove: () => {} },
        getBoundingClientRect: () => ({ left: 10, right: 680, top: 0, bottom: 500, width: 670 }),
        setPointerCapture: () => {}
    };
    const quill = {
        root,
        container: { parentElement: container },
        getIndex: () => imageIndex,
        getLeaf: () => [{ domNode: currentImage }],
        formatText: (...args) => calls.push(args),
        on: (name, callback) => textEvents.set(name, callback),
        getBounds: () => ({ left: 30, top: 40, height: 20 })
    };
    context.Quill = { find: node => ({ domNode: node }) };
    let divCount = 0;
    let buttonCount = 0;
    context.document = { createElement: tag => {
        if (tag === 'div') return [overlay, alignmentControls, marker][divCount++];
        if (buttonCount++ === 0) return handle;
        const buttonEvents = new Map();
        return { setAttribute: () => {}, addEventListener: (name, callback) => buttonEvents.set(name, callback), click: () => buttonEvents.get('click')() };
    } };
    context.window.addEventListener = () => {};
    context.window.removeEventListener = () => {};
    context.requestAnimationFrame = callback => callback();
    editor.setupImageSizing(quill);
    events.get('click')({ target: { closest: () => image } });
    assert.equal(overlay.hidden, false);
    assert.equal(overlay.style.left, '30px');
    assert.equal(overlay.style.top, '40px');
    buttons[0].click();
    buttons[1].click();
    buttons[2].click();
    assert.deepEqual(calls, [
        [5, 1, 'imageAlign', 'left', 'user'],
        [5, 1, 'imageAlign', 'center', 'user'],
        [5, 1, 'imageAlign', 'right', 'user']
    ]);
    calls.length = 0;
    events.get('pointerdown')({ pointerId: 3, clientX: 100, clientY: 100, target: { closest: () => image }, preventDefault: () => {} });
    events.get('pointerup')({ pointerId: 3, clientX: 100, clientY: 100 });
    events.get('click')({ target: { closest: () => null } });
    assert.equal(overlay.hidden, false);
    events.get('pointerdown')({ pointerId: 4, clientX: 100, clientY: 100, target: { closest: () => image }, preventDefault: () => {} });
    events.get('pointermove')({ pointerId: 4, clientX: 610, clientY: 100 });
    events.get('pointerup')({ pointerId: 4, clientX: 610, clientY: 100 });
    assert.deepEqual(calls, [[5, 1, 'imageAlign', 'right', 'user']]);
    events.get('click')({ target: { closest: () => null } });
    assert.equal(overlay.hidden, false);
    calls.length = 0;
    imageIndex = 8;
    textEvents.get('text-change')();
    handleEvents.get('pointerdown')({ pointerId: 1, clientX: 100, preventDefault: () => {}, stopPropagation: () => {} });
    handleEvents.get('pointermove')({ pointerId: 1, clientX: 160 });
    handleEvents.get('pointerup')({ pointerId: 1 });
    assert.deepEqual(calls, [[8, 1, 'width', '300', 'user']]);
    const reimported = { ...image };
    currentImage = reimported;
    quill._halloappImageSizingRefresh(true);
    assert.equal(overlay.hidden, false);
    handleEvents.get('pointerdown')({ pointerId: 2, clientX: 100, preventDefault: () => {}, stopPropagation: () => {} });
    handleEvents.get('pointermove')({ pointerId: 2, clientX: 120 });
    handleEvents.get('pointerup')({ pointerId: 2 });
    assert.deepEqual(calls[1], [8, 1, 'width', '260', 'user']);
    events.get('click')({ target: { closest: () => null } });
    assert.equal(overlay.hidden, true);
});

test('Ziehbreite wird auf die sichere Bildbreite begrenzt', () => {
    assert.equal(editor.draggedImageWidth(240, 100, 90), 230);
    assert.equal(editor.draggedImageWidth(240, 100, -300), 80);
    assert.equal(editor.draggedImageWidth(240, 100, 900), 670);
});

test('ohne markierten Text formatiert der Picker nur die nächste Eingabe', () => {
    const calls = [];
    editor.applySelectedFormat({
        focus: () => calls.push('focus'),
        format: (...args) => calls.push(args)
    }, 'font', 'arial-black', { index: 8, length: 0 });
    assert.deepEqual(calls, ['focus', ['font', 'arial-black', 'user']]);
});

test('Fett und Kursiv schalten die Formatierung der markierten Passage ein und aus', () => {
    const calls = [];
    const formats = {};
    const quill = {
        getFormat: () => ({ ...formats }),
        formatText: (index, length, name, value, source) => {
            calls.push(['formatText', index, length, name, value, source]);
            formats[name] = value;
        },
        setSelection: (...args) => calls.push(['setSelection', ...args])
    };
    const selection = { index: 4, length: 12 };
    editor.applyToolbarFormat(quill, 'bold', true, selection);
    editor.applyToolbarFormat(quill, 'bold', true, selection);
    editor.applyToolbarFormat(quill, 'italic', true, selection);
    assert.deepEqual(calls, [
        ['formatText', 4, 12, 'bold', true, 'user'],
        ['setSelection', 4, 12, 'silent'],
        ['formatText', 4, 12, 'bold', false, 'user'],
        ['setSelection', 4, 12, 'silent'],
        ['formatText', 4, 12, 'italic', true, 'user'],
        ['setSelection', 4, 12, 'silent']
    ]);
});

test('Farbe und Hintergrundfarbe werden auf die markierte Passage angewendet', () => {
    const calls = [];
    const quill = {
        formatText: (...args) => calls.push(['formatText', ...args]),
        setSelection: (...args) => calls.push(['setSelection', ...args])
    };
    const selection = { index: 6, length: 9 };
    editor.applyToolbarFormat(quill, 'color', '#ff0000', selection);
    editor.applyToolbarFormat(quill, 'background', '#ffff00', selection);
    assert.deepEqual(calls, [
        ['formatText', 6, 9, 'color', '#ff0000', 'user'],
        ['setSelection', 6, 9, 'silent'],
        ['formatText', 6, 9, 'background', '#ffff00', 'user'],
        ['setSelection', 6, 9, 'silent']
    ]);
});

test('Bildbreite wird über Quill geändert und auf die A4-Breite begrenzt', () => {
    const calls = [];
    const quill = {
        getLeaf: () => [{ domNode: { tagName: 'IMG', hasAttribute: () => false } }],
        formatText: (...args) => calls.push(args)
    };
    assert.equal(editor.resizeImage(quill, 7, 230), true);
    assert.equal(editor.resizeImage(quill, 7, 999), true);
    assert.equal(editor.resizeImage(quill, 7, 2), true);
    assert.deepEqual(calls, [
        [7, 1, 'width', '230', 'user'],
        [7, 1, 'width', '670', 'user'],
        [7, 1, 'width', '80', 'user']
    ]);
});

test('Bildbreite ignoriert ungültige Werte und Nicht-Bilder', () => {
    const calls = [];
    const quill = {
        getLeaf: index => [{ domNode: { tagName: index === 1 ? 'IMG' : 'SPAN', hasAttribute: () => false } }],
        formatText: (...args) => calls.push(args)
    };
    assert.equal(editor.resizeImage(quill, 1, 'abc'), false);
    assert.equal(editor.resizeImage(quill, 2, 240), false);
    assert.deepEqual(calls, []);
});

test('Bildbreite entfernt feste Höhe, damit das Seitenverhältnis erhalten bleibt', () => {
    const calls = [];
    const quill = {
        getLeaf: () => [{ domNode: { tagName: 'IMG', hasAttribute: name => name === 'height' } }],
        formatText: (...args) => calls.push(args)
    };
    assert.equal(editor.resizeImage(quill, 3, 290), true);
    assert.deepEqual(calls, [
        [3, 1, 'width', '290', 'user'],
        [3, 1, 'height', false, 'user']
    ]);
});

test('markierter normaler Text übernimmt keinen alten Arial-Black-Wert', () => {
    const sticky = { font: 'arial-black', size: '20px' };
    let updates = 0;
    const quill = { getSelection: () => ({ index: 3, length: 10 }) };
    assert.equal(editor.syncSelectionFormats(quill, sticky, ['font', 'size'], () => updates++), true);
    assert.equal(updates, 1);
    assert.equal(sticky.font, 'arial-black');
});

test('Cursor im bestehenden normalen Text löscht veraltete Schrift, aber nicht die Größe', () => {
    const sticky = { font: 'arial-black', size: '20px' };
    let updates = 0;
    const quill = {
        getSelection: () => ({ index: 3, length: 0 }),
        getLine: () => [{ domNode: { textContent: 'Texte normal' } }],
        getFormat: () => ({ size: '20px' })
    };
    assert.equal(editor.syncSelectionFormats(quill, sticky, ['font', 'size'], () => updates++), true);
    assert.equal(updates, 1);
    assert.equal(sticky.font, undefined);
    assert.equal(sticky.size, '20px');
});

test('Toolbar liegt über A4-Inhalt und Dropdown ist scrollbar', () => {
    assert.match(css, /\.halloapp-richtext \.ql-toolbar\.ql-snow\s*\{[^}]*z-index:\s*3\s*;/);
    assert.match(css, /\.halloapp-richtext \.ql-toolbar \.ql-picker-options\s*\{[^}]*overflow-y:\s*auto\s*;/);
    assert.match(css, /\.ql-font-arial-black\s*\{[^}]*font-weight:\s*900\s*;/);
    assert.ok(css.indexOf('.halloapp-image-size .halloapp-image-align') > css.indexOf('/* ===== Rich-Text-Editor'));
});
