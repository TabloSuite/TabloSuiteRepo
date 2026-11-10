/**
 * Skoulou – Tailwind-Konfiguration (Design „Soft Modern“ wie eNOTE).
 *
 * - Markenfarben folgen dem gewählten Farbthema (data-theme auf <html>): indigo/blue/violet/purple/fuchsia
 *   = Primärfarbe (--sk-p-*), cyan/sky/teal = Akzent (--sk-a-*). Werte in wwwroot/css/skoulou-theme.css.
 * - slate = ruhige Neutraltöne wie eNOTE; emerald/green = Erfolg, rose/red = Fehler, amber/orange = Hinweis.
 * - Weichere Schatten (Elevation statt harter Schlagschatten).
 * Nach Änderungen an Klassen: `npm run css` (der Build macht das automatisch, wenn node_modules existiert).
 */
const thema = (praefix) => Object.fromEntries(
    [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950].map(stufe => [stufe, `rgb(var(--sk-${praefix}-${stufe}) / <alpha-value>)`]));

const primaer = thema('p');
const akzent = thema('a');

module.exports = {
    content: [
        './Components/**/*.{razor,cs,html}',
        './Services/**/*.cs',
        './wwwroot/js/**/*.js'
    ],
    theme: {
        extend: {
            colors: {
                indigo: primaer,
                blue: primaer,
                violet: primaer,
                purple: primaer,
                fuchsia: primaer,
                cyan: akzent,
                sky: akzent,
                teal: akzent,
                slate: {
                    50: '#f7f9fc', 100: '#eef2f7', 200: '#e3e8f0', 300: '#cfd7e3', 400: '#8a95a8', 500: '#5d6a82',
                    600: '#47536b', 700: '#333e55', 800: '#1f2940', 900: '#101a33', 950: '#0a1124'
                }
            },
            boxShadow: {
                sm: '0 1px 2px rgba(16, 26, 51, .05)',
                DEFAULT: '0 1px 2px rgba(16, 26, 51, .04), 0 2px 6px rgba(16, 26, 51, .05)',
                md: '0 1px 2px rgba(16, 26, 51, .04), 0 6px 18px rgba(16, 26, 51, .07)',
                lg: '0 4px 10px rgba(16, 26, 51, .05), 0 12px 28px rgba(16, 26, 51, .08)',
                xl: '0 8px 20px rgba(16, 26, 51, .07), 0 20px 44px rgba(16, 26, 51, .10)',
                '2xl': '0 24px 60px rgba(16, 26, 51, .20)'
            }
        }
    },
    plugins: []
};
