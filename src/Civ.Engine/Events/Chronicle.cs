namespace Civ.Engine.Events;

/// <summary>
/// The append-only record of everything that happened.
/// </summary>
/// <remarks>
/// A consumer of the simulation, never a participant. Nothing reads the chronicle to decide
/// anything; if a system needs to know that a war happened, that belongs in state, not here.
/// Keeping this strictly one-directional is what stops the history log from quietly becoming a
/// second, undisciplined copy of world state.
/// </remarks>
public sealed class Chronicle
{
    private readonly List<SimEvent> _events = [];

    public IReadOnlyList<SimEvent> Events => _events;

    public int Count => _events.Count;

    internal void Record(SimEvent simEvent) => _events.Add(simEvent);

    public IEnumerable<SimEvent> InYear(int year) =>
        _events.Where(e => e.Year == year);

    public IEnumerable<SimEvent> Between(int fromYearInclusive, int toYearInclusive) =>
        _events.Where(e => e.Year >= fromYearInclusive && e.Year <= toYearInclusive);

    public IEnumerable<SimEvent> AtLeast(Salience salience) =>
        _events.Where(e => e.Salience >= salience);

    /// <summary>Most recent entries first, optionally filtered by importance.</summary>
    public IEnumerable<SimEvent> Recent(int count, Salience minimum = Salience.Minor)
    {
        for (int i = _events.Count - 1; i >= 0 && count > 0; i--)
        {
            if (_events[i].Salience >= minimum)
            {
                count--;
                yield return _events[i];
            }
        }
    }
}
