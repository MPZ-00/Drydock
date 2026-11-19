using Keen.VRage.Library.Memory;
using Keen.VRage.Library.Reflection;
using Keen.VRage.Library.Serialization;

namespace Drydock.Core;

/// <summary>The minimum of engine state VRage.Library needs to parse archives outside the game.</summary>
static class VRageRuntime
{
    static readonly Lock Gate = new();
    static bool _metadataPushed;

    public static void EnsureInitialized()
    {
        // the bump allocator is per thread
        if (!BumpAllocator.Instance.Initialized)
            BumpAllocator.Instance.Initialize();
        lock (Gate)
        {
            if (_metadataPushed)
                return;
            MetadataManager.Instance.PushContext(typeof(SerializationContext).Assembly);
            _metadataPushed = true;
        }
    }
}
