using System;
using System.IO;
using System.Threading;

namespace STGBSProxy
{
    /// <summary>
    /// Hot-Reload: ueberwacht UserData/STGBSProxy.json und laedt die Config
    /// bei externen Aenderungen ohne Neustart neu. Eigene Save()-Vorgaenge
    /// (Menue-Setter) werden ueber den Inhaltsvergleich mit
    /// PluginConfig.LastWrittenJson erkannt und ignoriert. Editoren feuern
    /// gern mehrere Events hintereinander -> 500-ms-Debounce ueber einen
    /// One-Shot-Timer. Komplett gepanzert: Fehler hier duerfen die Mod nie
    /// beeintraechtigen, schlimmstenfalls gibt es keinen Hot-Reload.
    /// </summary>
    internal sealed class ConfigWatcher : IDisposable
    {
        private const int DebounceMs = 500;

        private readonly object _gate = new object();
        private FileSystemWatcher _fsw;
        private Timer _debounce;
        private int _retriesLeft;

        public void Start()
        {
            try
            {
                string path = PluginConfig.ConfigPath;
                if (string.IsNullOrEmpty(path))
                    return;
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;

                _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);

                _fsw = new FileSystemWatcher(dir, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName |
                                   NotifyFilters.Size | NotifyFilters.CreationTime
                };
                _fsw.Changed += OnFsEvent;
                _fsw.Created += OnFsEvent;
                _fsw.Renamed += OnFsEvent;
                _fsw.EnableRaisingEvents = true;
                Plugin.Log?.Info("Config-Hot-Reload aktiv: " + path);
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("Config-Watcher konnte nicht starten (kein Hot-Reload, Mod laeuft normal weiter): " + e.Message);
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                _retriesLeft = 3;
                _debounce?.Change(DebounceMs, Timeout.Infinite);
            }
            catch { /* Timer evtl. schon disposed */ }
        }

        private void Reload()
        {
            try
            {
                lock (_gate)
                {
                    string json;
                    try
                    {
                        json = File.ReadAllText(PluginConfig.ConfigPath);
                    }
                    catch (IOException)
                    {
                        // Datei evtl. noch vom Editor gesperrt -> spaeter erneut.
                        if (_retriesLeft-- > 0)
                            _debounce?.Change(DebounceMs, Timeout.Infinite);
                        return;
                    }

                    if (json == PluginConfig.LastWrittenJson)
                        return; // unser eigener Save()

                    bool oldEnabled = PluginConfig.Enabled;
                    string oldBase = PluginConfig.ProxyBase;
                    string oldSecret = PluginConfig.Secret;
                    bool oldHealth = PluginConfig.HealthCheck;
                    int oldTimeout = PluginConfig.HealthTimeoutMs;
                    int oldRetry = PluginConfig.RetrySeconds;

                    PluginConfig.Load();

                    bool baseChanged = oldBase != PluginConfig.ProxyBase;
                    bool anyChanged = baseChanged ||
                                      oldEnabled != PluginConfig.Enabled ||
                                      oldSecret != PluginConfig.Secret ||
                                      oldHealth != PluginConfig.HealthCheck ||
                                      oldTimeout != PluginConfig.HealthTimeoutMs ||
                                      oldRetry != PluginConfig.RetrySeconds;

                    if (!anyChanged)
                    {
                        Plugin.Log?.Debug("Config-Datei geaendert, aber keine Wertaenderung -> nichts zu tun.");
                        return;
                    }

                    Plugin.Log?.Info("Config extern geaendert -> neu geladen"
                        + (baseChanged ? ", neues Proxy-Ziel: " + PluginConfig.ProxyBase : "") + ".");
                    Plugin.OnRuntimeConfigChanged(baseChanged);
                }
            }
            catch (Exception e)
            {
                try { Plugin.Log?.Warn("Config-Hot-Reload fehlgeschlagen: " + e.Message); } catch { }
            }
        }

        public void Dispose()
        {
            try
            {
                if (_fsw != null)
                {
                    _fsw.EnableRaisingEvents = false;
                    _fsw.Dispose();
                }
            }
            catch { }
            try { _debounce?.Dispose(); } catch { }
        }
    }
}
