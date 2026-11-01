# STG BS Proxy

[![Release](https://img.shields.io/github/v/release/x1708er/stgbsproxy-mod?label=Release)](https://github.com/x1708er/stgbsproxy-mod/releases)

Beat-Saber-Mod, die Map-Downloads über unseren Server
(bsc.stgmultigaming.de) laufen lässt — wenn abends die Verbindung zu
BeatSaver im Keller ist, kommen Maps damit trotzdem schnell.

## Installation

Voraussetzung: gemoddetes Beat Saber (BSIPA), wie üblich.

1. Neueste Version unter [Releases](https://github.com/x1708er/stgbsproxy-mod/releases) herunterladen.
2. Beat Saber beenden.
3. Zip in den Beat-Saber-Ordner entpacken
   (Steam: `...\steamapps\common\Beat Saber`) — die Mod landet automatisch im richtigen `Plugins`-Ordner.
4. Spiel starten. Fertig.

## Kann was kaputtgehen?

Nein. Die Mod prüft laufend, ob unser Server erreichbar ist. Ist er es
nicht, laufen die Downloads einfach ganz normal direkt bei BeatSaver —
auch wenn der Server mitten im Spielen ausfällt.

## Menü im Spiel

Hauptmenü → Einstellungen → Mod Settings → **STG BS Proxy**

- Status: an/aus, umgeleitete Downloads, geladenes Volumen, Ping
- Statistik des Servers (Dateien, Volumen, Cache, Tempo), alle 5 min aktuell
- „Proxy jetzt testen" — prüft sofort, ob der Server antwortet
- Ein-/Ausschalten und alle Einstellungen direkt im Menü, wirkt ohne Neustart

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
  acidways schicken.
