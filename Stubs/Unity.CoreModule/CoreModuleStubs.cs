// Compile-time stub for UnityEngine.CoreModule (Unity 2021.3).
// Only the members referenced by the status menu are declared; member
// resolution happens at runtime against the real, already-loaded module.
// The inheritance chain (Object <- Component <- Behaviour <- MonoBehaviour)
// must match the real one, base-type references are baked into metadata.
// NEVER shipped.

using System;

namespace UnityEngine
{
    public class Object
    {
        public static void DontDestroyOnLoad(Object target) { }
    }

    public class Component : Object { }

    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component => default;
    }

    public sealed class Ping
    {
        public Ping(string address) { }
        public bool isDone => default;
        public int time => default;
        public void DestroyPing() { }
    }

    public class YieldInstruction { }

    public class AsyncOperation : YieldInstruction
    {
        // Real signature (Unity 2021.3): invoked on the main thread; if the
        // operation is already done, the handler runs immediately on add.
        public event Action<AsyncOperation> completed { add { } remove { } }
    }
}
