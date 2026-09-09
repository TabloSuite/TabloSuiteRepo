window.SKOULOUAPPScrollToTop = {
    threshold: 320,
    offset: 120,
    initialized: false,

    init: function () {
        if (this.initialized) {
            return;
        }
        this.initialized = true;

        const update = () => {
            const button = document.getElementById('scroll-top-button');
            if (button) {
                if (window.scrollY > this.threshold) {
                    button.classList.add('is-visible');
                } else {
                    button.classList.remove('is-visible');
                }
            }

            this.updateScrollSpy();
        };

        window.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', update, { passive: true });
        update();
    },

    updateScrollSpy: function () {
        const links = document.querySelectorAll('[data-scrollspy]');
        if (!links.length) {
            return;
        }

        const marker = window.scrollY + this.offset + 4;
        let currentId = null;
        let currentTop = -Infinity;

        links.forEach(link => {
            const id = link.getAttribute('data-scrollspy');
            const section = document.getElementById(id);
            if (!section) {
                return;
            }
            const top = section.getBoundingClientRect().top + window.scrollY;
            if (top <= marker && top > currentTop) {
                currentTop = top;
                currentId = id;
            }
        });

        links.forEach(link => {
            if (link.getAttribute('data-scrollspy') === currentId) {
                link.classList.add('scrollspy-active');
            } else {
                link.classList.remove('scrollspy-active');
            }
        });
    },

    scrollToTop: function () {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    },

    scrollToId: function (id) {
        const target = document.getElementById(id);
        if (target) {
            const top = target.getBoundingClientRect().top + window.scrollY - this.offset;
            window.scrollTo({ top: Math.max(top, 0), behavior: 'smooth' });
        } else {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
        return false;
    }
};

document.addEventListener('DOMContentLoaded', function () {
    window.SKOULOUAPPScrollToTop.init();
});
