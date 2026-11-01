using System;
using System.Threading;
using HarmonyLib;
using UnityEngine.Networking;

namespace STGBSProxy
{
    /// <summary>
    /// Patcht UnityWebRequest.SendWebRequest statt BeatSaverSharp:
    /// erwischt damit BeatSaverDownloader, MultiplayerCore, PlaylistManager
    /// und jede andere Mod, die ueber UnityWebRequest laedt — ohne eigene
    /// Abhaengigkeit auf deren Assemblies.
    ///
    /// https://r2cdn.beatsaver.com/&lt;hash&gt;.zip  -> {ProxyBase}/r2cdn/&lt;hash&gt;.zip
    /// https://cfcdn.beatsaver.com/&lt;hash&gt;.jpg -> {ProxyBase}/cfcdn/&lt;hash&gt;.jpg
    /// api.beatsaver.com wird bewusst NICHT angefasst (liegt nicht hinter
    /// Cloudflare, kein Peering-Problem).
    /// </summary>
    [HarmonyPatch(typeof(UnityWebRequest), "SendWebRequest")]
    internal static class SendWebRequestPatch
    {
        // Live gegen die BeatSaver-API verifiziert (Stand 2026-07):
        // Zips -> r2cdn, Cover/Previews -> cfcdn; cdn.* als Legacy-Fallback.
        private static readonly string[] Hosts =
        {
            "r2cdn.beatsaver.com",
            "cfcdn.beatsaver.com",
            "cdn.beatsaver.com"
        };

        private static bool _errorLogged;

        [HarmonyPrefix]
        private static void Prefix(UnityWebRequest __instance)
        {
            try
            {
                if (!Plugin.Active)
                    return;

                string url = __instance.url;
                if (string.IsNullOrEmpty(url))
                    return;

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u))
                    return;
                if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
                    return;

                string host = u.Host.ToLowerInvariant();
                bool match = false;
                for (int i = 0; i < Hosts.Length; i++)
                {
                    if (host == Hosts[i]) { match = true; break; }
                }
                if (!match)
                    return;

                // "r2cdn.beatsaver.com" -> Pfadpraefix "r2cdn"
                string prefix = host.Substring(0, host.IndexOf('.'));
                __instance.url = PluginConfig.ProxyBase + "/" + prefix + u.PathAndQuery;

