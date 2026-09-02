namespace Civ.Batch;

/// <summary>Pass/fail summary of one experimental arm.</summary>
internal readonly record struct ArmOutcome(
    string Name,
    int InvariantViolations,
    int DeterminismMismatches,
    bool DeterminismChecked)
{
    public bool Failed => InvariantViolations > 0 || DeterminismMismatches > 0;

    /// <summary>
    /// Whether this arm may be described as deterministic.
    /// </summary>
    /// <remarks>
    /// False when nothing was rerun. An arm that was never checked is not a passing arm - it is an
    /// unmeasured one, and reporting it as reproduced was exactly the bug that made the earlier
    /// paired runs look verified when no comparison had happened at all.
    /// </remarks>
    public bool ReproducedExactly => DeterminismChecked && DeterminismMismatches == 0;

    public string Describe() =>
        !DeterminismChecked
            ? $"{Name}: {InvariantViolations} invariant violations, determinism NOT CHECKED"
            : $"{Name}: {InvariantViolations} invariant violations, "
                + $"{DeterminismMismatches} determinism mismatches";
}

/// <summary>
/// Turns arm outcomes into a process exit code.
/// </summary>
/// <remarks>
/// Separated from the reporting so it can be tested. The paired path previously returned success
/// unconditionally, which meant a run with invariant violations in either arm still exited 0 - the
/// batch runner is a regression gate, and a gate that always opens is worse than none.
/// </remarks>
internal static class BatchOutcome
{
    public const int Success = 0;

    public const int Failure = 1;

    public static int ExitCode(IEnumerable<ArmOutcome> arms) =>
        arms.Any(arm => arm.Failed) ? Failure : Success;
}
