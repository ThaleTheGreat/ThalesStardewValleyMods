using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SpriteMaster.Types.Pooling;

internal sealed class ObjectPool<T> : ISealedObjectPool<T, ObjectPool<T>> where T : class, new() {
    internal static readonly ObjectPool<T> Default = new();

#if DEBUG || DEVELOPMENT
    private readonly ConcurrentSet<T> Pool = new();
#else
	private readonly ConcurrentBag<T> Pool = new();
#endif

#if TRACK_ALLOCATIONS
    private readonly ConditionalWeakTable<T, StackTrace> AllocationTraces = new();

    [Conditional("TRACK_ALLOCATIONS")]
    private void TraceAllocation(T value) {
        AllocationTraces.Add(value, new(skipFrames: 2, fNeedFileInfo: true));
    }

    [Conditional("TRACK_ALLOCATIONS")]
    private void UntraceAllocation(T value) {
        if (!AllocationTraces.Remove(value)) {
            ThrowHelper.ThrowInvalidOperationException("Attempted to return an object to a pool it does not belong to");
        }
    }
#else
    [Conditional("TRACK_ALLOCATIONS")]
    private static void TraceAllocation(T value) { }

    [Conditional("TRACK_ALLOCATIONS")]
    private static void UntraceAllocation(T value) { }
#endif

    public int Count => Pool.Count;

    private long AllocatedInternal = 0L;
    public long Allocated => AllocatedInternal;

    internal ObjectPool() {
    }

    internal ObjectPool(int initialCapacity) {
        for (int i = 0; i < initialCapacity; ++i) {
            Pool.Add(new());
        }
    }

    [MethodImpl(Runtime.MethodImpl.Inline)]
    public T Get() {
        if (!Pool.TryTake(out var result)) {
            result = new();
        }

        Interlocked.Increment(ref AllocatedInternal);

        TraceAllocation(result);

        return result;
    }

    [MethodImpl(Runtime.MethodImpl.Inline)]
    public void Return(T value) {
#if DEBUG || DEVELOPMENT
        if (value is null) {
            ThrowHelper.ThrowArgumentNullException($"{nameof(Return)} value {nameof(value)} is null");
        }

        UntraceAllocation(value);

        if (!Pool.Add(value)) {
            ThrowHelper.ThrowInvalidOperationException($"Object {value} already exists in {nameof(ObjectPool<T>)}");
        }
#else
		UntraceAllocation(value);

		Pool.Add(value);
#endif

        Interlocked.Decrement(ref AllocatedInternal);
    }

    internal KeyValuePair<T, StackTrace>[] GetAllocationTraces() {
#if TRACK_ALLOCATIONS
        return AllocationTraces.ToArray();
#else
        return Array.Empty<KeyValuePair<T, StackTrace>>();
#endif
    }
}
