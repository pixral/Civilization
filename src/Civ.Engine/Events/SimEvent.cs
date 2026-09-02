namespace Civ.Engine.Events;

/// <summary>
/// One immutable entry in the world's history.
/// </summary>
/// <remarks>
/// <para><b>Events snapshot the names they need.</b> Subclasses carry both entity ids (for querying
/// and for later causality links) and the display strings as they stood at the moment of the event.
/// An event from year 400 has to still render correctly in year 1800, when the polity it names has
/// been dissolved for a millennium and its name reused. Resolving names lazily against current
/// state would quietly rewrite history.</para>
///
/// <para><b>Events are produced only by the applier</b>, from effects that actually changed state.
/// No system writes narrative. That is what makes the chronicle a report of the simulation rather
/// than a parallel story told alongside it.</para>
///
/// <para><see cref="Text"/> is a default rendering for convenience. The structured fields are the
/// source of truth, and a presentation layer is free to ignore it.</para>
/// </remarks>
public abstract record SimEvent(int Year, Salience Salience)
{
    /// <summary>Stable discriminator, used for filtering and for save formats.</summary>
    public abstract string Kind { get; }

    public abstract string Text { get; }

    public sealed override string ToString() => $"[{Year}] {Text}";
}
