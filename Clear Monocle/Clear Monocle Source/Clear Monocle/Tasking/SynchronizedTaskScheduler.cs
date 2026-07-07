using SpriteMaster.Configuration;
using SpriteMaster.Extensions;
using SpriteMaster.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpriteMaster.Tasking;

[DebuggerTypeProxy(typeof(SynchronizedTaskSchedulerDebugView))]
[DebuggerDisplay("Id={Id}, ScheduledTasks = {DebugTaskCount}")]
internal sealed class SynchronizedTaskScheduler : TaskScheduler, IDisposable {
    internal static readonly SynchronizedTaskScheduler Instance = new();

    private static bool IsMainThread => ThreadingExt.IsMainThread;

    internal sealed class TextureActionTask : Task {
        internal readonly TextureAction ActionData;
        internal readonly Priority OriginalPriority;
        internal Priority CurrentPriority;

        internal bool IsPriorityDowngraded => OriginalPriority < CurrentPriority;

        internal TextureActionTask(Action action, TextureAction actionData, Priority priority) : base(action) {
            ActionData = actionData;
            OriginalPriority = priority;
            CurrentPriority = priority;
        }
    }

    private class SynchronizedTaskSchedulerDebugView {
        private readonly SynchronizedTaskScheduler Scheduler;

        public SynchronizedTaskSchedulerDebugView(SynchronizedTaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerable<Task> ScheduledTasks => Scheduler.GetScheduledTasks();
    }

    internal enum Priority {
        High = 0,
        Normal,
        Low
    }

    private readonly record struct TextureActionKey(ComparableWeakReference<XTexture2D> Reference, Bounds Bounds);

    private static int PriorityCount => Enum.GetValues(typeof(Priority)).Length;

    private readonly CancellationTokenSource DisposeCancellation = new();

    public sealed class PrioritizedList<T> where T : class {
        private readonly List<T>[] InnerLists;

        internal List<T> this[Priority priority] => InnerLists[(int)priority];

        internal List<T>[] GetEnumerable() => InnerLists;

        internal bool Any {
            get {
                foreach (var innerList in InnerLists) {
                    if (innerList.Count > 0) {
                        return true;
                    }
                }

                return false;
            }
        }

        public PrioritizedList() {
            InnerLists = new List<T>[PriorityCount];

            for (int i = 0; i < InnerLists.Length; ++i) {
                InnerLists[i] = new();
            }
        }

        internal void Clear() {
            foreach (var innerList in InnerLists) {
                innerList.Clear();
            }
        }

        internal bool TryPeek(in Span<int> processed, [NotNullWhen(true)] out T? value, out Priority priority) {
            for (int i = 0; i < InnerLists.Length; ++i) {
                var innerList = InnerLists[i];
                if (innerList.Count > processed[i]) {
                    value = innerList[processed[i]];
                    priority = (Priority)i;
                    return true;
                }
            }

            priority = Priority.Normal;
            value = null;
            return false;
        }

        internal void CopyFrom(PrioritizedList<T> other, Priority priority, int start) {
            var otherList = other[priority];
            var thisList = this[priority];

            lock (this) {
                thisList.AddRange(otherList.GetRange(start, otherList.Count - start));
            }
        }
    }

    private readonly DoubleBuffer<List<Task>> PendingImmediate = new();
    private readonly DoubleBuffer<PrioritizedList<TextureActionTask>> PendingDeferred = new();
    private readonly DoubleBuffer<Dictionary<TextureActionKey, List<TextureActionTask>>> DeferredTaskIndex = new();
    private readonly object DeferredSwapLock = new();

    private readonly TexelTimer TexelAverage = new();

    private int DebugTaskCount {
        get {
            var immediate = PendingImmediate.Both;
            var deferreds = PendingDeferred.Both;

            int count = 0;

            lock (immediate.Item1) lock (immediate.Item2) {
                    count +=
                        immediate.Item1.Count +
                        immediate.Item2.Count;
                }

            lock (deferreds.Item1) {
                foreach (var deferred in deferreds.Item1.GetEnumerable()) {
                    count += deferred.Count;
                }
            }

            lock (deferreds.Item2) {
                foreach (var deferred in deferreds.Item2.GetEnumerable()) {
                    count += deferred.Count;
                }
            }

            return count;
        }
    }

