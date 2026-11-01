using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace STGBSProxy
{
    /// <summary>
    /// Bewusst ohne BSIPA-Config-System / Newtonsoft gehalten:
    /// flaches JSON in UserData/STGBSProxy.json, von Hand geparst.
    /// Null externe Abhängigkeiten = null Versionsrisiko.
    /// </summary>
    internal static class PluginConfig
    {
        public static bool Enabled = true;
        public static string ProxyBase = "https://bsc.stgmultigaming.de";
        public static string Secret = "";
        public static bool HealthCheck = true;
        public static int HealthTimeoutMs = 2000;
        public static int RetrySeconds = 60;

        public static string ConfigPath { get; private set; } = "";

        /// <summary>Exakter Inhalt des letzten eigenen Save() — der
        /// ConfigWatcher erkennt daran eigene Schreibvorgaenge und loest
        /// dafuer keinen Hot-Reload aus.</summary>
        public static volatile string LastWrittenJson;

        private const string FileName = "STGBSProxy.json";

        public static void Load()
        {
            try
            {
                string userData = FindUserDataDir();
                Directory.CreateDirectory(userData);
                ConfigPath = Path.Combine(userData, FileName);

                if (!File.Exists(ConfigPath))
                {
                    Save(); // Default-Config anlegen, damit Teamkollegen was zum Editieren haben
                    return;
                }

                string json = File.ReadAllText(ConfigPath);
                Enabled         = GetBool(json, "enabled", Enabled);
                ProxyBase       = GetString(json, "proxyBase", ProxyBase).Trim().TrimEnd('/');
                Secret          = GetString(json, "secret", Secret);
                HealthCheck     = GetBool(json, "healthCheck", HealthCheck);
                HealthTimeoutMs = GetInt(json, "healthTimeoutMs", HealthTimeoutMs);
                RetrySeconds    = Math.Max(5, GetInt(json, "retrySeconds", RetrySeconds));

                if (!IsValidProxyBase(ProxyBase))
                {
                    Plugin.Log?.Warn($"proxyBase '{ProxyBase}' ist keine gueltige URL -> Mod deaktiviert.");
                    Enabled = false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("Config konnte nicht geladen werden, nutze Defaults: " + e.Message);
            }
        }

        public static void Save()
        {
            try
            {
                string json =
                    "{\n" +
                    $"  \"enabled\": {(Enabled ? "true" : "false")},\n" +
                    $"  \"proxyBase\": \"{Escape(ProxyBase)}\",\n" +
                    $"  \"secret\": \"{Escape(Secret)}\",\n" +
                    $"  \"healthCheck\": {(HealthCheck ? "true" : "false")},\n" +
                    $"  \"healthTimeoutMs\": {HealthTimeoutMs},\n" +
                    $"  \"retrySeconds\": {RetrySeconds}\n" +
                    "}\n";
                // Vor dem Schreiben merken, damit der Watcher-Vergleich nicht
                // mit dem Dateisystem-Event racen kann.
                LastWrittenJson = json;
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("Config konnte nicht geschrieben werden: " + e.Message);
            }
        }

        /// <summary>Gleiche Pruefung fuer Load() und den Menue-Setter.</summary>
        public static bool IsValidProxyBase(string s)
        {
            return !string.IsNullOrEmpty(s) &&
                   (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                   Uri.TryCreate(s, UriKind.Absolute, out _);
        }

        /// <summary>Plugins/STGBSProxy.dll -> &lt;game&gt;/UserData, mit CWD-Fallback.</summary>
        private static string FindUserDataDir()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string pluginsDir = Path.GetDirectoryName(loc);
                    string gameDir = pluginsDir != null ? Path.GetDirectoryName(pluginsDir) : null;
                    if (!string.IsNullOrEmpty(gameDir))
                        return Path.Combine(gameDir, "UserData");
                }
            }
            catch { /* fallthrough */ }
            return Path.Combine(Directory.GetCurrentDirectory(), "UserData");
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string GetString(string json, string key, string def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : def;
        }

        private static bool GetBool(string json, string key, bool def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success ? string.Equals(m.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase) : def;
        }

        private static int GetInt(string json, string key, int def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : def;
        }
    }
}
