namespace Civ.Engine.Effects;

/// <summary>The only channel a system has for changing anything.</summary>
public interface IEffectSink
{
    void Emit(Effect effect);
}
