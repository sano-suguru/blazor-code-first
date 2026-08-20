namespace BlazorCodeFirst.Samples.Guestbook;

/// <summary>
/// A singleton, in-memory backing store. No database: the sample's own domain stays minimal so the
/// thing actually under test — <c>.FormName()</c> and <c>.RenderMode()</c> in a hosted app — is what
/// the code is exercising, the same choice HeighwayDragon made for its own domain logic.
/// </summary>
public sealed class GuestbookStore
{
    private readonly Lock _gate = new();
    private readonly List<GuestbookEntry> _entries = [];
    private int _nextId = 1;

    public GuestbookStore() => Add("Ada Lovelace", "First entry, seeded on startup.");

    public IReadOnlyList<GuestbookEntry> All()
    {
        lock (_gate)
            return [.. _entries.OrderByDescending(e => e.CreatedAt)];
    }

    public void Add(string name, string message)
    {
        lock (_gate)
            _entries.Add(new GuestbookEntry(_nextId++, name, message, DateTimeOffset.UtcNow));
    }

    public void Delete(int id)
    {
        lock (_gate)
            _entries.RemoveAll(e => e.Id == id);
    }

    public IReadOnlyList<GuestbookEntry> Search(string query)
    {
        lock (_gate)
        {
            var matches = string.IsNullOrWhiteSpace(query)
                ? _entries
                : _entries.Where(e =>
                    e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Message.Contains(query, StringComparison.OrdinalIgnoreCase));
            return [.. matches.OrderByDescending(e => e.CreatedAt)];
        }
    }
}
