namespace Civ.Engine.State;

/// <summary>
/// Coarse terrain classification. Deliberately small: terrain exists at this stage only to prove
/// that immutable world facts survive worldgen and serialization.
/// </summary>
public enum Terrain
{
    Plains = 0,
    Forest = 1,
    Hills = 2,
    Mountains = 3,
    Desert = 4,
    Coast = 5,
}
