using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace STGBSProxy
{
    /// <summary>
    /// Liest die oeffentliche Relay-Statistik ein ({ProxyBase}/stats/stats.json,
    /// generiert von der Server-Stats-Seite) im Hintergrund alle 5 min und
    /// stellt daraus zwei Anzeige-Zeilen fuer das Status-Menue bereit.
    ///
    /// Bewusst KEINE Kopplung an den Healthcheck: Schlaegt der Fetch fehl
    /// (anderer Proxy ohne /stats/), bleiben die Zeilen einfach auf "keine
    /// Daten" — Umleitung und Fail-open-Logik sind davon unberuehrt.
    /// JSON wird wie in PluginConfig per Regex gelesen: null externe
    /// Abhaengigkeiten. Der komplette Thread ist gepanzert, eine Exception
    /// hier darf niemals die Mod beeintraechtigen.
    /// </summary>
    internal static class RelayStats
    {
        private const int FetchIntervalMs = 5 * 60 * 1000; // Takt der Stats-Seite
        private const int FetchTimeoutMs = 10 * 1000;

        /// <summary>Fertig formatierte Menue-Zeilen; null = noch kein Fetch erfolgreich.</summary>
        internal static volatile string TotalsLine;
        internal static volatile string TempoLine;

        /// <summary>UTC-Ticks des letzten erfolgreichen Fetch (0 = noch keiner).</summary>
        internal static long FetchedTicksUtc;

        private static readonly Regex TotalsRe =
            new Regex("\"totals\"\\s*:\\s*\\{([^}]*)\\}", RegexOptions.Compiled);

        private static Thread _thread;
        private static volatile bool _running = true;
        private static int _nextFetchTick = Environment.TickCount; // erster Fetch sofort
        private static int _lastGen = int.MinValue;

        internal static void Start()
        {
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "STGBSProxy-RelayStats"
            };
            _thread.Start();
        }

        internal static void Stop()
        {
            _running = false;
        }

        private static void Loop()
        {
            try
            {
                while (_running)
                {
                    // ProxyBase-Wechsel (Menue/Hot-Reload) -> sofort neu holen.
                    int gen = Plugin.ConfigGeneration;
                    if (gen != _lastGen || Environment.TickCount - _nextFetchTick >= 0)
                    {
                        _lastGen = gen;
                        _nextFetchTick = Environment.TickCount + FetchIntervalMs;
                        try { Fetch(); }
                        catch (Exception e)
                        {
                            Plugin.Log?.Debug("Relay-Statistik nicht abrufbar (naechster Versuch in 5 min): "
                                              + e.GetType().Name + ": " + e.Message);
                        }
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception e)
            {
                try { Plugin.Log?.Warn("RelayStats-Loop beendet: " + e.Message); } catch { }
            }
        }

        private static void Fetch()
        {
            var req = (HttpWebRequest)WebRequest.Create(PluginConfig.ProxyBase + "/stats/stats.json");
            req.Method = "GET";
            req.Timeout = FetchTimeoutMs;
            req.ReadWriteTimeout = FetchTimeoutMs;
            req.UserAgent = "STGBSProxy/" + Plugin.Version;

            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
                json = sr.ReadToEnd();

            Parse(json);
            Interlocked.Exchange(ref FetchedTicksUtc, DateTime.UtcNow.Ticks);
        }

        private static void Parse(string json)
        {
            var m = TotalsRe.Match(json);
            if (!m.Success)
                return; // unbekanntes Format -> Zeilen unangetastet lassen
            string t = m.Groups[1].Value;

            long requests = GetLong(t, "requests", -1);
            if (requests < 0)
                return;

            long hits = GetLong(t, "hits", 0);
            long bytesTotal = GetLong(t, "bytes_total", 0);
            long bytesCache = GetLong(t, "bytes_cache", 0);
            double? dl = GetDouble(t, "dl_bps");
            double? up = GetDouble(t, "up_bps");

            if (requests == 0)
            {
                TotalsLine = "Relay gesamt: noch nichts aufgezeichnet";
            }
            else
            {
                int hitPct = (int)Math.Round(100.0 * hits / requests);
                TotalsLine = "Relay gesamt: " + requests + " Dateien, "
                           + FmtBytes(bytesTotal) + " (" + hitPct + " % aus dem Server-Cache, "
                           + FmtBytes(bytesCache) + ")";
            }

            TempoLine = (dl == null && up == null)
                ? "Relay-Tempo: noch keine Messwerte"
                : "Relay-Tempo: " + FmtSpeed(dl) + " an Spieler | " + FmtSpeed(up) + " von BeatSaver";
        }

        private static long GetLong(string s, string key, long def)
        {
            var m = Regex.Match(s, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            return m.Success && long.TryParse(m.Groups[1].Value, out long v) ? v : def;
        }

        private static double? GetDouble(string s, string key)
        {
            var m = Regex.Match(s, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            if (!m.Success)
                return null;
            return double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double d) ? d : (double?)null;
        }

        private static string FmtSpeed(double? bps)
        {
            return bps == null ? "-" : FmtBytes((long)Math.Round(bps.Value)) + "/s";
        }

        private static string FmtBytes(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024L * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + " MB";
            return (b / 1073741824.0).ToString("0.##") + " GB";
        }
    }
}