                int n = Interlocked.Increment(ref Plugin.RewriteCount);
                Interlocked.Exchange(ref Plugin.LastRewriteTicksUtc, DateTime.UtcNow.Ticks);
                if (n <= 5 || n % 25 == 0)
                    Plugin.Log?.Info($"Rewrite #{n}: {host}{u.AbsolutePath} -> Proxy");
                else
                    Plugin.Log?.Debug($"Rewrite #{n}: {host}{u.PathAndQuery}");
            }
            catch (Exception e)
            {
                // Ein Fehler hier darf NIEMALS einen Request abschiessen.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Plugin.Log?.Error("Rewrite-Prefix fehlgeschlagen (weitere Fehler werden unterdrueckt): " + e);
                }
            }
        }

        // =================================================================
        //  Ergebnis-Beobachtung umgeleiteter Requests:
        //  - Circuit-Breaker: N echte Downloads in Folge kaputt -> sofort
        //    deaktivieren statt auf den naechsten periodischen Probe warten.
        //  - Cache-Telemetrie: X-Cache-Status (setzt der nginx) + Volumen.
        //  Der completed-Callback laeuft auf dem Main-Thread. Unser Handler
        //  wird VOR dem des Spiels registriert (der Aufrufer sieht die
        //  AsyncOperation erst, nachdem SendWebRequest — inkl. Postfix —
        //  zurueckgekehrt ist), laeuft also vor einem Dispose im Spiel-Handler.
        //  Trotzdem ist jeder Zugriff auf den Request gepanzert.
        // =================================================================

        /// <summary>Nach so vielen in Folge gescheiterten Proxy-Downloads
        /// wird das Rewriting sofort deaktiviert (fail-open).</summary>
        private const int FailureThreshold = 3;

        private static int _failureStreak;
        private static bool _postfixErrorLogged;

        /// <summary>Health-Loop meldet "Proxy wieder ok" bzw. Basis-Wechsel.</summary>
        internal static void ResetFailureStreak() =>
            Interlocked.Exchange(ref _failureStreak, 0);

        [HarmonyPostfix]
        private static void Postfix(UnityWebRequest __instance, UnityWebRequestAsyncOperation __result)
        {
            try
            {
                if (__result == null || __instance == null)
                    return;

                string url = __instance.url;
                if (string.IsNullOrEmpty(url))
                    return;

                // Nur Requests beobachten, die (vom Prefix) zum Proxy zeigen.
                if (!url.StartsWith(PluginConfig.ProxyBase + "/", StringComparison.OrdinalIgnoreCase))
                    return;

                UnityWebRequest req = __instance;
                __result.completed += _ => OnProxyRequestCompleted(req, url);
            }
            catch (Exception e)
            {
                if (!_postfixErrorLogged)
                {
                    _postfixErrorLogged = true;
                    Plugin.Log?.Error("Ergebnis-Beobachtung fehlgeschlagen (weitere Fehler werden unterdrueckt): " + e);
                }
            }
        }

        private static void OnProxyRequestCompleted(UnityWebRequest req, string url)
        {
            try
            {
                UnityWebRequest.Result result = req.result;

                if (result == UnityWebRequest.Result.Success)
                {
                    Interlocked.Exchange(ref _failureStreak, 0);
                    RecordSuccess(req);
                    return;
                }

                long code = 0;
                try { code = req.responseCode; } catch { /* je nach Zustand nicht lesbar */ }

                // 404 & Co. sind Upstream-Antworten (Map geloescht etc.) und
                // sagen nichts ueber den Proxy; 403/429/5xx/ConnectionError schon.
                bool proxyFault =
                    result == UnityWebRequest.Result.ConnectionError ||
                    (result == UnityWebRequest.Result.ProtocolError &&
                     (code >= 500 || code == 403 || code == 429));

                if (!proxyFault)
                {
                    Plugin.Log?.Debug($"Proxy-Download beendet mit {result} (HTTP {code}): {url}");
                    return;
                }

                int n = Interlocked.Increment(ref _failureStreak);
                Plugin.Log?.Warn($"Proxy-Download fehlgeschlagen ({result}, HTTP {code}) [{n}/{FailureThreshold}]: {url}");

                if (n >= FailureThreshold && Plugin.Active)
                {
                    Interlocked.Exchange(ref _failureStreak, 0);
                    Plugin.TripBreaker($"{FailureThreshold} Proxy-Downloads in Folge fehlgeschlagen (zuletzt {result}, HTTP {code})");
                }
            }
            catch (Exception)
            {
                // Request wurde vermutlich schon disposed -> fuer diesen
                // Download gibt es halt keine Telemetrie.
            }
        }

        private static void RecordSuccess(UnityWebRequest req)
        {
            long bytes = 0;
            try { bytes = (long)req.downloadedBytes; } catch { }

            string cacheStatus = null;
            try { cacheStatus = req.GetResponseHeader("X-Cache-Status"); } catch { }

            if (bytes > 0)
                Interlocked.Add(ref Plugin.ProxyBytesTotal, bytes);

            // nginx $upstream_cache_status: alles, was ohne Upstream-Fetch
            // beantwortet wurde, zaehlt als HIT.
            switch ((cacheStatus ?? "").ToUpperInvariant())
            {
                case "HIT":
                case "STALE":
                case "UPDATING":
                case "REVALIDATED":
                    Interlocked.Increment(ref Plugin.CacheHits);
                    if (bytes > 0)
                        Interlocked.Add(ref Plugin.ProxyBytesFromCache, bytes);
                    break;
                case "MISS":
                case "BYPASS":
                case "EXPIRED":
                    Interlocked.Increment(ref Plugin.CacheMisses);
                    break;
                default:
                    Interlocked.Increment(ref Plugin.CacheUnknown);
                    break;
            }
        }
    }
}