    internal void Dispatch(TimeSpan remainingTime) {
        try {
            DispatchInternal(remainingTime);
        }
        catch (OperationCanceledException) { /* do nothing */ }
    }

    [MethodImpl(Runtime.MethodImpl.Inline)]
    private void InvokeTask(Task task) {
        if (TryExecuteTask(task) || task.IsCompleted) {
            task.Dispose();
        }
    }

    private static TextureActionKey MakeKey(TextureActionTask task) => new(task.ActionData.Reference, task.ActionData.Bounds);

    private static void AddToIndex(Dictionary<TextureActionKey, List<TextureActionTask>> index, TextureActionTask task) {
        var key = MakeKey(task);
        if (!index.TryGetValue(key, out var list)) {
            list = new List<TextureActionTask>();
            index.Add(key, list);
        }
        list.Add(task);
    }

    private static void RebuildIndex(PrioritizedList<TextureActionTask> source, Dictionary<TextureActionKey, List<TextureActionTask>> index) {
        index.Clear();
        foreach (var list in source.GetEnumerable()) {
            foreach (var task in list) {
                AddToIndex(index, task);
            }
        }
    }

    private void DispatchInternal(TimeSpan remainingTime) {
        var watch = Stopwatch.StartNew();
        {
            var pendingActions = PendingImmediate.Current;
            bool invoke;
            lock (pendingActions) {
                invoke = pendingActions.Count != 0;
            }

            if (invoke) {
                PendingImmediate.Swap();
                lock (pendingActions) {
                    foreach (var task in pendingActions) {
                        InvokeTask(task);
                    }
                    pendingActions.Clear();
                }
            }
        }

        PrioritizedList<TextureActionTask> pendingLoads;
        PrioritizedList<TextureActionTask> nextPendingLoads;
        Dictionary<TextureActionKey, List<TextureActionTask>> pendingLoadIndex;
        Dictionary<TextureActionKey, List<TextureActionTask>> nextPendingLoadIndex;

        lock (DeferredSwapLock) {
            (pendingLoads, nextPendingLoads) = PendingDeferred.Both;
            (pendingLoadIndex, nextPendingLoadIndex) = DeferredTaskIndex.Both;

            PendingDeferred.Swap();
            DeferredTaskIndex.Swap();
        }

        lock (pendingLoads) {
            if (!pendingLoads.Any) {
                return;
            }

            if (Config.AsyncScaling.ThrottledSynchronousLoads && !GameState.IsLoading) {
                Span<int> processed = stackalloc int[PriorityCount];
                for (int i = 0; i < processed.Length; ++i) {
                    processed[i] = 0;
                }
                bool processFirst = true; // always process the first.

                while (pendingLoads.TryPeek(in processed, out var task, out var priority)) {
                    if (!ProcessTask(task, watch, remainingTime, ref processFirst)) {
                        break;
                    }

                    ++processed[(int)priority];
                }

                // TODO : I'm not sure if this is necessary. The next frame, it'll probably come back around again to this buffer.
                for (int i = 0; i < processed.Length; ++i) {
                    if (processed[i] < pendingLoads[(Priority)i].Count) {
                        var priority = (Priority)i;
                        var start = processed[i];
                        nextPendingLoads.CopyFrom(pendingLoads, priority, start);

                        lock (nextPendingLoads) {
                            var list = pendingLoads[priority];
                            for (int j = start; j < list.Count; ++j) {
                                AddToIndex(nextPendingLoadIndex, list[j]);
                            }
                        }
                    }
                }
            }
            else {
                foreach (var list in pendingLoads.GetEnumerable()) {
                    foreach (var task in list) {
                        InvokeTask(task);
                    }
                }
            }
            pendingLoads.Clear();
            pendingLoadIndex.Clear();
        }
    }

    internal void FlushPendingTasks() {
        foreach (var pending in PendingImmediate.UnorderedBoth) {
            lock (pending) {
                foreach (var task in pending) {
                    InvokeTask(task);
                }

                pending.Clear();
            }
        }

        for (uint i = 0; i < 2U; ++i) {
            var pendings = PendingDeferred[i];
            var index = DeferredTaskIndex[i];
            lock (pendings) {
                foreach (var pending in pendings.GetEnumerable()) {
                    foreach (var task in pending) {
                        InvokeTask(task);
                    }

                    pending.Clear();
                }

                index.Clear();
            }
        }
    }

