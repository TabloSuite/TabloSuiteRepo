window.SKOULOUAPPAudioTest = (() => {
    const results = {
        html5: 'non testé',
        webaudio: 'non testé',
        tts: 'non testé'
    };

    function report() {
        const element = document.getElementById('halloapp-audio-test-result');
        if (element) {
            element.textContent = `HTML5: ${results.html5} | WebAudio: ${results.webaudio} | TTS: ${results.tts}`;
        }
    }

    // Kurzer 440-Hz-Sinuston als WAV-Data-URL (rein clientseitig, kein Download nötig).
    function buildWavDataUrl() {
        const sampleRate = 8000;
        const seconds = 0.4;
        const total = Math.floor(sampleRate * seconds);
        const buffer = new ArrayBuffer(44 + total * 2);
        const view = new DataView(buffer);
        const writeString = (offset, value) => {
            for (let i = 0; i < value.length; i++) {
                view.setUint8(offset + i, value.charCodeAt(i));
            }
        };

        writeString(0, 'RIFF');
        view.setUint32(4, 36 + total * 2, true);
        writeString(8, 'WAVE');
        writeString(12, 'fmt ');
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        writeString(36, 'data');
        view.setUint32(40, total * 2, true);

        for (let i = 0; i < total; i++) {
            const sample = Math.sin(2 * Math.PI * 440 * (i / sampleRate));
            view.setInt16(44 + i * 2, sample * 12000, true);
        }

        let binary = '';
        const bytes = new Uint8Array(buffer);
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }

        return `data:audio/wav;base64,${btoa(binary)}`;
    }

    let audioElement;

    function testHtml5() {
        try {
            audioElement ??= new Audio(buildWavDataUrl());
            audioElement.currentTime = 0;
            const playPromise = audioElement.play();
            if (playPromise?.then) {
                playPromise
                    .then(() => { results.html5 = 'OK'; report(); })
                    .catch(error => { results.html5 = `échec (${error?.name || 'inconnu'})`; report(); });
            } else {
                results.html5 = 'lancé';
            }
        } catch (error) {
            results.html5 = `échec (${error?.name || 'inconnu'})`;
        }

        report();
    }

    function testWebAudio() {
        try {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) {
                results.webaudio = 'non supporté';
                report();
                return;
            }

            const context = new AudioContextType();
            const finish = () => {
                const oscillator = context.createOscillator();
                const gain = context.createGain();
                oscillator.frequency.value = 660;
                gain.gain.value = 0.05;
                oscillator.connect(gain);
                gain.connect(context.destination);
                oscillator.start();
                oscillator.stop(context.currentTime + 0.3);
                results.webaudio = `OK (${context.state})`;
                report();
            };

            if (context.state === 'suspended') {
                context.resume().then(finish).catch(() => { results.webaudio = 'échec (resume)'; report(); });
            } else {
                finish();
            }
        } catch (error) {
            results.webaudio = `échec (${error?.name || 'inconnu'})`;
            report();
        }
    }

    function testTts() {
        try {
            if (!('speechSynthesis' in window) || typeof SpeechSynthesisUtterance === 'undefined') {
                results.tts = 'non supporté';
                report();
                return;
            }

            window.speechSynthesis.cancel();
            if (window.speechSynthesis.paused) {
                window.speechSynthesis.resume();
            }

            const utterance = new SpeechSynthesisUtterance('Guten Tag');
            utterance.lang = 'de-DE';
            utterance.onstart = () => { results.tts = 'OK (started)'; report(); };
            utterance.onend = () => { results.tts = 'OK (ended)'; report(); };
            utterance.onerror = event => { results.tts = `échec (${event.error || 'inconnu'})`; report(); };
            results.tts = 'queued';
            report();
            window.setTimeout(() => window.speechSynthesis.speak(utterance), 50);
            window.setTimeout(() => {
                if (results.tts === 'queued') {
                    results.tts = 'bloqué (not-started)';
                    report();
                }
            }, 1200);
        } catch (error) {
            results.tts = `échec (${error?.name || 'inconnu'})`;
            report();
        }
    }

    function install() {
        const bind = (id, handler) => {
            const button = document.getElementById(id);
            if (!button || button.dataset.halloappBound === 'true') {
                return;
            }

            button.dataset.halloappBound = 'true';
            ['pointerdown', 'touchstart', 'click'].forEach(eventName => {
                button.addEventListener(eventName, event => {
                    event.preventDefault();
                    event.stopPropagation();
                    handler();
                }, { capture: true });
            });
        };

        bind('halloapp-test-html5', testHtml5);
        bind('halloapp-test-webaudio', testWebAudio);
        bind('halloapp-test-tts', testTts);
        report();
    }

    const observer = new MutationObserver(() => install());
    observer.observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', install);
    install();

    return { install, testHtml5, testWebAudio, testTts };
})();
