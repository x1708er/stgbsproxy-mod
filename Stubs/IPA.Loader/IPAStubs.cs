// Compile-time stubs for BSIPA's IPA.Loader.
// Signatures verified against https://github.com/nike4613/BeatSaber-IPA-Reloaded
//   - PluginInterfaces/Attributes/PluginAttribute.cs
//   - PluginInterfaces/Attributes/LifecycleAttributes.cs
//   - Logging/Logger.cs
// This assembly is NEVER shipped; at runtime the real IPA.Loader.dll
// (not strong-named -> version-insensitive binding) is used.

using System;

namespace IPA
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PluginAttribute : Attribute
    {
        public RuntimeOptions RuntimeOptions { get; }
        public PluginAttribute(RuntimeOptions runtimeOptions) => RuntimeOptions = runtimeOptions;
    }

    // Order matters: values are baked into attribute metadata as ints.
    public enum RuntimeOptions
    {
        SingleStartInit, // = 0
        DynamicInit      // = 1
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class InitAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class OnStartAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class OnExitAttribute : Attribute { }
}

namespace IPA.Logging
{
    public abstract class Logger
    {
        public virtual void Trace(string message) { }
        public virtual void Debug(string message) { }
        public virtual void Info(string message) { }
        public virtual void Notice(string message) { }
        public virtual void Warn(string message) { }
        public virtual void Error(string message) { }
        public virtual void Critical(string message) { }
    }
}
