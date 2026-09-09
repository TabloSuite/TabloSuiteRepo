window.SKOULOUAPPVideoMetadata = (function () {
    function getDurationInSeconds(inputId) {
        return new Promise((resolve, reject) => {
            const input = document.getElementById(inputId);
            const file = input?.files?.[0];
            if (!file) {
                resolve(null);
                return;
            }

            const url = URL.createObjectURL(file);
            const video = document.createElement('video');
            video.preload = 'metadata';
            video.muted = true;
            video.playsInline = true;

            const cleanup = () => {
                URL.revokeObjectURL(url);
                video.removeAttribute('src');
                video.load();
            };

            video.onloadedmetadata = () => {
                const duration = Number.isFinite(video.duration) ? video.duration : null;
                cleanup();
                resolve(duration);
            };

            video.onerror = () => {
                cleanup();
                reject(new Error('La durée de la vidéo ne peut pas être lue.'));
            };

            video.src = url;
        });
    }

    return {
        getDurationInSeconds
    };
})();