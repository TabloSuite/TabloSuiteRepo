window.SKOULOUAPPScroll = window.SKOULOUAPPScroll || {};

window.SKOULOUAPPScroll.scrollElementToTop = function (element) {
    if (!element) {
        return;
    }

    const startedAt = performance.now();
    const durationMs = 1400;

    const forceTop = function () {
        element.scrollTop = 0;
    };

    const tick = function () {
        forceTop();
        if ((performance.now() - startedAt) < durationMs) {
            requestAnimationFrame(tick);
        }
    };

    tick();
    setTimeout(forceTop, 50);
    setTimeout(forceTop, 180);
    setTimeout(forceTop, 500);
    setTimeout(forceTop, 900);
};
