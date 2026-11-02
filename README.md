# STG BS Proxy

[![Release](https://img.shields.io/github/v/release/x1708er/stgbsproxy-mod?label=Release)](https://github.com/x1708er/stgbsproxy-mod/releases)

Beat-Saber-Mod, die Map-Downloads über unseren Server
(bsc.stgmultigaming.de) laufen lässt — wenn abends die Verbindung zu
BeatSaver im Keller ist, kommen Maps damit trotzdem schnell. (Stichwort Peering Hetzner/Cloudflare)

## Installation

Voraussetzung: gemoddetes Beat Saber (BSIPA), wie üblich.

1. Neueste Version unter [Releases](https://github.com/x1708er/stgbsproxy-mod/releases) herunterladen.
2. Beat Saber beenden.
3. Zip in den Beat-Saber-Ordner entpacken
   (Steam: `...\steamapps\common\Beat Saber`) — die Mod landet automatisch im richtigen `Plugins`-Ordner. Alternativ die ZIP einfach per Drag & Drop in den BSManager ziehen.
4. Spiel starten. Fertig.

## Kann was kaputtgehen?

Ist unser Server nicht erreichbar, laufen die Downloads ganz normal
direkt bei BeatSaver — die Mod schaltet sich dann von selbst ab, bis der
Server wieder antwortet.

Es ist aber eine Mod, natürlich kann da immer etwas schiefgehen. Wenn
etwas nicht funktioniert: [Bug-Report](https://github.com/x1708er/stgbsproxy-mod/issues)
aufmachen, gerne mit Log-Dateien aus `Logs\STGBSProxy\`.

Bei excessiver Nutzung, kann es zu ip-sperren seitens unserer Infrastruktur kommen. Bei normaler Nutzung ist dies jedoch absolut ausgeschlossen.

## Menü im Spiel

Hauptmenü → Einstellungen → Mod Settings → **STG BS Proxy**

- Status: an/aus, umgeleitete Downloads, geladenes Volumen, Ping
- Statistik des Servers (Dateien, Volumen, Cache, Tempo), alle 5 min aktuell
- „Proxy jetzt testen" — prüft sofort, ob der Server antwortet
- Ein-/Ausschalten und alle Einstellungen direkt im Menü, wirkt ohne Neustart

Die wichtigsten Stats gibt es zusätzlich als eigener Reiter („STG BS
Proxy") im Mods-Panel der Map-Auswahl — oben zwischen den Reitern
umschalten. Damit sieht man sie auch in Multiplayer-Runden, ohne ins
Hauptmenü zu müssen.

Die Einstellungen stehen zusätzlich in `UserData\STGBSProxy.json`
und werden auch dann übernommen, wenn man die Datei außerhalb des Spiels
ändert.

## Deinstallation

`Plugins\STGBSProxy.dll` löschen. Das war's.

## Hinweise

- Nach einem Beat-Saber-Update verschiebt BSIPA die Mod in den
  „Old Plugins"-Ordner — nach dem erneuten Modden einfach die neueste
  Version wieder installieren.
- Falls etwas klemmt: Log-Dateien liegen in `Logs\STGBSProxy\` — an
  [x1708er](https://github.com/x1708er) schicken.
- Für diese Mod wurde KI genutzt! Wer ein Problem damit hat, klickt weiter!
