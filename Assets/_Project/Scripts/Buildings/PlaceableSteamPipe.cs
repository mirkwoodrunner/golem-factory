using System.Collections.Generic;
using UnityEngine;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // The Steam Pipe (docs/progression-design.md §3.1). One cell, no flow direction, no
    // contents, no capacity -- it is a length of pipe. Sibling component alongside
    // PlaceableBuilding, same as PlaceableBoiler/PlaceableDepot/PlaceableBelt.
    //
    // Still far thinner than PlaceableBelt, and that is the §11-item-4 correction made physical:
    // a belt has a direction, a segment, a capacity and a link rule, while a pipe has a cell and
    // the shape of its own joints. Everything that makes a pipe USEFUL is the undirected flood
    // fill in Steam/SteamPipeRules, which never asks a pipe which way it points.
    //
    // WHAT THE PIECE-PICKING BELOW IS AND IS NOT. It chooses one of five sprites and a quarter
    // turn, from the same four-neighbour adjacency the flood fill walks. It changes no
    // reachability, publishes nothing, and is asked only when the built world changes. Before
    // it, every pipe drew the one east-west run, so a north-south column rendered as a stack of
    // disconnected rungs while being -- correctly -- one connected network. The picture
    // contradicted the simulation; this is the picture being made to agree with it.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableSteamPipe : MonoBehaviour
    {
        // §3.1's cost, RECORDED not charged -- see PlaceableBoiler for why (Iron Plate is a
        // §1.5 item, and §11 item 8 changes how costs are expressed at the same time).
        public const int IronPlateCost = SteamNetwork.SteamPipeIronPlateCost;

        // The five pieces PipeShapeRules picks between, each authored pointing EAST. Left null
        // on a prefab that has not been re-authored, in which case the renderer keeps whatever
        // sprite it already had and only the rotation applies -- so an un-updated scene degrades
        // to the single-run pipe it drew before rather than to an empty tile.
        [SerializeField] private Sprite endSprite;
        [SerializeField] private Sprite straightSprite;
        [SerializeField] private Sprite cornerSprite;
        [SerializeField] private Sprite teeSprite;
        [SerializeField] private Sprite crossSprite;

        // The renderer the piece is drawn on. Defaults to this object's own, which is where the
        // pipe sprite has always lived; a pipe has no children, so rotating that transform is
        // safe in a way it explicitly is NOT for a belt (see PlaceableBelt's lane renderer).
        [SerializeField] private SpriteRenderer pipeRenderer;

        /// <summary>The cell this pipe published, valid only while registered.</summary>
        public Vector2Int Cell { get; private set; }

        public bool IsRegistered { get; private set; }

        /// <summary>The piece currently drawn. Exposed so a test can read the picture.</summary>
        public PipeShape Shape { get; private set; } = PipeShape.End;

        /// <summary>The quarter turn currently applied, in grid terms.</summary>
        public Facing ShapeOrientation { get; private set; } = Facing.East;

        public bool RegisterWithSteamNetwork(SteamNetworkHolder holder, Vector2Int cell)
        {
            if (holder == null)
            {
                return false;
            }

            Cell = cell;
            IsRegistered = true;
            // AddPipe returns false on a cell that already has pipe; treat that as registered
            // anyway, since the network's state is what the caller asked for either way.
            holder.Network.AddPipe(cell);
            return true;
        }

        /// <summary>
        /// Pulls this pipe out of the network. Everything downstream of the gap loses steam
        /// immediately -- SteamNetwork re-derives reach wholesale rather than caching it, which
        /// is what makes progression-design §9's Phase 6 beat (a stage bar freezing because one
        /// extractor lost steam to a paved-over pipe) actually happen.
        /// </summary>
        public bool UnregisterFromSteamNetwork(SteamNetworkHolder holder)
        {
            if (holder == null || !IsRegistered)
            {
                return false;
            }

            IsRegistered = false;
            return holder.Network.RemovePipe(Cell);
        }

        // --- The picture ------------------------------------------------------------------

        /// <summary>
        /// Repoints this pipe's sprite and rotation at what its neighbours currently are.
        ///
        /// <para>
        /// Reads the NETWORK rather than the scene, for the same reason
        /// <c>BeltNetwork.Relink</c> recomputes wholesale: the network is the one place that
        /// knows what is really laid, and a pipe halfway through being demolished is still a
        /// GameObject for the rest of the frame.
        /// </para>
        /// </summary>
        public void RefreshShape(SteamNetworkHolder holder)
        {
            SpriteRenderer target = ResolveRenderer();
            if (target == null || holder == null || !IsRegistered)
            {
                return;
            }

            SteamNetwork network = holder.Network;
            PipeShape shape;
            Facing orientation;
            PipeShapeRules.Resolve(
                Joins(network, Facing.North), Joins(network, Facing.East),
                Joins(network, Facing.South), Joins(network, Facing.West),
                GetComponent<PlaceableBuilding>().Facing,
                out shape, out orientation);

            Shape = shape;
            ShapeOrientation = orientation;

            Sprite piece = SpriteFor(shape);
            if (piece != null)
            {
                target.sprite = piece;
            }

            target.transform.localRotation = Quaternion.Euler(
                0f, 0f, FacingVisuals.ScreenAngleDegrees(orientation));
        }

        // A pipe joins on to pipe AND to a boiler -- the boiler is the thing the run exists to
        // carry from, so a run that visibly stopped one cell short of it would read as broken
        // while being, in the flood fill's terms, perfectly connected.
        private bool Joins(SteamNetwork network, Facing side)
        {
            Vector2Int neighbour = FacingUtility.TargetCell(Cell, side);
            return network.HasPipe(neighbour) || network.HasBoilerAt(neighbour);
        }

        private Sprite SpriteFor(PipeShape shape)
        {
            switch (shape)
            {
                case PipeShape.Straight:
                    return straightSprite;
                case PipeShape.Corner:
                    return cornerSprite;
                case PipeShape.Tee:
                    return teeSprite;
                case PipeShape.Cross:
                    return crossSprite;
                default:
                    return endSprite;
            }
        }

        private SpriteRenderer ResolveRenderer()
        {
            if (pipeRenderer == null)
            {
                pipeRenderer = GetComponent<SpriteRenderer>();
            }

            return pipeRenderer;
        }

        /// <summary>
        /// Test/bootstrap wiring for the five pieces, matching the project's
        /// <c>Configure(...)</c> idiom rather than requiring Inspector-authored state.
        /// </summary>
        public void ConfigurePieces(
            SpriteRenderer renderer, Sprite end, Sprite straight, Sprite corner, Sprite tee,
            Sprite cross)
        {
            pipeRenderer = renderer;
            endSprite = end;
            straightSprite = straight;
            cornerSprite = corner;
            teeSprite = tee;
            crossSprite = cross;
        }

        // --- Keeping every pipe in the factory in agreement -------------------------------
        //
        // A pipe's picture depends on its NEIGHBOURS, so laying or lifting one cell changes up
        // to five tiles. Rather than have each pipe find its own neighbours' components (a
        // cell-to-component lookup this project deliberately does not have -- see the "golems
        // are not GridMap occupants" note), the placeables keep a live roster and the one caller
        // that knows the world just changed sweeps it.
        //
        // The sweep is O(pipes) on a click, never per tick, which is the same trade
        // BeltNetwork.Relink makes for exactly the same reason: a stale picture becomes
        // impossible by construction rather than by remembering to patch the right five cells.
        private static readonly List<PlaceableSteamPipe> Live = new List<PlaceableSteamPipe>();

        // OnEnable/OnDisable, so this roster cannot outlive the objects on it. There is no
        // [ExecuteAlways] here (as on almost everything in this project), so the roster is a
        // Play-mode thing -- which is fine, because the only thing that ever changes a pipe's
        // neighbours is a click, and clicks only happen in Play mode.
        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>Repoints every live pipe. A no-op with no network wired.</summary>
        public static void RefreshAllShapes(SteamNetworkHolder holder)
        {
            if (holder == null)
            {
                return;
            }

            for (int i = 0; i < Live.Count; i++)
            {
                if (Live[i] != null)
                {
                    Live[i].RefreshShape(holder);
                }
            }
        }
    }
}
