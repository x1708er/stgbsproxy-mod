STG BeatSaver CDN Proxy — v0.3.0
=================================

Was macht das?
  Leitet Map-Downloads von BeatSaver (r2cdn/cfcdn.beatsaver.com) ueber
  bsc.stgmultigaming.de um. Hilft, wenn abends das Peering
  Telekom <-> Cloudflare dicht ist und Downloads kriechen.

Installation (PCVR, gemoddetes Beat Saber mit BSIPA):
  1. Beat Saber beenden.
  2. Dieses Zip in den Beat-Saber-Ordner entpacken
     (Steam: .../steamapps/common/Beat Saber)
     -> STGBSProxy.dll landet automatisch in Plugins\
  3. Spiel starten. Fertig.

Verhalten (wichtig):
  - Fail-open, die GANZE Session lang: Die Mod prueft laufend, ob der
    Proxy erreichbar ist (Standard: alle 60 s). NICHT erreichbar ->
    Downloads laufen ganz normal direkt zu BeatSaver, bis der Proxy
    wieder antwortet. Es kann also nichts kaputtgehen, auch wenn der
    Server mitten im Spielen wegkippt.
  - Zusaetzlich reagiert die Mod sofort: Scheitern mehrere umgeleitete
    Downloads hintereinander (Verbindungsfehler, 5xx),
    wird die Umleitung auf der Stelle abgeschaltet — ohne auf den
    naechsten Check zu warten. Der fehlgeschlagene Download selbst muss
    einmal neu angestossen werden (geht dann direkt zu BeatSaver).
  - Menue im Spiel: Hauptmenue -> Settings -> Mod Settings -> STG BS Proxy
      * Status: aktiv/wartet/aus, umgeleitete Downloads, Cache-Treffer
        (HIT/MISS), geladenes Volumen, Ping zu Proxy und BeatSaver-CDN,
        letztes Healthcheck-Ergebnis
      * "Proxy jetzt testen"-Button: prueft die Erreichbarkeit sofort
      * Ein-/Ausschalten und ALLE Einstellungen (Proxy-URL,
        Intervall, Timeout) direkt im Menue — wirkt sofort, kein
        Neustart und kein JSON-Editieren noetig
    Braucht die Mod "BSML" — die ist bei gemoddetem Beat Saber praktisch
    immer dabei; ohne sie fehlt nur dieses Menue, die Umleitung laeuft
    trotzdem.
  - Config-Datei: UserData\STGBSProxy.json. Aenderungen an der Datei
    werden im laufenden Spiel uebernommen (Hot-Reload), ein Neustart ist
    nicht mehr noetig. "enabled": false schaltet die Umleitung aus (die
    Mod bleibt geladen, damit man sie im Menue wieder einschalten kann).
  - Log: Logs\STGBSProxy\ (falls was klemmt, die Datei an acidways schicken)

Deinstallation:
  Plugins\STGBSProxy.dll loeschen. Das war's.

Hinweis: Wie jede Mod wird sie bei einem Beat-Saber-Update von BSIPA
in den "Old ... Plugins"-Ordner verschoben und muss danach neu rein.
