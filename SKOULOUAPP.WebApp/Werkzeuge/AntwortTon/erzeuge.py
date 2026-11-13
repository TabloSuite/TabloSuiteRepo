"""
Erzeugt die gesprochenen Rückmeldungen für Schüler-Übungen EINMAL mit Azure Speech (Stimme Vivienne)
und legt sie als MP3 nach wwwroot/audio/antworten/. Die App spielt nur diese Dateien ab –
zur Laufzeit wird Azure nicht gebraucht (keine Kosten, kein Schlüssel in der App).

Aufruf:  python erzeuge.py            (nur fehlende Dateien)
         python erzeuge.py --neu      (alle neu erzeugen, z. B. nach Textänderung)

Schlüssel: Umgebungsvariable AZURE_SPEECH_KEY (+ optional AZURE_SPEECH_REGION) oder die
Datei eNOTE/.env des Repos. Der Schlüssel wird nie ausgegeben.
Die Texte stehen unten in SAETZE; die JS-Datei wwwroot/js/studentExerciseFeedback.js
kennt die Dateinamen (Anzahl je Gruppe muss dort übereinstimmen).
"""
import html, os, sys, urllib.error, urllib.request

HIER = os.path.dirname(os.path.abspath(__file__))
WEBAPP = os.path.dirname(os.path.dirname(HIER))
ZIEL = os.path.join(WEBAPP, 'wwwroot', 'audio', 'antworten')
ENV = os.path.join(os.path.dirname(WEBAPP), 'eNOTE', '.env')
STIMME = 'fr-FR-VivienneMultilingualNeural'

SAETZE = {
    'richtig-1': 'Bravo !',
    'richtig-2': 'Très bien !',
    'richtig-3': 'Excellent, continue comme ça !',
    'richtig-4': "C'est juste, bravo !",
    'richtig-5': 'Super, tu as trouvé !',
    'richtig-6': 'Parfait, bien joué !',
    'falsch-1': 'Oups, essaie encore !',
    'falsch-2': 'Pas tout à fait. Réessaie !',
    'falsch-3': 'Presque ! Encore un petit effort.',
    'falsch-4': "Ce n'est pas encore ça. Tu vas y arriver !",
    'falsch-5': 'Courage, essaie encore une fois !',
    'falsch-6': 'Regarde bien, et réessaie !',
    'weiter': 'Exercice suivant !',
    'gesendet': 'Réponse envoyée ! Ton enseignant va la corriger.',
}


def schluessel():
    key, region = os.environ.get('AZURE_SPEECH_KEY'), os.environ.get('AZURE_SPEECH_REGION')
    if not key and os.path.exists(ENV):
        werte = {}
        with open(ENV, encoding='utf-8-sig') as f:
            for zeile in f:
                zeile = zeile.strip()
                if zeile and not zeile.startswith('#') and '=' in zeile:
                    k, v = zeile.split('=', 1)
                    werte[k.strip()] = v.strip().strip('"').strip("'")
        key, region = werte.get('AZURE_SPEECH_KEY'), region or werte.get('AZURE_SPEECH_REGION')
    if not key:
        sys.exit('AZURE_SPEECH_KEY fehlt (Umgebungsvariable oder eNOTE/.env).')
    return key, region or 'francecentral'


def ssml(text):
    return ("<speak version='1.0' xml:lang='fr-FR'>"
            f"<voice name='{STIMME}'><prosody rate='+4%'>{html.escape(text)}</prosody></voice></speak>")


def main():
    neu = '--neu' in sys.argv
    key, region = schluessel()
    os.makedirs(ZIEL, exist_ok=True)
    for name, text in SAETZE.items():
        pfad = os.path.join(ZIEL, name + '.mp3')
        if os.path.exists(pfad) and not neu:
            continue
        anfrage = urllib.request.Request(
            f'https://{region}.tts.speech.microsoft.com/cognitiveservices/v1',
            data=ssml(text).encode('utf-8'), method='POST',
            headers={'Ocp-Apim-Subscription-Key': key, 'Content-Type': 'application/ssml+xml',
                     'X-Microsoft-OutputFormat': 'audio-24khz-48kbitrate-mono-mp3', 'User-Agent': 'skoulou-antwortton'})
        try:
            with urllib.request.urlopen(anfrage, timeout=60) as antwort, open(pfad, 'wb') as f:
                f.write(antwort.read())
        except urllib.error.HTTPError as e:
            sys.exit(f'Azure-Fehler {e.code} bei {name}')
        print(f'{name:10} {os.path.getsize(pfad) // 1024:3d} KB  {text}')


if __name__ == '__main__':
    main()
