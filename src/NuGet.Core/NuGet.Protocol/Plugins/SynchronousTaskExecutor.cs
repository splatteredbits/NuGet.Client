// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Protocol.Plugins
{
    public sealed class SynchronousTaskExecutor : IDisposable
    {
        private bool _isDisposed;
        private readonly BlockingCollection<WorkItem> _queue;

        private static Lazy<SynchronousTaskExecutor> _instance = new Lazy<SynchronousTaskExecutor>(Create);

        public static SynchronousTaskExecutor Instance
        {
            get
            {
                return _instance.Value;
            }
        }

        private SynchronousTaskExecutor()
        {
            _queue = new BlockingCollection<WorkItem>();
        }

        void IDisposable.Dispose()
        {
            if (!_isDisposed)
            {
                _queue.CompleteAdding();

                try
                {
                    _queue.Dispose();
                }
                catch (Exception)
                {
                }

                GC.SuppressFinalize(this);

                _isDisposed = true;
            }
        }

        public T Execute<T>(Func<Task<T>> taskFunc)
        {
            var workItem = new WorkItem<T>(taskFunc);

            _queue.Add(workItem);

            return workItem.TaskCompletionSource.Task.Result;
        }

        private void Run()
        {
            // Top-level exception handler.
            try
            {
                foreach (var workItem in _queue.GetConsumingEnumerable())
                {
                    workItem.Execute();
                }
            }
            catch (Exception)
            {
            }
        }

        private static SynchronousTaskExecutor Create()
        {
            var processor = new SynchronousTaskExecutor();

            Task.Factory.StartNew(
                processor.Run,
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);

            return processor;
        }

        private abstract class WorkItem
        {
            internal abstract void Execute();
        }

        private sealed class WorkItem<T> : WorkItem
        {
            internal Func<Task<T>> TaskFunc { get; }
            internal TaskCompletionSource<T> TaskCompletionSource { get; }

            internal WorkItem(Func<Task<T>> taskFunc)
            {
                TaskFunc = taskFunc;
                TaskCompletionSource = new TaskCompletionSource<T>();
            }

            internal override void Execute()
            {
                try
                {
                    var task = TaskFunc();

                    task.RunSynchronously();

                    TaskCompletionSource.TrySetResult(task.Result);
                }
                catch (Exception ex)
                {
                    TaskCompletionSource.TrySetException(ex);
                }
            }
        }
    }
}