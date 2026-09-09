// Applies and persists the selected SKOULOUAPP color theme.
(function () {
    const STORAGE_KEY = "halloapp-theme";
    const VALID_THEMES = ["indigo", "ocean", "coral", "slate", "sage", "navy"];

    function applyTheme(theme) {
        const safeTheme = VALID_THEMES.includes(theme) ? theme : "navy";
        document.documentElement.setAttribute("data-theme", safeTheme);
        try {
            localStorage.setItem(STORAGE_KEY, safeTheme);
        } catch {
            // localStorage unavailable (private mode) — ignore, theme still applies for this page load.
        }
    }

    function getStoredTheme() {
        try {
            return localStorage.getItem(STORAGE_KEY) || "navy";
        } catch {
            return "navy";
        }
    }

    // Apply immediately on script load to avoid a flash of the default theme.
    applyTheme(getStoredTheme());

    window.SKOULOUAPPTheme = {
        get: getStoredTheme,
        set: applyTheme
    };
})();
