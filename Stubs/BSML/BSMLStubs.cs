// Compile-time stubs fuer BeatSaberMarkupLanguage (BSML.dll).
// Signaturen verifiziert gegen https://github.com/monkeymanboy/BeatSaberMarkupLanguage
// (Stand 1.14.2, Beat Saber 1.40.x):
//   - Settings/BSMLSettings.cs   (AddSettingsMenu(string, string, object))
//   - Util/ZenjectSingleton.cs   (statisches Instance, wirft solange der
//                                 Menue-DI-Container nicht steht!)
//   - Attributes/UIValue.cs
// Wichtig: BSMLSettings erbt von ZenjectSingleton<T> -> der Compiler backt
// die Instance-Referenz auf die BASISKLASSE in die Metadaten, deshalb muss
// die Vererbung hier exakt nachgebildet sein.
// Diese Assembly wird NIE ausgeliefert; zur Laufzeit bindet der Loader das
// echte BSML (nicht strong-named -> versionsunabhaengig).

using System;

namespace BeatSaberMarkupLanguage.Util
{
    public abstract class ZenjectSingleton<T>
        where T : class
    {
        public static T Instance => default;
    }
}

namespace BeatSaberMarkupLanguage.Settings
{
    public class BSMLSettings : Util.ZenjectSingleton<BSMLSettings>
    {
        public void AddSettingsMenu(string name, string resource, object host) { }
    }
}

namespace BeatSaberMarkupLanguage.Attributes
{
    // Bewusst ohne [AttributeUsage] wie das Original.
    public class UIValue : Attribute
    {
        public UIValue(string id) { }
    }

    // Attributes/UIAction.cs — Signatur identisch zum Original.
    public class UIAction : Attribute
    {
        public UIAction(string id) { }
    }
}
