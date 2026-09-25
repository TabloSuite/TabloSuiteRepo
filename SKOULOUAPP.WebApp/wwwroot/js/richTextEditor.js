// JS-Interop für den Quill-basierten Rich-Text-Editor.
// Spiegelt das Muster von datePicker.js wider und wird von der
// RichTextEditor.razor-Komponente aufgerufen.
window.SKOULOUAPPRichText = {
    instances: {},
    favoriteFormulas: [],
    favoriteFormulasLoaded: false,

    // Maximal erlaubte Bildgröße für eingebettete data:-URLs (5 MB).
    maxImageBytes: 5 * 1024 * 1024,
    // Die gesamte HTML-Nachricht des Editors darf das konfigurierte
    // SignalR-Limit in Program.cs (8 MB) nicht überschreiten. Wegen des
    // Protokoll-Overheads bleibt ein kleiner Sicherheitsabstand.
    maxEditorContentBytes: 7 * 1024 * 1024,
    // Erlaubte Bildtypen für Einfügen per Datei/Paste.
    allowedImageTypes: ['image/png', 'image/jpeg', 'image/gif', 'image/webp'],

    // Auswählbare Schriftarten (Wert = CSS-Klassensuffix von Quill: ql-font-<wert>).
    fontWhitelist: ['sans-serif', 'serif', 'monospace', 'inter', 'roboto', 'nunito', 'arial', 'arial-black', 'georgia', 'times-new-roman', 'comic-sans', 'courier-new'],
    // Konkrete Größen plus relative Quill-Stufen; 16 px ist der Standard.
    sizeWhitelist: ['10px', '12px', '14px', '16px', '18px', '20px', '24px', '28px', '32px', '36px', '48px', 'small', false, 'large', 'huge'],

    formulaTemplates: [
        { group: 'Bases', label: 'Fraction', preview: '\\frac{a}{b}', latex: '\\frac{a}{b}' },
        { group: 'Bases', label: 'Fraction avec somme', preview: '\\frac{a+b}{c+d}', latex: '\\frac{a+b}{c+d}' },
        { group: 'Bases', label: 'Fraction mixte', preview: 'a\\frac{b}{c}', latex: 'a\\frac{b}{c}' },
        { group: 'Bases', label: 'Division', preview: 'a\\div b', latex: 'a\\div b' },
        { group: 'Bases', label: 'Multiplication', preview: 'a\\cdot b', latex: 'a\\cdot b' },
        { group: 'Bases', label: 'Pourcentage', preview: 'p\\%', latex: 'p\\%' },
        { group: 'Puissances & racines', label: 'Exposant', preview: 'x^{n}', latex: 'x^{n}' },
        { group: 'Puissances & racines', label: 'Indice', preview: 'a_{n}', latex: 'a_{n}' },
        { group: 'Puissances & racines', label: 'Racine carrée', preview: '\\sqrt{x}', latex: '\\sqrt{x}' },
        { group: 'Puissances & racines', label: 'Racine n-ième', preview: '\\sqrt[n]{x}', latex: '\\sqrt[n]{x}' },
        { group: 'Puissances & racines', label: 'Puissance de 10', preview: 'a\\times10^{n}', latex: 'a\\times10^{n}' },
        { group: 'Puissances & racines', label: 'Exponentielle', preview: 'e^{x}', latex: 'e^{x}' },
        { group: 'Équations & fonctions', label: 'Équation', preview: 'ax^{2}+bx+c=0', latex: 'ax^{2}+bx+c=0' },
        { group: 'Équations & fonctions', label: 'Solution carré', preview: 'x=\\frac{-b\\pm\\sqrt{b^{2}-4ac}}{2a}', latex: 'x=\\frac{-b\\pm\\sqrt{b^{2}-4ac}}{2a}' },
        { group: 'Équations & fonctions', label: 'Fonction', preview: 'f(x)=ax+b', latex: 'f(x)=ax+b' },
        { group: 'Équations & fonctions', label: 'Parabole', preview: 'f(x)=ax^{2}+bx+c', latex: 'f(x)=ax^{2}+bx+c' },
        { group: 'Équations & fonctions', label: 'Système', preview: '\\begin{cases}ax+by=c\\\\dx+ey=f\\end{cases}', latex: '\\begin{cases}ax+by=c\\\\dx+ey=f\\end{cases}' },
        { group: 'Équations & fonctions', label: 'Valeur absolue', preview: '|x|', latex: '|x|' },
        { group: 'Équations & fonctions', label: 'Logarithme', preview: '\\log_{a}(x)', latex: '\\log_{a}(x)' },
        { group: 'Équations & fonctions', label: 'Sinus', preview: '\\sin(x)', latex: '\\sin(x)' },
        { group: 'Équations & fonctions', label: 'Cosinus', preview: '\\cos(x)', latex: '\\cos(x)' },
        { group: 'Équations & fonctions', label: 'Tangente', preview: '\\tan(x)', latex: '\\tan(x)' },
        { group: 'Géométrie & vecteurs', label: 'Vecteur', preview: '\\vec{AB}', latex: '\\vec{AB}' },
        { group: 'Géométrie & vecteurs', label: 'Distance', preview: '|AB|', latex: '|AB|' },
        { group: 'Géométrie & vecteurs', label: 'Angle', preview: '\\widehat{ABC}', latex: '\\widehat{ABC}' },
        { group: 'Géométrie & vecteurs', label: 'Pythagore', preview: 'a^{2}+b^{2}=c^{2}', latex: 'a^{2}+b^{2}=c^{2}' },
        { group: 'Géométrie & vecteurs', label: 'Droite', preview: 'y=mx+p', latex: 'y=mx+p' },
        { group: 'Géométrie & vecteurs', label: 'Cercle', preview: '(x-a)^{2}+(y-b)^{2}=r^{2}', latex: '(x-a)^{2}+(y-b)^{2}=r^{2}' },
        { group: 'Géométrie & vecteurs', label: 'Matrice 2 × 2', preview: '\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}', latex: '\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}' },
        { group: 'Géométrie & vecteurs', label: 'Matrice 3 × 3', preview: '\\begin{pmatrix}a&b&c\\\\d&e&f\\\\g&h&i\\end{pmatrix}', latex: '\\begin{pmatrix}a&b&c\\\\d&e&f\\\\g&h&i\\end{pmatrix}' },
        { group: 'Analyse', label: 'Limite', preview: '\\lim_{x\\to a} f(x)', latex: '\\lim_{x\\to a} f(x)' },
        { group: 'Analyse', label: 'Dérivée', preview: 'f\'(x)', latex: 'f\'(x)' },
        { group: 'Analyse', label: 'Dérivée fraction', preview: '\\frac{dy}{dx}', latex: '\\frac{dy}{dx}' },
        { group: 'Analyse', label: 'Dérivée seconde', preview: 'f\'\'(x)', latex: 'f\'\'(x)' },
        { group: 'Analyse', label: 'Intégrale', preview: '\\int_{a}^{b} f(x)\\,dx', latex: '\\int_{a}^{b} f(x)\\,dx' },
        { group: 'Analyse', label: 'Primitive', preview: 'F(x)=\\int f(x)\\,dx', latex: 'F(x)=\\int f(x)\\,dx' },
        { group: 'Analyse', label: 'Somme', preview: '\\sum_{i=1}^{n} x_{i}', latex: '\\sum_{i=1}^{n} x_{i}' },
        { group: 'Analyse', label: 'Suite', preview: 'u_{n+1}=f(u_{n})', latex: 'u_{n+1}=f(u_{n})' },
        { group: 'Analyse', label: 'Suite géométrique', preview: 'u_{n}=u_{0}q^{n}', latex: 'u_{n}=u_{0}q^{n}' },
        { group: 'Ensembles & probabilités', label: 'Ensemble', preview: '\\{x\\in\\mathbb{R}\\mid x>0\\}', latex: '\\{x\\in\\mathbb{R}\\mid x>0\\}' },
        { group: 'Ensembles & probabilités', label: 'Union / intersection', preview: 'A\\cup B,\\ A\\cap B', latex: 'A\\cup B,\\ A\\cap B' },
        { group: 'Ensembles & probabilités', label: 'Probabilité', preview: 'P(A)=\\frac{n(A)}{n(\\Omega)}', latex: 'P(A)=\\frac{n(A)}{n(\\Omega)}' },
        { group: 'Ensembles & probabilités', label: 'Binomiale', preview: 'P(X=k)=\\binom{n}{k}p^{k}(1-p)^{n-k}', latex: 'P(X=k)=\\binom{n}{k}p^{k}(1-p)^{n-k}' }
        ,{ group: 'Ensembles & probabilités', label: 'Moyenne', preview: '\\overline{x}=\\frac{1}{n}\\sum_{i=1}^{n}x_{i}', latex: '\\overline{x}=\\frac{1}{n}\\sum_{i=1}^{n}x_{i}' }
        ,{ group: 'Ensembles & probabilités', label: 'Écart type', preview: '\\sigma=\\sqrt{\\frac{1}{n}\\sum_{i=1}^{n}(x_{i}-\\overline{x})^{2}}', latex: '\\sigma=\\sqrt{\\frac{1}{n}\\sum_{i=1}^{n}(x_{i}-\\overline{x})^{2}}' }
        ,{ group: 'Ensembles & probabilités', label: 'Factorielle', preview: 'n!=n\\cdot(n-1)\\cdots1', latex: 'n!=n\\cdot(n-1)\\cdots1' }
    ],

    ensureFavoriteFormulasLoaded: async function () {
        if (this.favoriteFormulasLoaded) {
            return this.favoriteFormulas;
        }

        try {
            const response = await fetch('/api/formules/favoris', {
                method: 'GET',
                credentials: 'include',
                headers: { 'Accept': 'application/json' }
            });

            if (!response.ok) {
                this.favoriteFormulas = [];
                this.favoriteFormulasLoaded = true;
                return this.favoriteFormulas;
            }

            const data = await response.json();
            this.favoriteFormulas = Array.isArray(data)
                ? data.map(function (item) {
                    return {
                        id: item.id || item.Id || null,
                        latex: item.latex || item.Latex || '',
                        label: item.bezeichnung || item.Bezeichnung || item.latex || item.Latex || '',
                        preview: item.vorschau || item.Vorschau || item.latex || item.Latex || ''
                    };
                }).filter(function (item) { return !!item.latex; })
                : [];
        } catch {
            this.favoriteFormulas = [];
        }

        this.favoriteFormulasLoaded = true;
        return this.favoriteFormulas;
    },

    isFormulaFavorite: function (latex) {
        if (!latex) {
            return false;
        }

        return this.favoriteFormulas.some(function (item) {
            return item.latex === latex;
        });
    },

    isBuiltInFormula: function (latex) {
        if (!latex) {
            return false;
        }

        return this.formulaTemplates.some(function (template) {
            return template.latex === latex;
        });
    },

    addFormulaFavorite: async function (template) {
        if (!template || !template.latex) {
            return false;
        }

        try {
            const response = await fetch('/api/formules/favoris', {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify({
                    latex: template.latex,
                    bezeichnung: template.label,
                    vorschau: template.preview || template.latex
                })
            });

            if (!response.ok) {
                return false;
            }

            const item = await response.json();
            const normalized = {
                id: item.id || item.Id || null,
                latex: item.latex || item.Latex || template.latex,
                label: item.bezeichnung || item.Bezeichnung || template.label || template.latex,
                preview: item.vorschau || item.Vorschau || template.preview || template.latex
            };

            this.favoriteFormulas = this.favoriteFormulas.filter(function (x) {
                return x.latex !== normalized.latex;
            });
            this.favoriteFormulas.push(normalized);
            this.favoriteFormulas.sort(function (a, b) {
                return (a.label || '').localeCompare((b.label || ''), 'fr', { sensitivity: 'base' });
            });
            this.favoriteFormulasLoaded = true;
            return true;
        } catch {
            return false;
        }
    },

    removeFormulaFavorite: async function (latex) {
        if (!latex) {
            return false;
        }

        try {
            const response = await fetch('/api/formules/favoris?latex=' + encodeURIComponent(latex), {
                method: 'DELETE',
                credentials: 'include'
            });

            if (!response.ok && response.status !== 404) {
                return false;
            }

            this.favoriteFormulas = this.favoriteFormulas.filter(function (item) {
                return item.latex !== latex;
            });
            this.favoriteFormulasLoaded = true;
            return true;
        } catch {
            return false;
        }
    },

    // Registriert Schriftarten und Größen einmalig bei Quill.
    ensureFormatsRegistered: function () {
        if (this._formatsRegistered || typeof Quill === 'undefined') {
            return;
        }

        const FontAttributor = Quill.import('attributors/class/font');
        FontAttributor.whitelist = this.fontWhitelist.filter(function (f) { return f; });
        Quill.register(FontAttributor, true);

        const SizeAttributor = Quill.import('attributors/class/size');
        SizeAttributor.whitelist = this.sizeWhitelist.filter(function (s) { return s; });
        Quill.register(SizeAttributor, true);

        this._formatsRegistered = true;
    },

    init: function (element, dotNetRef, initialHtml, placeholder, readOnly, enableImages, maxHeightPx, defaultFont, defaultSize, paged) {
        if (!element || typeof Quill === 'undefined') {
            return false;
        }

        // Bestehende Instanz aufräumen, falls vorhanden.
        this.destroy(element);

        this.ensureFormatsRegistered();

        const self = this;

        const toolbarOptions = readOnly ? false : [
            [{ 'font': this.fontWhitelist }, { 'size': this.sizeWhitelist }],
            ['bold', 'italic', 'underline', 'strike'],
            [{ 'color': [] }, { 'background': [] }],
            [{ 'align': '' }, { 'align': 'center' }, { 'align': 'right' }],
            [{ 'list': 'ordered' }, { 'list': 'bullet' }],
            ['blockquote', 'formula'],
            enableImages ? ['link', 'image'] : ['link'],
            ['clean']
        ];

        // Inline-Formate, die über einen Zeilenumbruch hinweg erhalten bleiben sollen.
        const inlineKeys = ['size', 'font', 'color', 'background', 'bold', 'italic', 'underline', 'strike'];

        const modules = { toolbar: toolbarOptions };

        const quill = new Quill(element, {
            theme: 'snow',
            readOnly: readOnly === true,
            placeholder: placeholder || '',
            modules: modules
        });

        if (readOnly !== true) {
            const toolbar = quill.getModule('toolbar');
            if (toolbar) {
                toolbar.addHandler('formula', function () {
                    self.openFormulaPalette(quill, toolbar.container);
                });
            }
        }

        if (readOnly !== true && !initialHtml && (defaultFont || defaultSize)) {
            quill.setSelection(0, 0, 'silent');
            if (defaultFont) {
                quill.format('font', defaultFont, 'user');
            }
            if (defaultSize) {
                quill.format('size', defaultSize, 'user');
            }
        }

        // Enter-Verhalten: Wir fangen die Enter-Taste in der CAPTURE-Phase ab,
        // bevor Quills eigener Keyboard-Handler reagiert, führen den Umbruch
        // selbst aus und übertragen das aktive Inline-Format (u. a. Schriftgröße,
        // Schriftart, Farbe) auf die neue Zeile. So springt z. B. "Large" nach
        // dem Umbruch nicht auf "Normal" zurück. In Listen/Zitaten/Code-Blöcken
        // überlassen wir das Verhalten Quill (Fortsetzung der Struktur).
        if (readOnly !== true) {
            // Merkt sich das zuletzt aktive Inline-Format ("Sticky Format").
            let stickyFormat = {};
            if (defaultFont) {
                stickyFormat.font = defaultFont;
            }
            if (defaultSize) {
                stickyFormat.size = defaultSize;
            }

            const setPickerValue = function (container, formatName, value) {
                if (!container || value === undefined || value === false) {
                    return;
                }
                const valueText = value.toString();
                const picker = container.querySelector('.ql-picker.ql-' + formatName);
                const nativeSelect = container.querySelector('select.ql-' + formatName);

                if (nativeSelect) {
                    nativeSelect.value = valueText;
                }

                if (picker) {
                    const label = picker.querySelector('.ql-picker-label');
                    const items = picker.querySelectorAll('.ql-picker-item');

                    picker.classList.remove('ql-expanded');
                    picker.classList.toggle('ql-active', true);
                    if (label) {
                        label.setAttribute('data-value', valueText);
                    }

                    items.forEach(function (item) {
                        const isActive = (item.getAttribute('data-value') || '') === valueText;
                        item.classList.toggle('ql-selected', isActive);
                    });
                }
            };

            const syncStickyToolbar = function () {
                const toolbarModule = quill.getModule('toolbar');
                const toolbar = toolbarModule?.container || quill.container.previousElementSibling;
                if (!toolbar) {
                    return;
                }

                setPickerValue(toolbar, 'size', stickyFormat.size);
                setPickerValue(toolbar, 'font', stickyFormat.font);
            };

            // Aktualisiert die Toolbar-Anzeige (Dropdowns/Buttons) anhand der
            // aktuellen Cursor-Formatierung. Wenn Quill auf einer leeren Zeile
            // kein Format meldet, setzen wir Größe/Schriftart aus stickyFormat.
            const updateToolbar = function () {
                const toolbarModule = quill.getModule('toolbar');
                const sel = quill.getSelection();
                if (toolbarModule && sel) {
                    toolbarModule.update(sel);
                    const fmt = quill.getFormat(sel);
                    if (sel.length === 0 && (!fmt.size || !fmt.font)) {
                        syncStickyToolbar();
                    }
                }
            };

            // Wendet das gemerkte Format an, wenn Quill am Cursor
            // (z. B. auf einer leeren Zeile) kein Format meldet.
            const applyStickyFormat = function () {
                const sel = quill.getSelection();
                if (!sel || sel.length !== 0) {
                    return;
                }
                const fmt = quill.getFormat(sel);
                let applied = false;
                inlineKeys.forEach(function (key) {
                    if (stickyFormat[key] !== undefined && fmt[key] === undefined) {
                        quill.format(key, stickyFormat[key], 'user');
                        applied = true;
                    }
                });
                updateToolbar();
            };

            // Bei jeder Selektions-/Textänderung das aktive Format merken bzw.
            // auf leeren Zeilen sofort wiederherstellen (verhindert das kurze
            // Zurückspringen der Toolbar auf "Normal").
            quill.on('editor-change', function () {
                const sel = quill.getSelection();
                if (!sel) {
                    return;
                }
                const fmt = quill.getFormat(sel);
                const hasInline = inlineKeys.some(function (key) {
                    return fmt[key] !== undefined && fmt[key] !== false;
                });
                if (hasInline) {
                    // Aktives Format merken.
                    inlineKeys.forEach(function (key) {
                        if (fmt[key] !== undefined && fmt[key] !== false) {
                            stickyFormat[key] = fmt[key];
                        } else {
                            delete stickyFormat[key];
                        }
                    });
                } else if (sel.length === 0) {
                    // Leere Zeile ohne Format: gemerktes Format wiederherstellen.
                    setTimeout(applyStickyFormat, 0);
                }
            });

            quill.root.addEventListener('keydown', function (e) {
                if (e.key !== 'Enter' || e.shiftKey || e.isComposing) {
                    return;
                }

                const range = quill.getSelection();
                if (!range) {
                    return;
                }

                const fmt = quill.getFormat(range.index, range.length);
                if (fmt.list || fmt.blockquote || fmt['code-block']) {
                    return; // Standardverhalten von Quill beibehalten.
                }

                // Quills eigenen Enter-Handler unterbinden.
                e.preventDefault();
                e.stopImmediatePropagation();

                const carry = {};
                inlineKeys.forEach(function (key) {
                    if (fmt[key] !== undefined && fmt[key] !== false) {
                        carry[key] = fmt[key];
                        stickyFormat[key] = fmt[key];
                    }
                });

                // Eventuelle Auswahl ersetzen und Zeilenumbruch einfügen.
                if (range.length > 0) {
                    quill.deleteText(range.index, range.length, 'user');
                }
                quill.insertText(range.index, '\n', 'user');
                quill.setSelection(range.index + 1, 0, 'silent');

                inlineKeys.forEach(function (key) {
                    if (carry[key] !== undefined) {
                        quill.format(key, carry[key], 'user');
                    }
                });

                // Toolbar-Anzeige direkt aus dem getragenen Format synchronisieren.
                // updateToolbar() liest auf der leeren neuen Zeile wieder "Normal",
                // daher hier bewusst stickyFormat verwenden.
                syncStickyToolbar();
                setTimeout(syncStickyToolbar, 0);
                requestAnimationFrame(syncStickyToolbar);
            }, true); // true = Capture-Phase

            // Startzustand: Quill zeigt auf leerem Cursor sonst häufig "Normal",
            // obwohl das nächste Zeichen bereits mit stickyFormat geschrieben wird.
            setTimeout(function () {
                applyStickyFormat();
                syncStickyToolbar();
            }, 0);

        }

        // Bild-Handler: direkte Datei-/Paste-/Base64-Bilder sind deaktiviert.
        // Bilder dürfen nur über die von Blazor bereitgestellte Azure-Bibliothek
        // als gespeicherte URL eingefügt werden.
        if (enableImages && readOnly !== true) {
            const toolbar = quill.getModule('toolbar');
            if (toolbar) {
                toolbar.addHandler('image', function () {
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OpenImageLibraryAsync');
                    }
                });
            }

            // Bilder per Copy/Paste bewusst blockieren, damit keine Base64-Daten
            // mehr im Editor landen.
            quill.root.addEventListener('paste', function (e) {
                const items = (e.clipboardData || e.originalEvent?.clipboardData)?.items;
                if (!items) {
                    return;
                }
                for (let i = 0; i < items.length; i++) {
                    if (items[i].type && items[i].type.indexOf('image') === 0) {
                        e.preventDefault();
                        alert("Les images doivent être choisies dans la bibliothèque Azure Storage.");
                        return;
                    }
                }
            });
        }

        if (initialHtml) {
            const delta = quill.clipboard.convert({ html: initialHtml });
            quill.setContents(delta, 'silent');
        }

        if (readOnly !== true) {
            self.setupFormulaEditing(quill);
        }

        if (typeof window.SKOULOUAPPFormulas !== 'undefined') {
            window.SKOULOUAPPFormulas.render();
        }

        if (dotNetRef && readOnly !== true) {
            if (paged === true) {
                // Word-ähnlicher Modus: EIN Quill-Editor für die gesamte Lektion.
                // Die A4-Seiten werden rein visuell dargestellt; Inhalt wird nie
                // zwischen Editoren verschoben, sodass der Cursor stabil bleibt.
                self.setupPagedMode(element, quill, dotNetRef);
            } else {
                quill.on('text-change', function () {
                    self.normalizeLinks(quill.root);
                    dotNetRef.invokeMethodAsync('OnContentChanged', quill.root.innerHTML);
                });
            }
        }

        element._quillInstance = quill;
        return true;
    },

    // A4-Maße in Pixeln bei 96 DPI. Das Umbruch-Raster entspricht damit exakt
    // den visuell gerenderten A4-Blättern (Word-ähnliche Darstellung).
    a4SheetHeightPx: Math.round(297 / 25.4 * 96),
    a4PaddingPx: Math.round(16 / 25.4 * 96),
    a4GapPx: 40,

    // Nutzbare Inhaltshöhe pro Seite (Blatthöhe abzüglich oberem/unterem Rand).
    a4ContentHeightPx: function () {
        return this.a4SheetHeightPx - 2 * this.a4PaddingPx;
    },

    // Richtet den Word-ähnlichen Seitenmodus ein: visuelle A4-Blätter hinter
    // dem Editor, automatischer Seitenumbruch per Spacer und Rückmeldung der
    // Seitenanzahl an Blazor.
    setupPagedMode: function (element, quill, dotNetRef) {
        const self = this;
        // Der Quill-Container (element) ist die Bühne: die A4-Blätter liegen
        // absolut in ihm, exakt hinter dem .ql-editor-Textbereich.
        element.classList.add('halloapp-paged');

        let sheetLayer = element.querySelector(':scope > .halloapp-page-sheets');
        if (!sheetLayer) {
            sheetLayer = document.createElement('div');
            sheetLayer.className = 'halloapp-page-sheets';
            element.insertBefore(sheetLayer, element.firstChild);
        }

        quill._halloappPaged = { sheetLayer: sheetLayer, pageCount: 0 };

        let scheduled = false;
        const schedule = function () {
            if (scheduled) {
                return;
            }
            scheduled = true;
            requestAnimationFrame(function () {
                scheduled = false;
                self.applyPageBreaks(quill);
                self.renderPageSheets(quill, dotNetRef);
            });
        };

        quill.on('text-change', function () {
            self.normalizeLinks(quill.root);
            schedule();
            dotNetRef.invokeMethodAsync('OnContentChanged', quill.root.innerHTML);
        });

        // Cursor stets im sichtbaren Bereich halten.
        quill.root.addEventListener('keydown', function () {
            requestAnimationFrame(function () { self.scrollCursorIntoView(quill); });
        });

        if (!self._pagedResizeBound) {
            self._pagedResizeBound = true;
            window.addEventListener('resize', function () {
                document.querySelectorAll('.halloapp-paged').forEach(function (containerEl) {
                    const q = containerEl._quillInstance;
                    if (q && q._halloappPaged) {
                        self.renderPageSheets(q, null);
                    }
                });
            });
        }

        // Für den Resize-Handler eine Referenz von der Editor-Wurzel auf Quill.
        quill.root.__quill = quill;

        // Initiale Vermessung nach dem ersten Layout.
        requestAnimationFrame(schedule);
    },

    // Fügt vor Blöcken, die eine A4-Seitengrenze überschreiten würden, einen
    // Spacer ein, sodass sie visuell exakt auf der nächsten Seite beginnen.
    // Der Algorithmus akkumuliert Höhen manuell und misst nie nach einer
    // DOM-Änderung – so entsteht keine Drift.
    applyPageBreaks: function (quill) {
        const root = quill.root;
        const contentPerPage = this.a4ContentHeightPx();
        const gap = this.a4GapPx;
        const pad = this.a4PaddingPx;
        if (!contentPerPage || contentPerPage <= 0) {
            return 1;
        }

        const blocks = Array.from(root.children);
        // Zuvor gesetzte Umbrüche zurücksetzen und Naturhöhen erfassen.
        const heights = blocks.map(function (block) {
            block.style.marginTop = '';
            block.classList.remove('halloapp-page-break-before');
            const rect = block.getBoundingClientRect();
            return rect.height;
        });

        let used = 0;
        let pageCount = 1;

        blocks.forEach(function (block, index) {
            const height = heights[index];

            // Passt der Block nicht mehr auf die aktuelle Seite und ist die
            // Seite nicht leer, beginnt er auf der nächsten Seite.
            if (used > 0 && used + height > contentPerPage) {
                const spacer = (contentPerPage - used) + 2 * pad + gap;
                block.style.marginTop = spacer + 'px';
                block.classList.add('halloapp-page-break-before');
                pageCount += 1;
                used = 0;
            }

            used += height;

            // Ein Block, der höher als eine ganze Seite ist, belegt weitere Seiten.
            while (used > contentPerPage) {
                used -= contentPerPage;
                pageCount += 1;
            }
        });

        return pageCount;
    },

    // Zeichnet die weißen A4-Blätter passend zur Seitenanzahl und meldet sie
    // an Blazor.
    renderPageSheets: function (quill, dotNetRef) {
        const state = quill._halloappPaged;
        if (!state) {
            return;
        }

        const sheetHeight = this.a4SheetHeightPx;
        const gap = this.a4GapPx;
        const pageCount = this.applyPageBreaks(quill);

        if (state.pageCount !== pageCount) {
            state.pageCount = pageCount;
            const layer = state.sheetLayer;
            layer.innerHTML = '';
            for (let i = 0; i < pageCount; i++) {
                const sheet = document.createElement('div');
                sheet.className = 'halloapp-page-sheet';
                sheet.style.top = (i * (sheetHeight + gap)) + 'px';
                const footer = document.createElement('div');
                footer.className = 'halloapp-page-sheet-footer';
                footer.textContent = 'Page ' + (i + 1);
                sheet.appendChild(footer);
                layer.appendChild(sheet);
            }
            // Gesamthöhe der Blätter-Ebene festlegen.
            layer.style.height = (pageCount * sheetHeight + (pageCount - 1) * gap) + 'px';

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnPageCountChanged', pageCount);
            }
        }
    },

    // Teilt den aktuellen Editorinhalt an den gemessenen A4-Grenzen in ein
    // Array von Seiten-HTML-Strings auf (für Speichern & PDF-Export).
    splitIntoPages: function (element) {
        const quill = element && element._quillInstance;
        if (!quill) {
            return [];
        }

        this.applyPageBreaks(quill);
        this.normalizeLinks(quill.root);

        const blocks = Array.from(quill.root.children);
        if (blocks.length === 0) {
            return ['<p><br></p>'];
        }

        const pages = [];
        let current = [];
        blocks.forEach(function (block) {
            if (block.classList.contains('halloapp-page-break-before') && current.length > 0) {
                pages.push(current);
                current = [];
            }
            // Klon ohne die rein visuellen Umbruch-Stile/Klassen speichern.
            const clone = block.cloneNode(true);
            clone.style.marginTop = '';
            clone.classList.remove('halloapp-page-break-before');
            current.push(clone.outerHTML);
        });
        if (current.length > 0) {
            pages.push(current);
        }

        return pages.map(function (parts) {
            return parts.length ? parts.join('') : '<p><br></p>';
        });
    },

    openFormulaPalette: async function (quill, toolbarElement) {
        document.querySelectorAll('.halloapp-formula-palette').forEach(function (palette) { palette.remove(); });

        const savedRange = quill.getSelection(true) || { index: Math.max(0, quill.getLength() - 1), length: 0 };
        const formulaButton = toolbarElement?.querySelector('.ql-formula');
        if (!formulaButton) {
            return;
        }

        const palette = document.createElement('div');
        palette.className = 'halloapp-formula-palette';
        palette.setAttribute('role', 'dialog');
        palette.setAttribute('aria-label', 'Modèles de formules');

        const title = document.createElement('div');
        title.className = 'halloapp-formula-palette-title';
        title.textContent = 'Insérer une formule';
        palette.appendChild(title);

        await this.ensureFavoriteFormulasLoaded();

        const groups = {};
        this.formulaTemplates.forEach(function (template) {
            (groups[template.group] ||= []).push(template);
        });

        const self = this;

        const insertFormula = function (template) {
            const index = Math.min(savedRange.index, Math.max(0, quill.getLength() - 1));
            const activeFormats = quill.getFormat(savedRange);
            quill.focus();
            quill.deleteText(index, savedRange.length, 'user');
            quill.insertEmbed(index, 'formula', template.latex, 'user');
            if (activeFormats.size) {
                quill.formatText(index, 1, 'size', activeFormats.size, 'user');
            }
            quill.insertText(index + 1, ' ', 'user');
            quill.setSelection(index + 2, 0, 'silent');
            palette.remove();
        };

        const renderGroups = function () {
            palette.querySelectorAll('.halloapp-formula-group').forEach(function (node) { node.remove(); });

            const favoriteBuiltIn = self.favoriteFormulas
                .filter(function (x) { return self.isBuiltInFormula(x.latex); })
                .map(function (x) {
                    return {
                        latex: x.latex,
                        label: x.label || x.latex,
                        preview: x.preview || x.latex,
                        group: 'Favoris',
                        favoriteOnly: true
                    };
                });

            const favoriteCustom = self.favoriteFormulas
                .filter(function (x) { return !self.isBuiltInFormula(x.latex); })
                .map(function (x) {
                    return {
                        latex: x.latex,
                        label: x.label || 'Formule personnelle',
                        preview: x.preview || x.latex,
                        group: 'Mes formules',
                        favoriteOnly: true
                    };
                });

            const allGroups = [];
            if (favoriteBuiltIn.length > 0) {
                allGroups.push('Favoris');
            }
            if (favoriteCustom.length > 0) {
                allGroups.push('Mes formules');
            }
            Object.keys(groups).forEach(function (groupName) { allGroups.push(groupName); });

            allGroups.forEach(function (groupName) {
            const group = document.createElement('section');
            group.className = 'halloapp-formula-group';
            const heading = document.createElement('h3');
            heading.textContent = groupName;
            group.appendChild(heading);

            const grid = document.createElement('div');
            grid.className = 'halloapp-formula-grid';
            const entries = groupName === 'Favoris'
                ? favoriteBuiltIn
                : groupName === 'Mes formules'
                    ? favoriteCustom
                    : (groups[groupName] || []);

            entries.forEach(function (template) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'halloapp-formula-template';
                button.title = template.label;
                button.setAttribute('aria-label', template.label);

                const math = document.createElement('span');
                math.className = 'halloapp-formula-preview';
                if (typeof katex !== 'undefined') {
                    try {
                        katex.render(template.preview, math, { throwOnError: false });
                    } catch {
                        math.textContent = template.preview;
                    }
                } else {
                    math.textContent = template.preview;
                }

                const label = document.createElement('span');
                label.className = 'halloapp-formula-label';
                label.textContent = template.label;
                const isCustomEntry = template.group === 'Mes formules';
                const favoriteToggle = document.createElement('span');
                favoriteToggle.className = isCustomEntry
                    ? 'halloapp-formula-favorite-toggle halloapp-formula-delete-toggle'
                    : 'halloapp-formula-favorite-toggle';
                favoriteToggle.setAttribute('role', 'button');
                favoriteToggle.setAttribute('tabindex', '0');
                favoriteToggle.setAttribute('aria-label', isCustomEntry ? 'Supprimer la formule' : 'Ajouter ou retirer des favoris');
                favoriteToggle.title = isCustomEntry ? 'Supprimer' : 'Favori';
                const setFavoriteVisual = function (isFavorite) {
                    if (isCustomEntry) {
                        favoriteToggle.textContent = '🗑';
                        favoriteToggle.classList.add('is-active');
                        return;
                    }
                    favoriteToggle.textContent = isFavorite ? '★' : '☆';
                    favoriteToggle.classList.toggle('is-active', isFavorite);
                };
                setFavoriteVisual(self.isFormulaFavorite(template.latex));

                favoriteToggle.addEventListener('click', async function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    if (isCustomEntry) {
                        const removedCustom = await self.removeFormulaFavorite(template.latex);
                        if (removedCustom) {
                            renderGroups();
                        }
                        return;
                    }

                    const isFavorite = self.isFormulaFavorite(template.latex);
                    if (isFavorite) {
                        const removed = await self.removeFormulaFavorite(template.latex);
                        if (removed) {
                            setFavoriteVisual(false);
                            renderGroups();
                        }
                    } else {
                        const added = await self.addFormulaFavorite(template);
                        if (added) {
                            setFavoriteVisual(true);
                            renderGroups();
                        }
                    }
                });

                favoriteToggle.addEventListener('keydown', async function (event) {
                    if (event.key !== 'Enter' && event.key !== ' ') {
                        return;
                    }

                    event.preventDefault();
                    favoriteToggle.click();
                });

                button.append(favoriteToggle, math, label);
                button.addEventListener('click', function () {
                    insertFormula(template);
                });
                grid.appendChild(button);
            });
            group.appendChild(grid);
            palette.appendChild(group);
        });
        };

        renderGroups();

        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'halloapp-formula-palette-close';
        close.textContent = 'Fermer';
        close.addEventListener('click', function () { palette.remove(); });
        palette.appendChild(close);

        const paletteWidth = Math.min(520, window.innerWidth - 24);
        const maxHeight = Math.min(window.innerHeight * 0.76, 680);
        const top = Math.max(12, (window.innerHeight - maxHeight) / 2);
        const left = Math.max(12, (window.innerWidth - paletteWidth) / 2);
        palette.style.top = top + 'px';
        palette.style.left = left + 'px';
        document.body.appendChild(palette);

        requestAnimationFrame(function () {
            const dismiss = function (event) {
                if (!palette.contains(event.target) && event.target !== formulaButton && !formulaButton.contains(event.target)) {
                    palette.remove();
                    document.removeEventListener('mousedown', dismiss, true);
                }
            };
            document.addEventListener('mousedown', dismiss, true);
        });
    },

    setupFormulaEditing: function (quill) {
        quill.root.addEventListener('click', function (event) {
            const formula = event.target.closest('.ql-formula[data-value]');
            if (!formula || !quill.root.contains(formula)) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            window.SKOULOUAPPRichText.openFormulaEditor(quill, formula);
        });
    },

    openFormulaEditor: function (quill, formulaElement) {
        document.querySelectorAll('.halloapp-formula-editor').forEach(function (editor) { editor.remove(); });

        const blot = Quill.find(formulaElement);
        const index = blot ? quill.getIndex(blot) : -1;
        if (index < 0) {
            return;
        }

        const editor = document.createElement('div');
        editor.className = 'halloapp-formula-editor';
        editor.setAttribute('role', 'dialog');
        editor.setAttribute('aria-label', 'Modifier la formule');

        const title = document.createElement('label');
        title.className = 'halloapp-formula-editor-title';
        title.textContent = 'Modifier la formule (LaTeX)';

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'halloapp-formula-editor-input';
        input.value = formulaElement.getAttribute('data-value') || '';
        input.setAttribute('aria-label', 'Expression LaTeX');

        const preview = document.createElement('div');
        preview.className = 'halloapp-formula-editor-preview';
        const renderPreview = function () {
            preview.replaceChildren();
            if (typeof katex !== 'undefined') {
                try {
                    katex.render(input.value || '\\text{Formule vide}', preview, { throwOnError: false });
                    return;
                } catch {
                    // Affiche le texte brut si l'expression est incomplète.
                }
            }
            preview.textContent = input.value;
        };
        input.addEventListener('input', renderPreview);
        renderPreview();

        const actions = document.createElement('div');
        actions.className = 'halloapp-formula-editor-actions';
        const self = this;

        const favoriteToggle = document.createElement('button');
        favoriteToggle.type = 'button';
        favoriteToggle.className = 'halloapp-formula-editor-favorite';
        const setFavoriteEditorVisual = function (isFavorite) {
            favoriteToggle.textContent = isFavorite ? '☑ Ajoutée à mes formules' : '☐ Ajouter à mes formules';
        };
        setFavoriteEditorVisual(self.isFormulaFavorite(input.value.trim()));

        favoriteToggle.addEventListener('click', async function () {
            const latex = input.value.trim();
            if (!latex) {
                input.focus();
                return;
            }

            const isFavorite = self.isFormulaFavorite(latex);
            if (isFavorite) {
                const removed = await self.removeFormulaFavorite(latex);
                if (removed) {
                    setFavoriteEditorVisual(false);
                }
                return;
            }

            const added = await self.addFormulaFavorite({
                latex: latex,
                label: 'Formule personnelle',
                preview: latex
            });
            if (added) {
                setFavoriteEditorVisual(true);
            }
        });

        input.addEventListener('input', function () {
            setFavoriteEditorVisual(self.isFormulaFavorite(input.value.trim()));
        });

        const cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.className = 'halloapp-formula-editor-cancel';
        cancel.textContent = 'Annuler';
        cancel.addEventListener('click', function () { editor.remove(); });

        const save = document.createElement('button');
        save.type = 'button';
        save.className = 'halloapp-formula-editor-save';
        save.textContent = 'Enregistrer';
        const apply = async function () {
            const latex = input.value.trim();
            if (!latex) {
                input.focus();
                return;
            }

            const formulaFormats = quill.getFormat(index, 1);
            quill.deleteText(index, 1, 'user');
            quill.insertEmbed(index, 'formula', latex, 'user');
            if (formulaFormats.size) {
                quill.formatText(index, 1, 'size', formulaFormats.size, 'user');
            }
            quill.setSelection(index + 1, 0, 'silent');
            quill.focus();
            editor.remove();
        };
        save.addEventListener('click', function () { void apply(); });
        input.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') {
                event.preventDefault();
                void apply();
            } else if (event.key === 'Escape') {
                editor.remove();
            }
        });

        const primaryActions = document.createElement('div');
        primaryActions.className = 'halloapp-formula-editor-primary-actions';
        primaryActions.append(cancel, save);

        actions.append(favoriteToggle, primaryActions);
        editor.append(title, input, preview, actions);
        document.body.appendChild(editor);

        editor.style.top = '50%';
        editor.style.left = '50%';
        editor.style.transform = 'translate(-50%, -50%)';
        input.focus();
        input.select();
    },

    normalizeLinks: function (root) {
        if (!root) {
            return;
        }

        const links = root.querySelectorAll('a[href]');
        links.forEach(function (link) {
            link.setAttribute('target', '_blank');
            link.setAttribute('rel', 'noopener noreferrer');
        });
    },

    setHtml: function (element, html) {
        if (!element || !element._quillInstance) {
            return;
        }

        const quill = element._quillInstance;
        const selection = quill.getSelection();
        const delta = quill.clipboard.convert({ html: html || '' });
        quill.setContents(delta, 'silent');
        this.normalizeLinks(quill.root);

        // Blazor rendert während des Seitenflusses den gebundenen HTML-Wert
        // erneut. Quill verliert dabei sonst seine Auswahl und setzt den Cursor
        // an den Zeilenanfang. Nur die vorhandene, gültige Position wiederherstellen.
        if (selection) {
            const newLength = Math.max(0, quill.getLength() - 1);
            const index = Math.min(selection.index, newLength);
            const length = Math.min(selection.length, Math.max(0, newLength - index));
            quill.setSelection(index, length, 'silent');
        }
    },

    getHtml: function (element) {
        if (!element || !element._quillInstance) {
            return '';
        }

        this.normalizeLinks(element._quillInstance.root);
        return element._quillInstance.root.innerHTML;
    },

    focusPageAtEnd: function (pageIndex) {
        const focusPreviousEditor = function (remainingAttempts) {
            const editor = document.querySelector('[data-lesson-page-index="' + pageIndex + '"] .ql-editor');
            const quill = editor?.parentElement?._quillInstance;
            if (!quill) {
                if (remainingAttempts > 0) {
                    requestAnimationFrame(function () { focusPreviousEditor(remainingAttempts - 1); });
                }
                return;
            }

            const end = Math.max(0, quill.getLength() - 1);
            quill.focus();
            quill.setSelection(end, 0, 'silent');
            requestAnimationFrame(function () {
                editor.scrollIntoView({ block: 'center', behavior: 'smooth' });
            });
        };

        requestAnimationFrame(function () { focusPreviousEditor(5); });
    },

    scrollCursorIntoView: function (quill) {
        const selection = quill.getSelection();
        if (!selection) {
            return;
        }

        const cursorBounds = quill.getBounds(selection.index, 0);
        const editorBounds = quill.root.getBoundingClientRect();
        const cursorTop = editorBounds.top + cursorBounds.top;
        const cursorBottom = cursorTop + Math.max(cursorBounds.height, 20);
        const safeTop = 96;
        const safeBottom = window.innerHeight - 64;

        if (cursorTop < safeTop || cursorBottom > safeBottom) {
            const targetTop = window.scrollY + cursorTop - Math.round(window.innerHeight * 0.45);
            window.scrollTo({ top: Math.max(0, targetTop), behavior: 'smooth' });
        }
    },

    destroy: function (element) {
        if (element && element._quillInstance) {
            element._quillInstance = null;
            // Von Quill hinzugefügte Toolbar entfernen, damit keine Duplikate entstehen.
            const container = element.parentElement;
            if (container) {
                const toolbars = container.querySelectorAll('.ql-toolbar');
                toolbars.forEach(function (tb) { tb.remove(); });
            }
            element.classList.remove('ql-container', 'ql-snow');
            element.innerHTML = '';
        }
    },

    insertImageUrl: function (element, imageUrl, altText) {
        const quill = element?._quillInstance;
        if (!quill || !imageUrl) {
            return;
        }

        const index = quill.getSelection(true)?.index ?? Math.max(0, quill.getLength() - 1);
        quill.insertEmbed(index, 'image', imageUrl, 'user');
        const image = quill.root.querySelector('img[src="' + CSS.escape(imageUrl) + '"]');
        if (image && altText) {
            image.setAttribute('alt', altText);
        }
        quill.setSelection(index + 1, 0, 'silent');
    }
};
