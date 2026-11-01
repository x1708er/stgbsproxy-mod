using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Settings;
using UnityEngine;

namespace STGBSProxy
{
    /// <summary>
    /// Status-Anzeige + Einstellungen im Spiel: Hauptmenue -> Settings ->
    /// Mod Settings -> "STG BS Proxy". BSML ist eine WEICHE Abhaengigkeit:
    /// Ist es nicht installiert (oder inkompatibel), schlaegt nur die
    /// Menue-Registrierung fehl und wird geloggt — die Mod selbst laeuft
    /// normal weiter. Dafuer ist jeder BSML-Zugriff in RegisterCore()
    /// isoliert (NoInlining, damit der JIT die BSML-Typen erst beim Aufruf
    /// aufloest).
    /// </summary>
    internal static class StatusMenu
    {
        /// <summary>Wird von Plugin.OnStart auf dem Main-Thread aufgerufen.</summary>
        public static void Init()
        {
            var go = new GameObject("STGBSProxy_StatusMenu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MenuRegistrar>();
        }
    }

    /// <summary>
    /// Registriert das Settings-Menue, sobald BSMLs Zenject-Container steht
    /// (BSMLSettings.Instance WIRFT davor), und haelt danach die angezeigten
    /// Werte aktuell. Nach einem Soft-Restart (z.B. Grafikeinstellung
    /// geaendert) baut BSML einen neuen Container mit leerer Menueliste —
    /// deshalb wird dauerhaft alle 5 s geprueft, ob die Instanz gewechselt
    /// hat, und dann neu registriert.
    /// </summary>
    internal class MenuRegistrar : MonoBehaviour
    {
        /// <summary>Der CDN-Host, um den es beim Peering-Problem geht.</summary>
        private const string CdnHost = "r2cdn.beatsaver.com";
        private const int PingIntervalMs = 30000;
        private const int PingTimeoutMs = 5000;

        private readonly StatusHost _host = new StatusHost();

        private object _registeredWith;
        private bool _bsmlBroken;
        private int _nextRegisterTry;
        private int _nextRefresh;

        // UnityEngine.Ping will eine IP (dotted), keinen Hostnamen -> DNS
        // einmalig auf dem ThreadPool aufloesen. Felder volatile, weil sie
        // vom ThreadPool geschrieben und in Update gelesen werden.
        private volatile string _proxyIp;
        private volatile string _cdnIp;
        private volatile bool _dnsRunning;
        private int _nextDnsTry;
        private int _nextPingRound;
        private int _pingStartedAt;
        private Ping _proxyPing;
        private Ping _cdnPing;

        /// <summary>Zuletzt gesehene Config-Generation: aendert sich
        /// ProxyBase zur Laufzeit, muss der Ping-DNS neu aufgeloest werden.</summary>
        private int _cfgGeneration;

        private void Awake()
        {
            // Environment.TickCount statt UnityEngine.Time: spart den Stub.
            int now = Environment.TickCount;
            _nextRegisterTry = now;
            _nextRefresh = now;
            _nextDnsTry = now;
            _nextPingRound = now;
            _cfgGeneration = Plugin.ConfigGeneration;
        }

        private void Update()
        {
            int now = Environment.TickCount;

            if (!_bsmlBroken && now - _nextRegisterTry >= 0)
            {
                _nextRegisterTry = now + 5000;
                TryRegister();
            }

            int gen = Plugin.ConfigGeneration;
            if (gen != _cfgGeneration)
            {
                _cfgGeneration = gen;
                _proxyIp = null;      // ProxyBase gewechselt -> neu aufloesen
                _nextDnsTry = now;
            }

            UpdatePings(now);

            if (now - _nextRefresh >= 0)
            {
                _nextRefresh = now + 1000;
                try { _host.Refresh(); }
                catch { /* Anzeige darf niemals den Frame abschiessen */ }
            }
        }

        private void UpdatePings(int now)
        {
            if ((_proxyIp == null || _cdnIp == null) && !_dnsRunning && now - _nextDnsTry >= 0)
            {
                _dnsRunning = true;
                _nextDnsTry = now + 60000;
                int gen = _cfgGeneration;
                ThreadPool.QueueUserWorkItem(_ => ResolveDns(gen));
            }

            if (_proxyPing == null && _cdnPing == null
                && (_proxyIp != null || _cdnIp != null)
                && now - _nextPingRound >= 0)
            {
                _nextPingRound = now + PingIntervalMs;
                _pingStartedAt = now;
                try
                {
                    if (_proxyIp != null) _proxyPing = new Ping(_proxyIp);
                    if (_cdnIp != null) _cdnPing = new Ping(_cdnIp);
                }
                catch (Exception e)
                {
                    Plugin.Log?.Debug("Ping-Start fehlgeschlagen: " + e.Message);
                }
            }

            bool timedOut = now - _pingStartedAt > PingTimeoutMs;

            if (_proxyPing != null && (_proxyPing.isDone || timedOut))
            {
                _host.ProxyPingMs = _proxyPing.isDone && _proxyPing.time >= 0 ? _proxyPing.time : -1;
                _proxyPing.DestroyPing();
                _proxyPing = null;
            }

            if (_cdnPing != null && (_cdnPing.isDone || timedOut))
            {
                _host.CdnPingMs = _cdnPing.isDone && _cdnPing.time >= 0 ? _cdnPing.time : -1;
                _cdnPing.DestroyPing();
                _cdnPing = null;
            }
        }

