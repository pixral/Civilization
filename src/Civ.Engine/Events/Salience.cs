namespace Civ.Engine.Events;

/// <summary>
/// How much an event deserves the reader's attention.
/// </summary>
/// <remarks>
/// Present from the first commit on purpose. A simulation of this kind produces far more state
/// changes than anyone will read; without a ranking baked into the event itself, the chronicle
/// degenerates into a scrolling wall in which nothing is distinguishable from anything else.
/// The UI filters on this; "advance until something interesting happens" is a threshold on it.
/// </remarks>
public enum Salience
{
    /// <summary>Bookkeeping. Retained for debugging, normally never shown.</summary>
    Trivial = 0,

    /// <summary>Local and routine.</summary>
    Minor = 1,

    /// <summary>Worth a line in a regional chronicle.</summary>
    Notable = 2,

    /// <summary>Changes something structural for a polity.</summary>
    Major = 3,

    /// <summary>Alters the shape of the world. Rare by construction.</summary>
    Historic = 4,
}
