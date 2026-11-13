window.skoulouStudentExerciseFeedback = (() => {
    // Gesprochene Rückmeldung: feste Sätze, EINMAL mit Azure (Stimme Vivienne) erzeugt und als MP3 in
    // wwwroot/audio/antworten/ abgelegt (Werkzeuge/AntwortTon/erzeuge.py). Abgespielt wird über den
    // freigeschalteten AudioContext (zuverlässig auch auf iPhone); nur falls die Dateien fehlen,
    // springt die Browser-Stimme ein. Ohne Vornamen – dieselben Dateien für alle Schüler.
    const SAETZE = {
        richtig: ['richtig-1', 'richtig-2', 'richtig-3', 'richtig-4', 'richtig-5', 'richtig-6'],
        falsch: ['falsch-1', 'falsch-2', 'falsch-3', 'falsch-4', 'falsch-5', 'falsch-6'],
        weiter: ['weiter'],
        gesendet: ['gesendet']
    };
    const ERSATZ_TEXT = {
        richtig: 'Bravo, très bien !',
        falsch: 'Oups, essaie encore !',
        weiter: 'Exercice suivant !',
        gesendet: 'Réponse envoyée !'
    };

    let audioContext;
    const puffer = new Map();      // Name → AudioBuffer
    let laden = null;              // Promise des Vorladens
    const zuletzt = {};            // Gruppe → zuletzt gespielter Name (keine direkte Wiederholung)

    function getAudioContext() {
        audioContext ??= window.SKOULOUAPPAudio?.getAudioContext?.();
        if (!audioContext) {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) {
                return null;
            }

            audioContext = new AudioContextType();
        }
        if (audioContext.state === 'suspended') {
            audioContext.resume().catch(() => { });
        }

        return audioContext;
    }

    function dateiUrl(name) {
        return new URL(`audio/antworten/${name}.mp3`, document.baseURI).toString();
    }

    function vorladen() {
        if (laden) {
            return laden;
        }

        const context = getAudioContext();
        if (!context) {
            laden = Promise.resolve();
            return laden;
        }

        const namen = Object.values(SAETZE).flat();
        laden = Promise.all(namen.map(async name => {
            try {
                const antwort = await fetch(dateiUrl(name), { cache: 'force-cache' });
                if (!antwort.ok) {
                    return;
                }
                const daten = await antwort.arrayBuffer();
                const buffer = await new Promise((ok, fehler) => context.decodeAudioData(daten, ok, fehler));
                puffer.set(name, buffer);
            } catch {
                // Fehlende Datei: Ersatz über die Browser-Stimme.
            }
        }));
        return laden;
    }

    function waehle(gruppe) {
        const namen = SAETZE[gruppe].filter(name => puffer.has(name));
        if (namen.length === 0) {
            return null;
        }
        const auswahl = namen.length > 1 ? namen.filter(name => name !== zuletzt[gruppe]) : namen;
        const name = auswahl[Math.floor(Math.random() * auswahl.length)];
        zuletzt[gruppe] = name;
        return puffer.get(name);
    }

    function playTone(frequency, start, duration, type = 'sine', gainValue = 0.06) {
        const context = getAudioContext();
        if (!context) {
            return;
        }

        const oscillator = context.createOscillator();
        const gain = context.createGain();
        oscillator.type = type;
        oscillator.frequency.setValueAtTime(frequency, context.currentTime + start);
        gain.gain.setValueAtTime(0.0001, context.currentTime + start);
        gain.gain.exponentialRampToValueAtTime(gainValue, context.currentTime + start + 0.03);
        gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + start + duration);
        oscillator.connect(gain);
        gain.connect(context.destination);
        oscillator.start(context.currentTime + start);
        oscillator.stop(context.currentTime + start + duration + 0.03);
    }

    /** Spielt einen Satz der Gruppe ab Sekunde „start“; liefert das Ende (Sekunden ab jetzt). */
    function spieleSatz(gruppe, start) {
        const context = getAudioContext();
        const buffer = context ? waehle(gruppe) : null;
        if (!buffer) {
            sprich(ERSATZ_TEXT[gruppe], start);
            return start + 1.4;
        }

        const quelle = context.createBufferSource();
        const gain = context.createGain();
        gain.gain.value = 0.95;
        quelle.buffer = buffer;
        quelle.connect(gain);
        gain.connect(context.destination);
        quelle.start(context.currentTime + start);
        return start + buffer.duration;
    }

    function sprich(text, start) {
        if (!('speechSynthesis' in window)) {
            return;
        }

        window.setTimeout(() => {
            if (window.speechSynthesis.speaking || window.speechSynthesis.pending) {
                window.speechSynthesis.cancel();
            }

            const utterance = new SpeechSynthesisUtterance(text);
            utterance.lang = 'fr-FR';
            utterance.rate = 1.04;
            utterance.volume = 0.85;
            window.speechSynthesis.speak(utterance);
        }, Math.round(start * 1000));
    }

    /**
     * Rückmeldung nach « Vérifier la réponse ». studentName wird nicht mehr gesprochen (feste Sätze);
     * tonAus = Schüler hat die Rückmeldung im Konto ausgeschaltet → nichts abspielen.
     */
    function play(percent, isCorrect, requiresManualGrading, studentName, hasNextExercise, tonAus) {
        if (tonAus) {
            return;
        }

        try {
            window.SKOULOUAPPAudio?.unlock?.();
            vorladen();

            if (requiresManualGrading) {
                playTone(440, 0, 0.12, 'triangle', 0.045);
                playTone(554, 0.14, 0.12, 'triangle', 0.045);
                spieleSatz('gesendet', 0.32);
                return;
            }

            if (isCorrect || percent >= 100) {
                playTone(523, 0, 0.11, 'sine', 0.07);
                playTone(659, 0.12, 0.11, 'sine', 0.07);
                playTone(784, 0.24, 0.16, 'sine', 0.08);
                const ende = spieleSatz('richtig', 0.45);
                if (hasNextExercise) {
                    spieleSatz('weiter', ende + 0.15);
                }
                return;
            }

            playTone(220, 0, 0.12, 'sawtooth', 0.035);
            playTone(196, 0.16, 0.18, 'sawtooth', 0.03);
            spieleSatz('falsch', 0.38);
        } catch {
            // Audio feedback must never block the exercise flow.
        }
    }

    return { play, vorladen };
})();
