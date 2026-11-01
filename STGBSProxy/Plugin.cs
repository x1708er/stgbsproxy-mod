using System;
using System.Net;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using IPA;
using IPALogger = IPA.Logging.Logger;

namespace STGBSProxy
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal const string HarmonyId = "de.stgmultigaming.bsproxy";
        internal const string Version = "0.3.0";

        internal static IPALogger Log;

        /// <summary>Nur wenn true werden URLs umgeschrieben (fail-open).</summary>
        internal static volatile bool Active;

        /// <summary>Anzahl umgeschriebener Requests (Session).</summary>
        internal static int RewriteCount;

        /// <summary>UTC-Ticks des letzten Rewrites (0 = noch keiner), Zugriff via Interlocked.</summary>
        internal static long LastRewriteTicksUtc;

        /// <summary>Ergebnis des letzten Healthchecks (z.B. "HTTP 200"), fuer das Status-Menue.</summary>
        internal static volatile string LastProbeDetail;

        /// <summary>UTC-Ticks des letzten Healthchecks (0 = noch keiner), Zugriff via Interlocked.</summary>
        internal static long LastProbeTicksUtc;

        // ---- Telemetrie abgeschlossener Proxy-Downloads (SendWebRequestPatch
        //      schreibt im completed-Callback, StatusHost liest; Interlocked). ----
        internal static long CacheHits;
        internal static long CacheMisses;
        internal static long CacheUnknown;
        internal static long ProxyBytesTotal;
        internal static long ProxyBytesFromCache;

        /// <summary>
        /// Wird bei jeder Laufzeitaenderung von ProxyBase erhoeht (Menue oder
        /// Hot-Reload). MenuRegistrar loest darauf den Ping-DNS neu auf.
        /// Schreiben via Interlocked, Lesen plain (int-Reads sind atomar).
        /// </summary>
        internal static int ConfigGeneration;

        private static Plugin _instance;

        private Harmony _harmony;
        private Thread _healthThread;
        private ConfigWatcher _configWatcher;
        private volatile bool _running = true;

        /// <summary>Breaker/Test-Button/ProxyBase-Wechsel: beim naechsten Durchlauf
        /// proben, auch wenn healthCheck=false. Wird erst bei Probe-Erfolg geloescht.</summary>
        private volatile bool _forceProbe;

        /// <summary>Weckt den Health-Loop sofort (Toggle, Test-Button, Breaker, Exit).</summary>
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        /// <summary>Zuletzt geloggter Aktiv-Zustand: -1 = noch keiner, 0 = aus, 1 = an.
        /// Nur fuer Transition-Logging; via Interlocked, weil der Breaker (Main-Thread)
        /// und der Health-Thread beide schreiben.</summary>
        private int _lastLoggedState = -1;

        [Init]
        public Plugin(IPALogger logger)
        {
            Log = logger;
            _instance = this;
            PluginConfig.Load();
        }

        [OnStart]
        public void OnStart()
        {
            // Der Patch wird IMMER angewendet und rein ueber den Laufzeit-Flag
            // Active gegated (Prefix ohne Active ist ein No-Op). Nur so koennen
            // Menue-Toggle und Hot-Reload die Umleitung ohne Neustart schalten.
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info($"Harmony-Patch aktiv (gegated ueber Active). Proxy-Ziel: {PluginConfig.ProxyBase}");
            }
            catch (Exception e)
            {
                Log.Error("Harmony-Patching fehlgeschlagen -> Mod bleibt passiv: " + e);
                return;
            }

            try
            {
                StatusMenu.Init();
            }
            catch (Exception e)
            {
                Log.Warn("Status-Menue konnte nicht angelegt werden (Mod laeuft normal weiter): " + e.Message);
            }

            _configWatcher = new ConfigWatcher();
            _configWatcher.Start();

            try
            {
                RelayStats.Start();
            }
            catch (Exception e)
            {
                Log.Warn("Relay-Statistik konnte nicht gestartet werden (nur die Menue-Anzeige fehlt): " + e.Message);
            }

            if (!PluginConfig.Enabled)
                Log.Info("Per Config deaktiviert -> Rewriting AUS, per Menue-Toggle jederzeit aktivierbar.");

            // Der Loop laeuft immer und bildet alle Zustaende ab
            // (Enabled/HealthCheck/Breaker) — siehe HealthLoop.
            _healthThread = new Thread(HealthLoop)
            {
                IsBackground = true,
                Name = "STGBSProxy-Health"
            };
            _healthThread.Start();
        }

        [OnExit]
        public void OnExit()
        {
            try
            {
                _running = false;
                Active = false;
                try { _wake.Set(); } catch { }
                try { RelayStats.Stop(); } catch { }
                try { _configWatcher?.Dispose(); } catch { }
                try { _harmony?.UnpatchSelf(); } catch { /* Spiel beendet sich eh */ }

                long hits = Interlocked.Read(ref CacheHits);
                long misses = Interlocked.Read(ref CacheMisses);
                long mb = Interlocked.Read(ref ProxyBytesTotal) / (1024 * 1024);
                Log?.Info($"Beendet. Session: {RewriteCount} umgeleitete Requests, Cache {hits} HIT / {misses} MISS, ~{mb} MB via Proxy.");
            }
            catch (Exception e)
            {
                try { Log?.Error("OnExit-Fehler: " + e.Message); } catch { }
            }
        }

        // =================================================================
        //  Schnittstellen fuer Menue, Breaker und Hot-Reload
        // =================================================================

        /// <summary>"Jetzt testen"-Button: Probe so bald wie moeglich ausfuehren
        /// (nie blockierend auf dem Main-Thread). Laeuft auch im AUS-Zustand,
        /// damit man die Erreichbarkeit pruefen kann, bevor man einschaltet.</summary>
        internal static void RequestProbe()
        {
            Plugin inst = _instance;
            if (inst == null)
                return;
            inst._forceProbe = true;
            inst._wake.Set();
        }

        /// <summary>
        /// Circuit-Breaker (SendWebRequestPatch): mehrere echte Downloads in
        /// Folge gescheitert -> Rewriting SOFORT aus, nicht erst beim
        /// naechsten periodischen Probe. Reaktiviert wird ausschliesslich
        /// ueber einen erfolgreichen Healthcheck.
        /// </summary>
        internal static void TripBreaker(string reason)
        {
            Active = false;
            Plugin inst = _instance;
            if (inst != null)
                Interlocked.Exchange(ref inst._lastLoggedState, 0);
            Log?.Notice("CDN-Rewriting sofort AUS — " + reason + ". Downloads laufen direkt zu BeatSaver; der Healthcheck prueft, wann der Proxy zurueck ist.");
            RequestProbe();
        }

        /// <summary>Menue-Setter und Hot-Reload melden hier geaenderte Werte.</summary>
        internal static void OnRuntimeConfigChanged(bool proxyBaseChanged)
        {
            Plugin inst = _instance;
            if (inst == null)
                return;

            if (proxyBaseChanged)
            {
                Interlocked.Increment(ref ConfigGeneration);
                SendWebRequestPatch.ResetFailureStreak();
                // Neues Ziel sofort verifizieren statt blind weiterzuleiten.
                inst._forceProbe = true;
            }
            inst._wake.Set();
        }

        // =================================================================
        //  Health-Loop
        // =================================================================

        /// <summary>
        /// Fail-open-Logik, jetzt fuer die GESAMTE Session: Rewriting ist nur
        /// aktiv, solange der Proxy nachweislich antwortet (2xx/3xx auf
        /// /health). Der Loop laeuft dauerhaft im RetrySeconds-Takt weiter
        /// und setzt Active bei jedem Ergebnis. Hysterese gegen Flattern:
        /// deaktiviert wird erst nach 2 aufeinanderfolgenden Fehlschlaegen
        /// (Kontrollprobe nach 5 s), reaktiviert sofort beim ersten Erfolg.
        /// Der komplette Thread ist gepanzert: Eine Exception hier darf
        /// niemals unbehandelt auf einem Background-Thread landen.
        /// </summary>
        private void HealthLoop()
        {
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
                catch { /* je nach Runtime-Profil nicht noetig */ }

                int failStreak = 0;

                while (_running)
                {
                    if (!PluginConfig.Enabled)
                    {
                        failStreak = 0;
                        if (_forceProbe)
                        {
                            // Test-Button im AUS-Zustand: Ergebnis nur anzeigen.
                            _forceProbe = false;
                            RunProbe(out _);
                        }
                        SetActive(false, "per Einstellung deaktiviert");
                        _wake.WaitOne(1000);
                        continue;
                    }

                    if (!PluginConfig.HealthCheck && !_forceProbe)
                    {
                        failStreak = 0;
                        SetActive(true, "Healthcheck per Config deaktiviert");
                        _wake.WaitOne(1000);
                        continue;
                    }

                    bool ok = RunProbe(out string detail);

                    if (ok)
                    {
                        failStreak = 0;
                        _forceProbe = false;
                        SendWebRequestPatch.ResetFailureStreak();
                        SetActive(true, "Proxy erreichbar (" + detail + ")");
                        WaitSeconds(PluginConfig.RetrySeconds);
                    }
                    else
                    {
                        failStreak++;
                        if (Active && failStreak < 2)
                        {
                            // Hysterese: einzelner Fehlschlag koennte ein
                            // Schluckauf sein -> aktiv bleiben, schnell nachpruefen.
                            Log.Warn($"Healthcheck fehlgeschlagen ({detail}) -> Rewriting bleibt vorerst AKTIV, Kontrollprobe in 5 s.");
                            WaitSeconds(5);
                        }
                        else
                        {
                            SetActive(false, "Proxy nicht nutzbar (" + detail + ")");
                            WaitSeconds(PluginConfig.RetrySeconds);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Active = false;
                try { Log?.Error("HealthLoop abgebrochen -> Rewriting AUS (fail-open): " + e); } catch { }
            }
        }

        /// <summary>Setzt Active und loggt nur echte Zustandswechsel.</summary>
        private void SetActive(bool value, string reason)
        {
            Active = value;
            int state = value ? 1 : 0;
            if (Interlocked.Exchange(ref _lastLoggedState, state) == state)
                return;
            if (value)
                Log.Info("CDN-Rewriting AKTIV — " + reason + ".");
            else
                Log.Notice("CDN-Rewriting AUS — " + reason + ". Downloads laufen direkt zu BeatSaver.");
        }

        /// <summary>Wartet, laesst sich aber jederzeit per _wake unterbrechen.</summary>
        private void WaitSeconds(int seconds)
        {
            _wake.WaitOne(TimeSpan.FromSeconds(Math.Max(1, seconds)));
        }

        private static bool RunProbe(out string detail)
        {
            bool ok = Probe(out detail);
            LastProbeDetail = detail;
            Interlocked.Exchange(ref LastProbeTicksUtc, DateTime.UtcNow.Ticks);
            return ok;
        }

        private static bool Probe(out string detail)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(PluginConfig.ProxyBase + "/health");
                req.Method = "HEAD";
                req.Timeout = PluginConfig.HealthTimeoutMs;
                req.ReadWriteTimeout = PluginConfig.HealthTimeoutMs;
                req.AllowAutoRedirect = true;
                req.UserAgent = "STGBSProxy/" + Version;

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    detail = "HTTP " + (int)resp.StatusCode;
                    return true; // GetResponse wirft bei 4xx/5xx -> hier nur 2xx/3xx
                }
            }
            catch (WebException we) when (we.Response is HttpWebResponse r)
            {
                // Server antwortet, aber lehnt ab (404 = /health fehlt):
                // NICHT aktivieren, sonst laufen alle Downloads gegen die Wand.
                detail = "HTTP " + (int)r.StatusCode + " — nginx-Config pruefen";
                r.Close();
                return false;
            }
            catch (Exception e)
            {
                detail = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }
    }
}
