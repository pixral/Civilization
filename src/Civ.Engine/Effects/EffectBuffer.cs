namespace Civ.Engine.Effects;

/// <summary>
/// Collects one system's effects for one phase.
/// </summary>
/// <remarks>
/// Each system gets its own buffer rather than sharing one. Buffers are concatenated in pipeline
/// order at the barrier, so effect ordering is a property of the pipeline definition and not of
/// when a system happened to emit. That is what will make the read half of a phase safely
/// parallelisable later without changing a single result.
/// </remarks>
public sealed class EffectBuffer(string sourceName) : IEffectSink
{
    private readonly List<Effect> _effects = [];

    public string SourceName { get; } = sourceName;

    public IReadOnlyList<Effect> Effects => _effects;

    public int Count => _effects.Count;

    public void Emit(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.Source = SourceName;
        _effects.Add(effect);
    }

    public void Clear() => _effects.Clear();
}
