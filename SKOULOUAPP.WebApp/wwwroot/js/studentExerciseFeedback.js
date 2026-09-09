window.halloStudentExerciseFeedback = (() => {
    let audioContext;

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

    async function speak(text, rate = 1.08, pitch = 1.15, delay = 260) {
        if (!('speechSynthesis' in window)) {
            return;
        }

        await window.SKOULOUAPPAudio?.unlock?.();

        window.setTimeout(() => {
            if (window.speechSynthesis.speaking || window.speechSynthesis.pending) {
                window.speechSynthesis.cancel();
            }

            const utterance = new SpeechSynthesisUtterance(text);
            utterance.lang = 'fr-FR';
            utterance.rate = rate;
            utterance.pitch = pitch;
            utterance.volume = 0.85;
            window.speechSynthesis.speak(utterance);
        }, delay);
    }

    function buildMessage(baseMessage, name, hasNextExercise) {
        const trimmedName = typeof name === 'string' ? name.trim() : '';
        const namePart = trimmedName ? ` ${trimmedName}` : '';
        const nextPart = hasNextExercise ? ' Prochain exercice !' : '';
        return `${baseMessage}${namePart} !${nextPart}`;
    }

    async function play(percent, isCorrect, requiresManualGrading, studentName, hasNextExercise) {
        try {
            await window.SKOULOUAPPAudio?.unlock?.();

            if (requiresManualGrading) {
                playTone(440, 0, 0.12, 'triangle', 0.045);
                playTone(554, 0.14, 0.12, 'triangle', 0.045);
                speak(buildMessage('Réponse envoyée', studentName, false), 1.02, 1.05, 320);
                return;
            }

            if (isCorrect || percent >= 100) {
                playTone(523, 0, 0.11, 'sine', 0.07);
                playTone(659, 0.12, 0.11, 'sine', 0.07);
                playTone(784, 0.24, 0.16, 'sine', 0.08);
                speak(buildMessage('Bravo, parfait', studentName, hasNextExercise), 1.04, 1.12, 430);
                return;
            }

            if (percent >= 75) {
                playTone(494, 0, 0.12, 'triangle', 0.06);
                playTone(659, 0.14, 0.16, 'triangle', 0.065);
                speak(buildMessage('Super, tu progresses', studentName, hasNextExercise), 1.04, 1.08, 360);
                return;
            }

            if (percent >= 50) {
                playTone(392, 0, 0.12, 'triangle', 0.055);
                playTone(523, 0.16, 0.14, 'triangle', 0.055);
                speak(buildMessage('Bien joué, continue', studentName, hasNextExercise), 1.02, 1.04, 340);
                return;
            }

            playTone(220, 0, 0.12, 'sawtooth', 0.035);
            playTone(196, 0.16, 0.18, 'sawtooth', 0.03);
            speak(buildMessage('Oups, essaie encore', studentName, false), 0.96, 0.95, 360);
        } catch {
            // Audio feedback must never block the exercise flow.
        }
    }

    return { play };
})();
