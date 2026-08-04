namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// Shared counter that rows report their construction and teardown into.
/// </summary>
/// <remarks>
/// DESIGN.md §7.2 claims runtime sequence increment causes "サブツリー全体の破棄・再生成と状態消失".
/// Disposal is that teardown, so counting it measures the claim directly instead of inferring state
/// loss from rendered markup. One instance is passed to every row as a parameter rather than kept in
/// a static field, so tests running in parallel cannot see each other's counts.
/// </remarks>
public sealed class RowLifecycleLog
{
    /// <summary>Rows whose <c>OnInitialized</c> ran, meaning a fresh component instance was created.</summary>
    public int Initialized { get; set; }

    /// <summary>Rows the diff tore down, taking their state with them.</summary>
    public int Disposed { get; set; }

    /// <summary>Zeroes both counts, so a measurement can exclude the initial render.</summary>
    public void Reset()
    {
        Initialized = 0;
        Disposed = 0;
    }
}
