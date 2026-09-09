#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace FreshWorld.Runtime
{
    /// <summary>
    /// Runs a coroutine and its nested enumerators on the calling thread. Only external yield
    /// values (including null) reach the caller; nested enumerators are advanced here so Unity
    /// cannot execute their code outside this error boundary.
    /// </summary>
    /// <remarks>
    /// Errors from MoveNext, Current, the continuation predicate, and Dispose are reported to
    /// onError. Cancellation or any such error disposes the active stack from child to parent.
    /// Every active enumerator is disposed once, and onFinished is attempted once after cleanup;
    /// its argument is true only after normal completion without an execution or cleanup error.
    /// Exceptions thrown by onFinished are reported to onError. Exceptions thrown by onError are
    /// swallowed, so notification failures never interrupt cleanup or escape to Unity.
    /// Dispose cancels an unfinished run and is idempotent. This type is not thread safe.
    /// </remarks>
    public sealed class GuardedCoroutine : IEnumerator, IDisposable
    {
        private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
        private readonly Func<bool> _canContinue;
        private readonly Action<Exception> _onError;
        private readonly Action<bool> _onFinished;
        private bool _finished;
        private object? _current;

        public GuardedCoroutine(
            IEnumerator root,
            Func<bool> canContinue,
            Action<Exception> onError,
            Action<bool> onFinished)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _canContinue = canContinue ?? throw new ArgumentNullException(nameof(canContinue));
            _onError = onError ?? throw new ArgumentNullException(nameof(onError));
            _onFinished = onFinished ?? throw new ArgumentNullException(nameof(onFinished));
            _stack.Push(root);
        }

        public object? Current => _current;

        public bool MoveNext()
        {
            if (_finished) return false;
            _current = null;

            try
            {
                while (_stack.Count > 0)
                {
                    if (!_canContinue())
                    {
                        Finish(false);
                        return false;
                    }

                    IEnumerator active = _stack.Peek();
                    if (!active.MoveNext())
                    {
                        // Remove first: even a throwing Dispose must never be attempted twice.
                        _stack.Pop();
                        (active as IDisposable)?.Dispose();
                        continue;
                    }

                    object? yielded = active.Current;
                    if (yielded is IEnumerator nested)
                    {
                        // A coroutine yielding itself (or an active parent) cannot make progress.
                        foreach (IEnumerator ancestor in _stack)
                        {
                            if (ReferenceEquals(ancestor, nested))
                                throw new InvalidOperationException("A coroutine yielded an already active enumerator.");
                        }
                        _stack.Push(nested);
                        continue;
                    }

                    _current = yielded;
                    return true;
                }

                Finish(true);
                return false;
            }
            catch (Exception error)
            {
                Report(error);
                Finish(false);
                return false;
            }
        }

        public void Reset() => throw new NotSupportedException("Coroutines cannot be reset.");

        public void Dispose() => Finish(false);

        private void Finish(bool success)
        {
            if (_finished) return;
            _finished = true;
            _current = null;

            while (_stack.Count > 0)
            {
                IEnumerator active = _stack.Pop();
                try
                {
                    (active as IDisposable)?.Dispose();
                }
                catch (Exception error)
                {
                    success = false;
                    Report(error);
                }
            }

            try
            {
                _onFinished(success);
            }
            catch (Exception error)
            {
                Report(error);
            }
        }

        private void Report(Exception error)
        {
            try
            {
                _onError(error);
            }
            catch (Exception)
            {
                // Logging/notification failures must not interrupt the remaining finally blocks.
            }
        }
    }
}
