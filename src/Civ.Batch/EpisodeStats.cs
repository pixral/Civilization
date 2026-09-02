namespace Civ.Batch;

/// <summary>
/// One empire, from the year it crossed the threshold to the year it fell back below it.
/// </summary>
/// <remarks>
/// <para>The aggregate statistics say how many large states there were and how long they lasted.
/// They cannot say whether the pattern this experiment is looking for actually happened, because
/// that pattern is a <i>sequence</i>: an exceptional administrator holds a remote periphery, the
/// realm reaches a size nothing else in the world reaches, an ordinary successor arrives, and the
/// periphery goes. Only individual histories can show or refute that, so they are kept.</para>
///
/// <para><see cref="AdminChangeAtSuccession"/> is null when no succession fell in the 25 years
/// before the episode ended - which is itself the answer to "was succession related to the
/// contraction" for that episode.</para>
/// </remarks>
internal sealed record EpisodeRecord(
    ulong Seed,
    string Polity,
    int StartYear,
    int PeakYear,
    int PeakShare,
    int Duration,
    int StartAdmin,
    int PeakAdmin,
    int PeakMilitary,
    bool EndedByExtinction,
    int? AdminChangeAtSuccession,
    int RegionsAtEnd,
    int RegionsAfterWindow)
{
    public int EndYear => StartYear + Duration;

    /// <summary>Territory lost in the 25 years after the episode ended. Positive means lost.</summary>
    public int LossAfterEnd => RegionsAtEnd - RegionsAfterWindow;

    public bool BeganUnderStrongAdministrator => StartAdmin >= RulerAnalysis.Exceptional;
}

/// <summary>
/// Empire episodes: continuous stretches in which one polity held at least a threshold world share.
/// </summary>
/// <remarks>
/// <para>This is the measurement the whole ruler programme is aiming at. Peak-share means and
/// distributions say how big states get; an episode says whether a <i>particular</i> state rose,
/// how long it stayed up, who was on the throne at its height, and how it came down.</para>
///
/// <para>Reported at 25% as the success threshold and at 20% as a diagnostic. The lower figure is
/// there because a run that produces no 25% episode at all still has a shape worth seeing - it is
/// not a redefinition of success.</para>
/// </remarks>
internal sealed record EpisodeStats(
    int Count,
    long TotalDuration,
    int MaxDuration,
    long TotalPeakShare,
    long TotalPeakAdmin,
    long TotalPeakMilitary,
    int EndedByExtinction,
    int EndedUnderWeakerAdministrator,
    int PeakedWithBothAbilitiesHigh)
{
    public static EpisodeStats Empty => new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public double MeanDuration => Count == 0 ? 0 : (double)TotalDuration / Count;

    public double MeanPeakShare => Count == 0 ? 0 : (double)TotalPeakShare / Count;

    public double MeanPeakAdmin => Count == 0 ? 0 : (double)TotalPeakAdmin / Count;

    public double MeanPeakMilitary => Count == 0 ? 0 : (double)TotalPeakMilitary / Count;

    public static EpisodeStats operator +(EpisodeStats a, EpisodeStats b) => new(
        a.Count + b.Count,
        a.TotalDuration + b.TotalDuration,
        Math.Max(a.MaxDuration, b.MaxDuration),
        a.TotalPeakShare + b.TotalPeakShare,
        a.TotalPeakAdmin + b.TotalPeakAdmin,
        a.TotalPeakMilitary + b.TotalPeakMilitary,
        a.EndedByExtinction + b.EndedByExtinction,
        a.EndedUnderWeakerAdministrator + b.EndedUnderWeakerAdministrator,
        a.PeakedWithBothAbilitiesHigh + b.PeakedWithBothAbilitiesHigh);

    internal sealed class Accumulator
    {
        private EpisodeStats _stats = Empty;

        public void Add(
            int duration,
            int peakShare,
            int peakAdmin,
            int peakMilitary,
            bool extinct,
            bool weakerAdministratorAtEnd)
        {
            _stats = _stats with
            {
                Count = _stats.Count + 1,
                TotalDuration = _stats.TotalDuration + duration,
                MaxDuration = Math.Max(_stats.MaxDuration, duration),
                TotalPeakShare = _stats.TotalPeakShare + peakShare,
                TotalPeakAdmin = _stats.TotalPeakAdmin + peakAdmin,
                TotalPeakMilitary = _stats.TotalPeakMilitary + peakMilitary,
                EndedByExtinction = _stats.EndedByExtinction + (extinct ? 1 : 0),
                EndedUnderWeakerAdministrator =
                    _stats.EndedUnderWeakerAdministrator + (weakerAdministratorAtEnd ? 1 : 0),
                PeakedWithBothAbilitiesHigh = _stats.PeakedWithBothAbilitiesHigh
                    + (peakAdmin >= RulerAnalysis.Exceptional && peakMilitary >= RulerAnalysis.Exceptional
                        ? 1
                        : 0),
            };
        }

        public EpisodeStats Snapshot() => _stats;
    }
}
