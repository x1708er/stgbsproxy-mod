// Compile-time stub for UnityEngine.UnityWebRequestModule (Unity 2021.3).
// Only the members referenced by the Harmony patch are declared; member
// resolution happens at runtime against the real, already-loaded module.
// The Result enum values are baked into our IL as ints -> order must match
// Unity exactly (InProgress=0 .. DataProcessingError=4).
// NEVER shipped.

using System;

namespace UnityEngine.Networking
{
    public class UnityWebRequest : IDisposable
    {
        public enum Result
        {
            InProgress,         // = 0
            Success,            // = 1
            ConnectionError,    // = 2
            ProtocolError,      // = 3
            DataProcessingError // = 4
        }

        public string url { get; set; }
        public Result result => default;
        public long responseCode => default;
        public ulong downloadedBytes => default;
        public string GetResponseHeader(string name) => default;
        public void SetRequestHeader(string name, string value) { }
        public void Dispose() { }
    }

    public class UnityWebRequestAsyncOperation : AsyncOperation
    {
        public UnityWebRequest webRequest => default;
    }
}