        private void ResolveDns(int generation)
        {
            try
            {
                if (_cdnIp == null) _cdnIp = FirstV4(CdnHost);
                string proxyIp = _proxyIp ?? FirstV4(new Uri(PluginConfig.ProxyBase).Host);
                // Ergebnis verwerfen, wenn ProxyBase inzwischen gewechselt hat.
                if (Plugin.ConfigGeneration == generation)
                    _proxyIp = proxyIp;
            }
            catch (Exception e)
            {
                Plugin.Log?.Debug("Ping-DNS fehlgeschlagen (naechster Versuch in 60 s): " + e.Message);
            }
            finally
            {
                _dnsRunning = false;
            }
        }

        private static string FirstV4(string host)
        {
            foreach (IPAddress a in Dns.GetHostAddresses(host))
            {
                if (a.AddressFamily == AddressFamily.InterNetwork)
                    return a.ToString();
            }
            return null;
        }

        private void TryRegister()
        {
            try
            {
                RegisterCore();
            }
            catch (InvalidOperationException)
            {
                // Container (noch) nicht bereit -> beim naechsten Tick wieder.
            }
            catch (Exception e)
            {
                _bsmlBroken = true;
                Plugin.Log?.Warn("BSML nicht verfuegbar/inkompatibel -> kein Status-Menue (Mod laeuft normal weiter): "
                    + e.GetType().Name + ": " + e.Message);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterCore()
        {
            BSMLSettings settings = BSMLSettings.Instance;
            if (settings == null || ReferenceEquals(settings, _registeredWith))
                return;

            // AddSettingsMenu laedt die View per Assembly.GetCallingAssembly()
            // als eingebettete Ressource aus STGBSProxy.dll.
            settings.AddSettingsMenu("STG BS Proxy", "STGBSProxy.Views.settings.bsml", _host);
            _registeredWith = settings;
            Plugin.Log?.Info("Status-Menue in Mod Settings registriert.");
        }
    }

    /// <summary>
    /// Liefert die Textzeilen und Einstellungen fuer die BSML-View. BSML
    /// bindet die [UIValue]-Member beim Parsen und aktualisiert die Anzeige
    /// ueber INotifyPropertyChanged (getriggert von MenuRegistrar.Update).
    /// Die cfg-*-Setter laufen auf dem Main-Thread (BSML-UI), schreiben die
    /// Config sofort auf Platte (Save) und stossen den Health-Loop an —
    /// dadurch wirken alle Aenderungen ohne Neustart.
    /// UI-Texte bewusst ohne Umlaute — Fontabdeckung nicht garantiert.
    /// </summary>
    internal class StatusHost : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Sentinel: noch keine Messung abgeschlossen.</summary>
        internal const int NoPing = int.MinValue;

        /// <summary>Letzte Ping-RTT in ms; -1 = keine Antwort. Schreibt MenuRegistrar.</summary>
        internal volatile int ProxyPingMs = NoPing;
        internal volatile int CdnPingMs = NoPing;

        private string _lastStatus, _lastProxy, _lastRewrites, _lastCache, _lastTraffic, _lastHealth, _lastPing;

        // Merker fuer extern (Hot-Reload) geaenderte Config-Werte, damit
        // Refresh die gebundenen Controls nachzieht (bind-value).
        private bool? _lastCfgEnabled, _lastCfgHealth;
        private int _lastCfgRetry = -1, _lastCfgTimeout = -1;
        private string _lastCfgBase;

        // ------------------------- Status-Zeilen -------------------------

        [UIValue("status-line")]
        public string StatusLine
        {
            get
            {
                if (!PluginConfig.Enabled)
                    return "<color=#E06060>AUS</color> - per Einstellung deaktiviert, Downloads gehen direkt zu BeatSaver";
                return Plugin.Active
                    ? "<color=#6BD46B>AKTIV</color> - Map-Downloads laufen durch den Proxy"
                    : "<color=#E0B040>WARTET</color> - Proxy nicht erreichbar, Downloads gehen direkt zu BeatSaver";
            }
        }

        [UIValue("proxy-line")]
        public string ProxyLine => "Proxy: " + PluginConfig.ProxyBase;

        [UIValue("rewrites-line")]
        public string RewritesLine
        {
            get
            {
                string s = "Umgeleitete Downloads (Session): " + Plugin.RewriteCount;
                long t = Interlocked.Read(ref Plugin.LastRewriteTicksUtc);
                if (t != 0)
                    s += "   (zuletzt " + Ago(t) + ")";
                return s;
            }
        }

        [UIValue("cache-line")]
        public string CacheLine
        {
            get
            {
                long hits = Interlocked.Read(ref Plugin.CacheHits);
                long misses = Interlocked.Read(ref Plugin.CacheMisses);
                long unknown = Interlocked.Read(ref Plugin.CacheUnknown);
                if (hits + misses + unknown == 0)
                    return "Server-Cache: noch keine Proxy-Downloads abgeschlossen";

                string s = "Server-Cache: " + hits + " HIT / " + misses + " MISS";
                if (hits + misses > 0)
                    s += " (" + (int)Math.Round(100.0 * hits / (hits + misses)) + " % aus dem Cache)";
                if (unknown > 0)
                    s += " / " + unknown + " ohne Status";
                return s;
            }
        }

        [UIValue("traffic-line")]
        public string TrafficLine
        {
            get
            {
                long total = Interlocked.Read(ref Plugin.ProxyBytesTotal);
                if (total <= 0)
                    return "Volumen (Session): noch nichts ueber den Proxy geladen";
                long cached = Interlocked.Read(ref Plugin.ProxyBytesFromCache);
                return "Volumen (Session): " + FmtBytes(total) + " via Proxy, davon "
                    + FmtBytes(cached) + " aus dem Server-Cache";
            }
        }

        [UIValue("ping-line")]
        public string PingLine =>
            "Ping: Proxy " + FmtPing(ProxyPingMs) + "  |  BeatSaver-CDN " + FmtPing(CdnPingMs);

        [UIValue("health-line")]
        public string HealthLine
        {
            get
            {
                long t = Interlocked.Read(ref Plugin.LastProbeTicksUtc);
                if (!PluginConfig.HealthCheck && t == 0)
                    return "Healthcheck: deaktiviert (Config)";
                if (t == 0)
                    return "Healthcheck: noch kein Ergebnis";
                return "Letzter Healthcheck: " + (Plugin.LastProbeDetail ?? "?") + " (" + Ago(t) + ")";
            }
        }

        // --------------------- Aktionen & Einstellungen ---------------------

        [UIAction("test-now")]
        private void TestNow()
        {
            // Nur anstossen — die Probe laeuft auf dem Health-Thread, der
            // Main-Thread (UI) darf hier nicht blockieren. Das Ergebnis
            // erscheint binnen Sekunden in der Healthcheck-Zeile.
            Plugin.Log?.Info("Manueller Proxy-Test ueber das Menue angestossen.");
            Plugin.RequestProbe();
        }

        [UIValue("cfg-enabled")]
        public bool CfgEnabled
        {
            get => PluginConfig.Enabled;
            set
            {
                if (value == PluginConfig.Enabled)
                    return;
                PluginConfig.Enabled = value;
                Plugin.Log?.Info(value ? "Proxy per Menue EINgeschaltet." : "Proxy per Menue AUSgeschaltet.");
                PersistAndApply(false);
            }
        }

        [UIValue("cfg-healthcheck")]
        public bool CfgHealthCheck
        {
            get => PluginConfig.HealthCheck;
            set
            {
                if (value == PluginConfig.HealthCheck)
                    return;
                PluginConfig.HealthCheck = value;
                PersistAndApply(false);
            }
        }

        [UIValue("cfg-retry")]
        public int CfgRetrySeconds
        {
            get => PluginConfig.RetrySeconds;
            set
            {
                int v = Math.Max(5, Math.Min(600, value));
                if (v == PluginConfig.RetrySeconds)
                    return;
                PluginConfig.RetrySeconds = v;
                PersistAndApply(false);
            }
        }

        [UIValue("cfg-timeout")]
        public int CfgTimeoutMs
        {
            get => PluginConfig.HealthTimeoutMs;
            set
            {
                int v = Math.Max(500, Math.Min(10000, value));
                if (v == PluginConfig.HealthTimeoutMs)
                    return;
                PluginConfig.HealthTimeoutMs = v;
                PersistAndApply(false);
            }
        }

        [UIValue("cfg-proxybase")]
        public string CfgProxyBase
        {
            get => PluginConfig.ProxyBase;
            set
            {
                string v = (value ?? "").Trim().TrimEnd('/');
                if (v == PluginConfig.ProxyBase)
                    return;
                if (!PluginConfig.IsValidProxyBase(v))
                {
                    Plugin.Log?.Warn("Ungueltige Proxy-URL '" + v + "' -> Aenderung verworfen (http(s)://... erwartet).");
                    // Control zeigt sonst den verworfenen Text -> zuruecksetzen.
                    NotifyNow(nameof(CfgProxyBase), "cfg-proxybase");
                    return;
                }
                PluginConfig.ProxyBase = v;
                PersistAndApply(true);
            }
        }

        private static void PersistAndApply(bool proxyBaseChanged)
        {
            PluginConfig.Save();
            Plugin.OnRuntimeConfigChanged(proxyBaseChanged);
        }

        // ----------------------------- Refresh -----------------------------

        internal void Refresh()
        {
            var pc = PropertyChanged;
            if (pc == null)
                return; // Menue noch nie geoeffnet -> niemand hoert zu

            Notify(pc, StatusLine, ref _lastStatus, nameof(StatusLine), "status-line");
            Notify(pc, ProxyLine, ref _lastProxy, nameof(ProxyLine), "proxy-line");
            Notify(pc, RewritesLine, ref _lastRewrites, nameof(RewritesLine), "rewrites-line");
            Notify(pc, CacheLine, ref _lastCache, nameof(CacheLine), "cache-line");
            Notify(pc, TrafficLine, ref _lastTraffic, nameof(TrafficLine), "traffic-line");
            Notify(pc, PingLine, ref _lastPing, nameof(PingLine), "ping-line");
            Notify(pc, HealthLine, ref _lastHealth, nameof(HealthLine), "health-line");

            // Config kann sich extern aendern (Hot-Reload) -> gebundene
            // Controls nachziehen. Bei Aenderung UEBER das Control ist der
            // Wert schon gleich, die Notify ist dann ein No-Op-Roundtrip
            // (die Setter brechen bei unveraendertem Wert sofort ab).
            if (_lastCfgEnabled != PluginConfig.Enabled)
            {
                _lastCfgEnabled = PluginConfig.Enabled;
                NotifyNow(nameof(CfgEnabled), "cfg-enabled");
            }
            if (_lastCfgHealth != PluginConfig.HealthCheck)
            {
                _lastCfgHealth = PluginConfig.HealthCheck;
                NotifyNow(nameof(CfgHealthCheck), "cfg-healthcheck");
            }
            if (_lastCfgRetry != PluginConfig.RetrySeconds)
            {
                _lastCfgRetry = PluginConfig.RetrySeconds;
                NotifyNow(nameof(CfgRetrySeconds), "cfg-retry");
            }
            if (_lastCfgTimeout != PluginConfig.HealthTimeoutMs)
            {
                _lastCfgTimeout = PluginConfig.HealthTimeoutMs;
                NotifyNow(nameof(CfgTimeoutMs), "cfg-timeout");
            }
            if (_lastCfgBase != PluginConfig.ProxyBase)
            {
                _lastCfgBase = PluginConfig.ProxyBase;
                NotifyNow(nameof(CfgProxyBase), "cfg-proxybase");
            }
        }

        private void Notify(PropertyChangedEventHandler pc, string current, ref string last, string propName, string id)
        {
            if (current == last)
                return;
            last = current;
            // BSML-Versionen matchen je nach Stand auf Membernamen oder
            // UIValue-Id -> beide melden, unbekannte Namen ignoriert BSML.
            pc(this, new PropertyChangedEventArgs(propName));
            pc(this, new PropertyChangedEventArgs(id));
        }

        private void NotifyNow(string propName, string id)
        {
            var pc = PropertyChanged;
            if (pc == null)
                return;
            pc(this, new PropertyChangedEventArgs(propName));
            pc(this, new PropertyChangedEventArgs(id));
        }

        private static string FmtPing(int ms)
        {
            if (ms == NoPing) return "...";
            if (ms < 0) return "keine Antwort";
            string col = ms < 60 ? "#6BD46B" : ms < 150 ? "#E0B040" : "#E06060";
            return "<color=" + col + ">" + ms + " ms</color>";
        }

        private static string FmtBytes(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024L * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + " MB";
            return (b / 1073741824.0).ToString("0.##") + " GB";
        }

        private static string Ago(long ticksUtc)
        {
            double s = (DateTime.UtcNow - new DateTime(ticksUtc, DateTimeKind.Utc)).TotalSeconds;
            if (s < 5) return "gerade eben";
            if (s < 90) return "vor " + (int)s + " s";
            if (s < 5400) return "vor " + (int)(s / 60) + " min";
            return "vor " + (int)(s / 3600) + " h";
        }
    }
}
