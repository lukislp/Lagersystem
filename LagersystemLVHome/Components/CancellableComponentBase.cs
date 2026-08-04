using Microsoft.AspNetCore.Components;

namespace LagersystemLVHome.Components;

/// <summary>
/// Base class for Blazor components that need to honour the Blazor circuit
/// lifetime when calling <c>async</c> service methods.
///
/// <para>
/// Pages inheriting from this class can pass <see cref="ComponentCancellation"/>
/// to any service call that accepts a <see cref="CancellationToken"/>. When the
/// component is disposed (page navigation, circuit teardown, browser close), the
/// underlying <see cref="CancellationTokenSource"/> is cancelled, allowing
/// in-flight EF Core / HTTP calls to abort cooperatively instead of running to
/// completion against a dead circuit.
/// </para>
///
/// <para>Usage in a Razor page:</para>
/// <code>
/// @inherits CancellableComponentBase
///
/// @code {
///     private List&lt;Product&gt; _products = [];
///
///     protected override async Task OnInitializedAsync()
///     {
///         _products = await ProductService.GetAllAsync(ComponentCancellation);
///     }
/// }
/// </code>
/// </summary>
public abstract class CancellableComponentBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>
    /// Token that is cancelled when the component is disposed.
    /// Pass this to async service calls so they abort with the circuit.
    /// </summary>
    protected CancellationToken ComponentCancellation => _cts.Token;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            try { _cts.Cancel(); } catch { /* already disposed */ }
            _cts.Dispose();
        }
    }
}
