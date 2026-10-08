using GolemFactory.Compat;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A steam pipe: publishes its cell into the <see cref="SteamNetwork"/>, which re-derives
    /// every boiler's reach on the spot. Ported from Unity's sibling component (G2b) -- the
    /// logic half; the five pipe sprites are the Godot layer's, drawn from <see cref="Shape"/>
    /// and <see cref="ShapeOrientation"/>.
    ///
    /// <para>
    /// <b>A steam pipe has no facing.</b> The building's Facing (set with R) decides exactly
    /// one thing: the orientation of an ISOLATED stub, which is the one case the topology
    /// cannot answer and the one the player cares about, since the next pipe is laid off that
    /// open end. <see cref="PipeShapeRules"/> owns the answer.
    /// </para>
    /// </summary>
    public sealed class PlaceableSteamPipe : IBuildingPart
    {
        public const int IronPlateCost = SteamNetwork.SteamPipeIronPlateCost;

        public Vector2Int Cell { get; private set; }
        public bool IsRegistered { get; private set; }
        public PipeShape Shape { get; private set; } = PipeShape.End;
        public Facing ShapeOrientation { get; private set; } = Facing.East;

        public IBuildingPart CloneForInstance() => new PlaceableSteamPipe();

        public bool RegisterWithSteamNetwork(SteamNetwork network, Vector2Int cell)
        {
            if (network == null)
            {
                return false;
            }

            Cell = cell;
            IsRegistered = true;
            network.AddPipe(cell);
            return true;
        }

        public bool UnregisterFromSteamNetwork(SteamNetwork network)
        {
            if (network == null || !IsRegistered)
            {
                return false;
            }

            IsRegistered = false;
            return network.RemovePipe(Cell);
        }

        /// <summary>
        /// Re-derives <see cref="Shape"/> from which neighbours join this pipe -- another pipe or
        /// a boiler. <paramref name="buildingFacing"/> orients an isolated stub only.
        /// </summary>
        public void RefreshShape(SteamNetwork network, Facing buildingFacing)
        {
            if (network == null || !IsRegistered)
            {
                return;
            }

            PipeShapeRules.Resolve(
                Joins(network, Facing.North), Joins(network, Facing.East),
                Joins(network, Facing.South), Joins(network, Facing.West),
                buildingFacing,
                out PipeShape shape, out Facing orientation);
            Shape = shape;
            ShapeOrientation = orientation;
        }

        private bool Joins(SteamNetwork network, Facing side)
        {
            Vector2Int neighbour = FacingUtility.TargetCell(Cell, side);
            return network.HasPipe(neighbour) || network.HasBoilerAt(neighbour);
        }
    }
}
