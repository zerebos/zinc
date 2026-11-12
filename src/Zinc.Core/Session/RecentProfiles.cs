namespace Zinc.Session;

/// <summary>
/// The profiles opened lately, newest first, as profile ids saved with the session. The jump list shows them as
/// "Recent profiles".
/// Restored and handed-over terminals don't count, only ones opened.
/// </summary>
public sealed class RecentProfiles
{
    public const int Capacity = 10;

    private readonly List<string> _ids = [];

    public RecentProfiles(IEnumerable<string>? saved = null) => _ids.AddRange((saved ?? []).Distinct().Take(Capacity));

    public IReadOnlyList<string> All => _ids;

    public void Note(string id)
    {
        if (_ids.Count > 0 && _ids[0] == id)
        {
            return;
        }

        _ids.Remove(id);
        _ids.Insert(0, id);
        if (_ids.Count > Capacity)
        {
            _ids.RemoveAt(_ids.Count - 1);
        }
    }
}
