// Compile-time stubs fuer HarmonyX (0Harmony.dll, wie von BSIPA 4.3.x geliefert).
// Ersetzt das HarmonyX-NuGet-Paket, das MonoMod.Backports als transitive
// Compile-Referenz einschleppte -> Roslyn kompilierte $-Interpolationen gegen
// dessen DefaultInterpolatedStringHandler -> FileNotFoundException zur Laufzeit.
// Diese Assembly wird NIE ausgeliefert; zur Laufzeit bindet der Loader das
// echte 0Harmony (nicht strong-named -> versionsunabhaengig).
//
// Klassennamen exakt wie in Harmony: "HarmonyPatch"/"HarmonyPrefix" OHNE
// "Attribute"-Suffix, damit die Metadaten-Referenzen zur Laufzeit aufloesen.

using System;
using System.Reflection;

namespace HarmonyLib
{
    public class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll(Assembly assembly) { }
        public void UnpatchSelf() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute
    {
        public HarmonyPrefix() { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute
    {
        public HarmonyPostfix() { }
    }
}
