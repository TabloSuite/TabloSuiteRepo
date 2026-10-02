window.SKOULOUAPPAudio = (() => {
    let audioContext;
    let unlocked = false;

    function getAudioContext() {
        const AudioContextType = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextType) {
            return null;
        }

        audioContext ??= new AudioContextType();
        return audioContext;
    }

    function tinyUnlockTone(audible = false) {
        const context = getAudioContext();
        if (!context) {
            return;
        }

        const oscillator = context.createOscillator();
        const gain = context.createGain();
        oscillator.frequency.value = audible ? 660 : 1;
        gain.gain.value = audible ? 0.04 : 0.0001;
        oscillator.connect(gain);
        gain.connect(context.destination);
        oscillator.start();
        oscillator.stop(context.currentTime + (audible ? 0.12 : 0.025));
    }

    async function unlock(audible = false) {
        if (unlocked) {
            if (audible) {
                tinyUnlockTone(true);
            }

            return true;
        }

        try {
            const context = getAudioContext();
            if (context && context.state === 'suspended') {
                await context.resume();
            }

            tinyUnlockTone(audible);

            if ('speechSynthesis' in window && typeof SpeechSynthesisUtterance !== 'undefined') {
                window.speechSynthesis.getVoices();
                if (audible) {
                    const utterance = new SpeechSynthesisUtterance('Audio activé');
                    utterance.lang = 'fr-FR';
                    utterance.volume = 0.6;
                    utterance.rate = 1.05;
                    window.speechSynthesis.speak(utterance);
                }
            }

            unlocked = true;
            return true;
        } catch {
            return false;
        }
    }

    async function activate() {
        return unlock(true);
    }

    async function testTone() {
        const ok = await unlock(false);
        if (!ok) {
            return false;
        }

        tinyUnlockTone(true);
        return true;
    }

    function getStatus() {
        const context = getAudioContext();
        const voices = 'speechSynthesis' in window ? window.speechSynthesis.getVoices() : [];
        return {
            audioContextSupported: !!context,
            audioContextState: context?.state || 'unavailable',
            unlocked,
            speechSynthesisSupported: 'speechSynthesis' in window && typeof SpeechSynthesisUtterance !== 'undefined',
            speechSynthesisVoices: voices?.length || 0,
            userAgent: navigator.userAgent || ''
        };
    }

    function installUnlockListeners() {
        const handler = () => {
            unlock();
        };

        ['pointerdown', 'touchstart', 'click', 'keydown'].forEach(eventName => {
            window.addEventListener(eventName, handler, { passive: true });
        });

        const directToneHandler = event => {
            const button = event.target?.closest?.('[data-skoulouapp-test-tone]');
            if (!button || button.disabled) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            testTone();
        };

        document.addEventListener('pointerdown', directToneHandler, { capture: true });
        document.addEventListener('touchstart', directToneHandler, { capture: true, passive: false });
        document.addEventListener('click', directToneHandler, { capture: true });
    }

    return {
        getAudioContext,
        activate,
        getStatus,
        installUnlockListeners,
        testTone,
        unlock,
        get isUnlocked() {
            return unlocked;
        }
    };
})();

window.SKOULOUAPPAudio.installUnlockListeners();
