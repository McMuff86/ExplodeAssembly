# Anweisungen für den AI Coding Agent

Dieses Dokument enthält die Anweisungen für den AI Coding Agent, um die Projektstruktur, den Denkprozess und spezielle Anforderungen für Unterordner systematisch zu dokumentieren.

---

## 1. Dokumentation der Projektordnerstruktur

### Ziel
Eine klare Übersicht der Projektordnerstruktur von Anfang an sicherstellen.

### Anweisungen
1. **Prüfen**: Existiert im Hauptprojektordner eine Datei namens `projektstruktur.txt`?
2. **Erstellen**: Falls nicht, erstelle eine neue Datei `projektstruktur.txt` im Hauptprojektordner.
3. **Dokumentieren**:
   - Erstelle eine hierarchische Darstellung der Ordner und Dateien (z. B. mit `-` oder `|`).
   - Beispiel:
     ```
     Projektordner
     |- main.py
     |- utils.py
     |- data
        |- input.csv
        |- output.csv
     ```
4. **Aktualisieren**: Bei Änderungen der Struktur die Datei anpassen.
5. **Zeitstempel hinzufügen**: Am Anfang der Datei, z. B.:
   ```
   Zuletzt aktualisiert: 2023-10-01 12:00
   ```
6. **Optional**: Ältere Versionen in separaten Dateien speichern (z. B. `projektstruktur_2023-10-01.txt`).

### Technische Umsetzung
- Verwende `os.walk()` in Python, um die Struktur zu erfassen und in `projektstruktur.txt` zu schreiben.

---

## 2. Dokumentation des Denkprozesses

### Ziel
Den Denkprozess und die Entwicklung des `ExplodeAssembly`-Files nachvollziehbar dokumentieren.

### Anweisungen
1. **Prüfen**: Existiert ein Ordner `thoughtprocess` im Projektordner?
2. **Erstellen**: Falls nicht, erstelle den Ordner `thoughtprocess`.
3. **Dokumentieren**:
   - **Nummerierung**: Bestimme die nächste fortlaufende Nummer (dreistellig, z. B. `001`, `002`).
   - **Textdatei erstellen**:
     - Speichere sie in `thoughtprocess` mit dem Namen `<nummer>_thoughtprocess.txt` (z. B. `001_thoughtprocess.txt`).
     - Schreibe den aktuellen Denkprozess hinein.
   - **Code-Version speichern**:
     - Erstelle eine Kopie des aktuellen `ExplodeAssembly`-Files.
     - Speichere sie in `thoughtprocess` als `<nummer>_ExplodeAssembly.py` (z. B. `001_ExplodeAssembly.py`).
4. **Nummerierung sicherstellen**: Keine Dateien überschreiben, korrekt hochzählen.

### Beispiel
- Schritt 1: `001_thoughtprocess.txt` und `001_ExplodeAssembly.py`.
- Schritt 2: `002_thoughtprocess.txt` und `002_ExplodeAssembly.py`.

---

## 3. README.md-Dateien für Unterordner

### Ziel
Jeder Unterordner soll eine eigene `README.md`-Datei mit spezifischen Informationen erhalten.

### Anweisungen
1. **Bei Erstellung eines Unterordners**:
   - Automatisch eine `README.md`-Datei im neuen Unterordner erstellen.
2. **Inhalt**:
   - Zweck des Ordners beschreiben.
   - Spezielle Anweisungen hinzufügen.
   - Kurz und präzise halten.
3. **Aktualisierung prüfen**: Bei Änderungen die `README.md` anpassen.

### Beispiel für `thoughtprocess`
```markdown
# Thoughtprocess Ordner

Dieser Ordner dokumentiert den Denkprozess und die Versionen des `ExplodeAssembly`-Files.

## Dateibenennung
- Denkprozess: `<nummer>_thoughtprocess.txt`
- Code-Version: `<nummer>_ExplodeAssembly.py`

## Anweisungen
- Neue Schritte erhalten fortlaufende Nummern.
- Code-Versionen zeigen den Zustand vor Änderungen.
```

---

## 4. Integration in den Workflow

### Zu Projektbeginn
- Erstelle `projektstruktur.txt` mit der initialen Struktur.
- Erstelle den Ordner `thoughtprocess` mit einer `README.md` und beginne die Dokumentation.

### Bei Änderungen
- Aktualisiere `projektstruktur.txt`.
- Dokumentiere neue Denkprozess-Schritte und speichere Code-Versionen.
- Erstelle bei neuen Unterordnern eine `README.md`.

---

## 5. Technische Hinweise

### Python-Skripte verwenden
- Projektstruktur mit `os.walk()` in `projektstruktur.txt` schreiben.
- Denkprozess und Code-Versionen in `thoughtprocess` verwalten.
- Automatisch `README.md`-Dateien für Unterordner generieren.

### Nummerierung
- Korrekte Hochzählung sicherstellen.
- Format einheitlich halten (z.B. dreistellig: 001, 002, ...).