    private bool ProcessTask(TextureActionTask task, Stopwatch watch, TimeSpan remainingTime, ref bool processFirst) {
        var estimate = TexelAverage.Estimate(task.ActionData);
        if (!processFirst && DrawState.PushedUpdateWithin(0) && watch.Elapsed + estimate > remainingTime) {
            return false;
        }

        processFirst = false;

        DrawState.IsUpdatedThisFrame = true;
        var start = watch.Elapsed;
        InvokeTask(task);
        var duration = watch.Elapsed - start;
        Debug.Trace($"Sprite Finished: ['{task.ActionData.Name}'] Est: {estimate.TotalMilliseconds} ms, Act: {duration.TotalMilliseconds} ms  ({task.ActionData.Size.AsDataSize()}) (rem: {remainingTime.TotalMilliseconds} ms)");
        TexelAverage.Add(task.ActionData, duration);

        return true;
    }

    protected override void QueueTask(Task task) {
        if (task is TextureActionTask actionTask) {
            QueueTask(actionTask, actionTask.CurrentPriority);
        }
        else {
            QueueTask(task, Priority.Normal);
        }
    }

    internal void QueueTask(Task task, Priority priority) {
        if (DisposeCancellation.IsCancellationRequested) {
            ThrowHelper.ThrowObjectDisposedException(GetType().Name);
            return;
        }

        if (task is TextureActionTask actionTask) {
            lock (DeferredSwapLock) {
                var currentDeferred = PendingDeferred.Current;
                var currentIndex = DeferredTaskIndex.Current;

                lock (currentDeferred) {
                    var current = currentDeferred[priority];
                if (priority != Priority.Low) {
                    var lowerPriority = priority + 1;
                    var lowerList = currentDeferred[lowerPriority];
                    var key = MakeKey(actionTask);

                    if (currentIndex.TryGetValue(key, out var matchingTasks)) {
                        for (int i = 0; i < matchingTasks.Count; ++i) {
                            var item = matchingTasks[i];
                            if (item.CurrentPriority > priority) {
                                continue;
                            }

                            var other = currentDeferred[item.CurrentPriority];
                            if (!other.Remove(item)) {
                                RebuildIndex(currentDeferred, currentIndex);
                                continue;
                            }

                            item.CurrentPriority = lowerPriority;
                            lowerList.Add(item);
                        }
                    }
                }

                    current.Add(actionTask);
                    AddToIndex(currentIndex, actionTask);
                }
            }
        }
        else {
            var current = PendingImmediate.Current;
            lock (current) {
                current.Add(task);
            }
        }
    }

    internal bool RestorePriority(TextureActionTask task) {
        if (task.CurrentPriority == task.OriginalPriority) {
            return false;
        }

        var currentDeferred = PendingDeferred.Current;
        var current = currentDeferred[task.CurrentPriority];
        var original = currentDeferred[task.OriginalPriority];

        lock (currentDeferred) {
            if (current.Remove(task)) {
                original.Add(task);
                task.CurrentPriority = task.OriginalPriority;
                return true;
            }
        }

        return false;
    }

    internal bool Bump(TextureActionTask task) {
        return false;
    }

    [MethodImpl(Runtime.MethodImpl.Inline)]
    internal void QueueImmediate(Action action) {
        var task = new Task(action);
        task.Start(this);
    }

    [MethodImpl(Runtime.MethodImpl.Inline)]
    internal TextureActionTask? QueueDeferred(Action action, TextureAction actionData, Priority priority = Priority.Normal) {
        var task = new TextureActionTask(action, actionData, priority);
        task.Start(this);
        return task;
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => IsMainThread && TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() {
        var immediate = PendingImmediate.Both;
        var deferred = PendingDeferred.Both;

        List<Task> tasks;
        lock (immediate.Item1) lock (immediate.Item2) {
                tasks = new(immediate.Item1.Count + immediate.Item2.Count);
                tasks.AddRange(immediate.Item1);
                tasks.AddRange(immediate.Item2);
            }

        lock (deferred.Item1) {
            foreach (var list in deferred.Item1.GetEnumerable()) {
                tasks.AddRange(list);
            }
        }

        lock (deferred.Item2) {
            foreach (var list in deferred.Item2.GetEnumerable()) {
                tasks.AddRange(list);
            }
        }

        return tasks;
    }

    public void Dispose() => DisposeCancellation.Cancel();
}
