using System.Collections;
using FreshWorld.Runtime;

var tests = new (string Name, Action Test)[]
{
    ("nested enumerators flatten into external yields", NormalYields),
    ("nested MoveNext failure unwinds child before parent", NestedMoveNextFailure),
    ("nested Current failure remains inside the error boundary", CurrentFailure),
    ("cancellation executes active iterator finally blocks once", Cancellation),
    ("continuation predicate failure terminates and cleans up", ContinuationFailure),
    ("all cleanup runs when multiple disposals throw", CleanupFailures),
    ("failure disposing a completed child stops its parent", CompletedChildDisposeFailure),
    ("throwing notification callbacks never interrupt cleanup", CallbackFailures),
    ("completion callback failure is reported without repeating completion", CompletionCallbackFailure),
    ("explicit disposal is idempotent", IdempotentDisposal),
    ("an active enumerator cycle fails and cleans up", ActiveCycle),
};

int failed = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {error}");
    }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} runtime tests passed.");
return failed == 0 ? 0 : 1;

static void NormalYields()
{
    object before = new(), middle = new(), after = new();
    var child = Probe.From(null, middle);
    var parent = Probe.From(before, child, after);
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    var actual = Drain(runner);
    Sequence(new object?[] { before, null, middle, after }, actual);
    Equal(1, child.Disposals);
    Equal(1, parent.Disposals);
    Equal(0, errors.Count);
    Sequence(new[] { true }, finishes);
    Equal(false, runner.MoveNext());
    Equal(null, runner.Current);
}

static void NestedMoveNextFailure()
{
    var failure = new InvalidOperationException("nested MoveNext");
    var disposalOrder = new List<string>();
    var child = new Probe(() => throw failure, () => null, () => disposalOrder.Add("child"));
    var parent = Probe.From(child);
    parent.OnDispose = () => disposalOrder.Add("parent");
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    Equal(false, runner.MoveNext());
    Sequence(new[] { failure }, errors);
    Sequence(new[] { "child", "parent" }, disposalOrder);
    Sequence(new[] { false }, finishes);
}

static void CurrentFailure()
{
    var failure = new InvalidOperationException("nested Current");
    var child = new Probe(() => true, () => throw failure);
    var parent = Probe.From(child);
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    Equal(false, runner.MoveNext());
    Sequence(new[] { failure }, errors);
    Sequence(new[] { false }, finishes);
    Equal(1, child.Disposals);
    Equal(1, parent.Disposals);
}

static void Cancellation()
{
    bool allowed = true;
    var cleanup = new List<string>();
    var finishes = new List<bool>();
    var errors = new List<Exception>();
    using var runner = new GuardedCoroutine(Parent(), () => allowed, errors.Add, finishes.Add);
    Equal(true, runner.MoveNext());
    Equal(null, runner.Current);
    allowed = false;
    Equal(false, runner.MoveNext());
    runner.Dispose();
    Equal(false, runner.MoveNext());
    Sequence(new[] { "child", "parent" }, cleanup);
    Sequence(new[] { false }, finishes);
    Equal(0, errors.Count);

    IEnumerator Parent()
    {
        try { yield return Child(); }
        finally { cleanup.Add("parent"); }
    }
    IEnumerator Child()
    {
        try { yield return null; throw new Exception("Canceled child must not advance again."); }
        finally { cleanup.Add("child"); }
    }
}

static void ContinuationFailure()
{
    var failure = new InvalidOperationException("canContinue");
    var parent = Probe.From(new object());
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => throw failure, errors.Add, finishes.Add);
    Equal(false, runner.MoveNext());
    Sequence(new[] { failure }, errors);
    Sequence(new[] { false }, finishes);
    Equal(1, parent.Disposals);
    Equal(0, parent.Moves);
}

