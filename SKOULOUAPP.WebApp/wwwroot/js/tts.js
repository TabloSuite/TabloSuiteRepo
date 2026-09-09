// Text-to-Speech über die Web Speech API (Browser-TTS).
// Austauschbares Modul: später kann hier optional ein OpenAI-TTS-Player ergänzt werden.
window.SKOULOUAPPTts = (function () {
    const preferredLanguageStorageKey = 'halloapp_tts_lang';
    const fallbackLanguage = 'de-DE';
    let voicesLoaded = false;
    let delegatedHandlersInstalled = false;
    let lastStatus = 'idle';
    let currentAudio = null;

    function normalizeLanguage(lang) {
        const value = (lang || '').trim().toLowerCase();
        if (value.startsWith('de')) {
            return 'de-DE';
        }
        if (value.startsWith('fr')) {
            return 'fr-FR';
        }
        if (value.startsWith('en')) {
            return 'en-US';
        }

        return fallbackLanguage;
    }

    function getPreferredLanguage() {
        try {
            return normalizeLanguage(window.localStorage?.getItem(preferredLanguageStorageKey));
        } catch (e) {
            return fallbackLanguage;
        }
    }

    function setPreferredLanguage(lang) {
        const normalized = normalizeLanguage(lang);
        try {
            window.localStorage?.setItem(preferredLanguageStorageKey, normalized);
        } catch (e) {
            // ignore storage errors
        }

        return normalized;
    }

    function ensureVoices() {
        if (voicesLoaded) {
            return;
        }
        // Stimmenliste anstoßen (einige Browser laden asynchron).
        try {
            window.speechSynthesis.getVoices();
            voicesLoaded = true;
        } catch (e) {
            // ignore
        }
    }

    function pickVoice(lang) {
        try {
            const voices = window.speechSynthesis.getVoices();
            if (!voices || voices.length === 0) {
                return null;
            }
            const target = normalizeLanguage(lang || getPreferredLanguage()).toLowerCase();
            // Exakte Sprachübereinstimmung bevorzugen, sonst Präfix (de).
            return voices.find(v => v.lang && v.lang.toLowerCase() === target)
                || voices.find(v => v.lang && v.lang.toLowerCase().startsWith(target.split('-')[0]))
                || null;
        } catch (e) {
            return null;
        }
    }

    function createUtterance(text, lang, rate) {
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = normalizeLanguage(lang || getPreferredLanguage());
        utterance.rate = rate || 1.0;
        utterance.onstart = () => {
            lastStatus = 'started';
        };
        utterance.onend = () => {
            lastStatus = 'ended';
        };
        utterance.onerror = event => {
            lastStatus = `error:${event.error || 'unknown'}`;
            window.SKOULOUAPPAudio?.testTone?.();
        };
        const voice = pickVoice(utterance.lang);
        if (voice) {
            utterance.voice = voice;
        }
        return utterance;
    }

    function speakTextNow(text, lang, rate) {
        if (!isSupported() || !text) {
            return false;
        }

        ensureVoices();
        window.speechSynthesis.cancel();
        if (window.speechSynthesis.paused) {
            window.speechSynthesis.resume();
        }

        const utterance = createUtterance(text, lang, rate);
        lastStatus = 'queued';
        window.speechSynthesis.speak(utterance);
        window.setTimeout(() => {
            if (lastStatus === 'queued') {
                lastStatus = 'not-started';
                window.SKOULOUAPPAudio?.testTone?.();
            }
        }, 900);
        return true;
    }

    async function speakText(text, lang, rate) {
        await window.SKOULOUAPPAudio?.unlock?.();
        return speakTextNow(text, lang, rate);
    }

    async function playAudioUrl(audioUrl) {
        if (!audioUrl) {
            return false;
        }

        try {
            await window.SKOULOUAPPAudio?.unlock?.();
            window.speechSynthesis?.cancel?.();
            if (currentAudio) {
                currentAudio.pause();
                currentAudio.currentTime = 0;
            }

            currentAudio = new Audio(audioUrl);
            currentAudio.preload = 'auto';
            currentAudio.playsInline = true;
            lastStatus = 'html5-queued';
            currentAudio.onplaying = () => {
                lastStatus = 'html5-started';
            };
            currentAudio.onended = () => {
                lastStatus = 'html5-ended';
            };
            currentAudio.onerror = () => {
                lastStatus = 'html5-error';
            };
            await currentAudio.play();
            return true;
        } catch (e) {
            lastStatus = `html5-error:${e?.name || 'unknown'}`;
            return false;
        }
    }

    async function playAudioOrSpeak(audioUrl, text, lang, rate) {
        const audioPlayed = await playAudioUrl(audioUrl);
        if (audioPlayed) {
            return true;
        }

        return speakText(text, lang, rate);
    }

    function isSupported() {
        return typeof window.speechSynthesis !== 'undefined'
            && typeof window.SpeechSynthesisUtterance !== 'undefined';
    }

    function handleDirectTts(event) {
        const button = event.target?.closest?.('[data-halloapp-tts-text]');
        if (!button || button.disabled) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        const text = button.getAttribute('data-halloapp-tts-text') || '';
        const audioUrl = button.getAttribute('data-halloapp-audio-url') || '';
        const lang = button.getAttribute('data-halloapp-tts-lang') || getPreferredLanguage();
        const rate = Number.parseFloat(button.getAttribute('data-halloapp-tts-rate') || '1') || 1;
        playAudioOrSpeak(audioUrl, text, lang, rate);
    }

    function handleLanguageSelection(event) {
        const button = event.target?.closest?.('[data-halloapp-tts-set-lang]');
        if (!button || button.disabled) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        const selected = button.getAttribute('data-halloapp-tts-set-lang');
        setPreferredLanguage(selected);
    }

    function installDelegatedHandlers() {
        if (delegatedHandlersInstalled) {
            return;
        }

        delegatedHandlersInstalled = true;
        document.addEventListener('pointerdown', handleDirectTts, { capture: true });
        document.addEventListener('touchstart', handleDirectTts, { capture: true, passive: false });
        document.addEventListener('click', handleDirectTts, { capture: true });
        document.addEventListener('pointerdown', handleLanguageSelection, { capture: true });
        document.addEventListener('touchstart', handleLanguageSelection, { capture: true, passive: false });
        document.addEventListener('click', handleLanguageSelection, { capture: true });
    }

    const api = {
        isSupported: function () {
            return isSupported();
        },
        speak: async function (text, lang, rate) {
            return speakText(text, lang, rate);
        },
        play: async function (audioUrl, text, lang, rate) {
            return playAudioOrSpeak(audioUrl, text, lang, rate);
        },
        setPreferredLanguage: function (lang) {
            return setPreferredLanguage(lang);
        },
        getPreferredLanguage: function () {
            return getPreferredLanguage();
        },
        stop: function () {
            if (this.isSupported()) {
                window.speechSynthesis.cancel();
            }
            if (currentAudio) {
                currentAudio.pause();
                currentAudio.currentTime = 0;
            }
        },
        getStatus: function () {
            return lastStatus;
        }
    };

    installDelegatedHandlers();
    return api;
})();
