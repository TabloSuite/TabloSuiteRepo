window.SKOULOUAPPScroll = window.SKOULOUAPPScroll || {};

window.SKOULOUAPPScroll.scrollElementToTop = function (element) {
    if (!element) {
        return;
    }

    let userInteracted = false;
    const markUserInteraction = function () {
        userInteracted = true;
    };

    element.addEventListener('wheel', markUserInteraction, { passive: true, once: true });
    element.addEventListener('touchstart', markUserInteraction, { passive: true, once: true });
    element.addEventListener('pointerdown', markUserInteraction, { passive: true, once: true });

    const forceTop = function () {
        if (userInteracted) {
            return;
        }
        element.scrollTop = 0;
    };

    forceTop();
    requestAnimationFrame(forceTop);
    setTimeout(forceTop, 40);
    setTimeout(forceTop, 120);
};