static void CleanupFailures()
{
    var failure = new Exception("execution");
    var childDispose = new Exception("child disposal");
    var parentDispose = new Exception("parent disposal");
    var child = new Probe(() => throw failure, () => null, () => throw childDispose);
    var parent = Probe.From(child);
    parent.OnDispose = () => throw parentDispose;
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    Equal(false, runner.MoveNext());
    Sequence(new[] { failure, childDispose, parentDispose }, errors);
    Sequence(new[] { false }, finishes);
    Equal(1, child.Disposals);
    Equal(1, parent.Disposals);
}

static void CompletedChildDisposeFailure()
{
    var failure = new Exception("completed child disposal");
    var child = Probe.From();
    child.OnDispose = () => throw failure;
    var parent = Probe.From(child, "must not reach this yield");
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    Equal(false, runner.MoveNext());
    Sequence(new[] { failure }, errors);
    Sequence(new[] { false }, finishes);
    Equal(1, child.Disposals);
    Equal(1, parent.Disposals);
    Equal(1, parent.Moves);
}

static void CallbackFailures()
{
    var failure = new Exception("execution");
    var childDispose = new Exception("child disposal");
    var finishFailure = new Exception("onFinished");
    var child = new Probe(() => throw failure, () => null, () => throw childDispose);
    var parent = Probe.From(child);
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true,
        error => { errors.Add(error); throw new Exception("onError"); },
        success => { finishes.Add(success); throw finishFailure; });
    Equal(false, runner.MoveNext());
    runner.Dispose();
    Sequence(new[] { failure, childDispose, finishFailure }, errors);
    Sequence(new[] { false }, finishes);
    Equal(1, child.Disposals);
    Equal(1, parent.Disposals);
}

static void CompletionCallbackFailure()
{
    var failure = new Exception("onFinished");
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(Probe.From(), () => true, errors.Add,
        success => { finishes.Add(success); throw failure; });
    Equal(false, runner.MoveNext());
    Equal(false, runner.MoveNext());
    runner.Dispose();
    Sequence(new[] { true }, finishes);
    Sequence(new[] { failure }, errors);
}

static void IdempotentDisposal()
{
    var child = Probe.From("one", "two");
    var parent = Probe.From(child);
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    Equal(true, runner.MoveNext());
    runner.Dispose();
    runner.Dispose();
    Equal(false, runner.MoveNext());
    Equal(null, runner.Current);
    Equal(1, child.Disposals);
    Equal(1, parent.Disposals);
    Sequence(new[] { false }, finishes);
    Equal(0, errors.Count);
}

static void ActiveCycle()
{
    Probe? parent = null;
    parent = new Probe(() => true, () => parent);
    var errors = new List<Exception>();
    var finishes = new List<bool>();
    using var runner = new GuardedCoroutine(parent, () => true, errors.Add, finishes.Add);
    Equal(false, runner.MoveNext());
    Equal(1, errors.Count);
    Equal(true, errors[0] is InvalidOperationException);
    Equal(1, parent.Disposals);
    Sequence(new[] { false }, finishes);
}

static List<object?> Drain(IEnumerator runner)
{
    var values = new List<object?>();
    while (runner.MoveNext())
    {
        if (runner.Current is IEnumerator) throw new Exception("Nested enumerator escaped to Unity.");
        values.Add(runner.Current);
        if (values.Count > 100) throw new Exception("Coroutine did not terminate.");
    }
    return values;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected [{expected}], got [{actual}].");
}

static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new Exception($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}

sealed class Probe : IEnumerator, IDisposable
{
    private readonly Func<bool> _move;
    private readonly Func<object?> _current;
    public Action? OnDispose { get; set; }
    public int Moves { get; private set; }
    public int Disposals { get; private set; }

    public Probe(Func<bool> move, Func<object?> current, Action? dispose = null)
    {
        _move = move;
        _current = current;
        OnDispose = dispose;
    }
    public object? Current => _current();
    public bool MoveNext() { Moves++; return _move(); }
    public void Reset() => throw new NotSupportedException();
    public void Dispose() { Disposals++; OnDispose?.Invoke(); }

    public static Probe From(params object?[] values)
    {
        int index = -1;
        return new Probe(() => ++index < values.Length, () => values[index]);
    }
}
