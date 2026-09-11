// Applies and persists the selected SKOULOUAPP color theme.
(function () {
    const STORAGE_KEY = "skoulouapp-theme";
    const COOKIE_KEY = "skoulouapp-theme";
    const VALID_THEMES = ["ocean", "coral", "slate", "sage", "navy"];

    function readCookie(name) {
        const prefix = name + "=";
        const parts = document.cookie.split(";");
        for (let i = 0; i < parts.length; i++) {
            const part = parts[i].trim();
            if (part.startsWith(prefix)) {
                return decodeURIComponent(part.substring(prefix.length));
            }
        }
        return null;
    }

    function applyTheme(theme) {
        const safeTheme = VALID_THEMES.includes(theme) ? theme : "navy";
        document.documentElement.setAttribute("data-theme", safeTheme);
        document.documentElement.style.setProperty("color-scheme", "light");

        document.cookie = COOKIE_KEY + "=" + encodeURIComponent(safeTheme) + ";path=/;max-age=31536000;samesite=lax";

        try {
            localStorage.setItem(STORAGE_KEY, safeTheme);
        } catch {
            // localStorage unavailable (private mode) — ignore, theme still applies for this page load.
        }
    }

    function getStoredTheme() {
        try {
            const stored = localStorage.getItem(STORAGE_KEY);
            if (VALID_THEMES.includes(stored || "")) {
                return stored;
            }
        } catch {
            // ignore
        }

        const fromCookie = readCookie(COOKIE_KEY);
        if (VALID_THEMES.includes(fromCookie || "")) {
            return fromCookie;
        }

        return "navy";
    }

    function applyThemeFromStorage() {
        applyTheme(getStoredTheme());
    }

    // Apply immediately on script load to avoid a flash of the default theme.
    applyThemeFromStorage();

    window.SKOULOUAPPTheme = {
        get: getStoredTheme,
        set: applyTheme,
        sync: applyThemeFromStorage,
        preview: function (theme) {
            const safeTheme = VALID_THEMES.includes(theme) ? theme : "navy";
            document.documentElement.setAttribute("data-theme", safeTheme);
            document.documentElement.style.setProperty("color-scheme", "light");
        }
    };

    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) {
            applyThemeFromStorage();
        }
    });

    window.addEventListener("pageshow", applyThemeFromStorage);
    window.addEventListener("focus", applyThemeFromStorage);

    // Blazor Enhanced Navigation kann das data-theme-Attribut auf <html> beim
    // Seitenwechsel (z. B. Klick auf Accueil) zurücksetzen. Nach jedem
    // Enhanced-Load das gespeicherte Theme erneut anwenden.
    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", applyThemeFromStorage);
    } else {
        document.addEventListener("blazor-enhanced-load", applyThemeFromStorage);
    }

    window.addEventListener("storage", function (event) {
        if (!event || event.key === STORAGE_KEY) {
            applyThemeFromStorage();
        }
    });
})();
