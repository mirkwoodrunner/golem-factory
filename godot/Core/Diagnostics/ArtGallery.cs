using System;
using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Diagnostics
{
    /// <summary>One golem the gallery stands, and the frame its view is pinned to.</summary>
    public readonly struct GalleryGolem
    {
        public readonly GolemEntity Golem;
        public readonly GolemFrameKey Frame;

        public GalleryGolem(GolemEntity golem, GolemFrameKey frame)
        {
            Golem = golem;
            Frame = frame;
        }
    }

    /// <summary>
    /// The <c>art</c> scenario's gallery (docs/art-pipeline-design.md §7.1): every frame of a
    /// chassis in every facing, laid out in a grid on the street and built through the player's
    /// verbs (each chassis paid for, then built at the starter station, as
    /// <see cref="EndgameLoad"/> does), so the normal nodes draw it. Each golem's view is pinned to
    /// one frame, so every frame is shown without waiting for the moment it plays.
    ///
    /// <para>
    /// The grid: one column per frame state (base, idle_1, work_0..2, stall, carry), one row per
    /// facing (north, east, south, west), columns <see cref="ColumnStep"/> cells apart and rows
    /// <see cref="RowStep"/> apart, so a 64 × 96 body never overlaps its neighbour.
    /// </para>
    /// </summary>
    public static class ArtGallery
    {
        public const int ColumnStep = 2;
        public const int RowStep = 2;

        public static readonly Facing[] Facings = { Facing.North, Facing.East, Facing.South, Facing.West };

        /// <summary>The 7 distinct frames of a facing, in the gallery's column order.</summary>
        public static List<GolemFrameKey> FramesOf(Facing facing)
        {
            var frames = new List<GolemFrameKey>
            {
                GolemFrameKey.Base(facing),
                new GolemFrameKey(GolemFrameState.Idle, 1, facing),
            };
            for (int i = 0; i < GolemFrames.WorkFrames; i++)
            {
                frames.Add(new GolemFrameKey(GolemFrameState.Work, i, facing));
            }
            frames.Add(new GolemFrameKey(GolemFrameState.Stall, 0, facing));
            frames.Add(new GolemFrameKey(GolemFrameState.Carry, 0, facing));
            return frames;
        }

        /// <summary>Where a frame stands, from the grid's top-left cell.</summary>
        public static Vector2Int CellFor(Vector2Int topLeft, GolemFrameKey frame)
        {
            int column = FramesOf(frame.Facing).IndexOf(frame);
            int row = Array.IndexOf(Facings, frame.Facing);
            return new Vector2Int(topLeft.x + column * ColumnStep, topLeft.y - row * RowStep);
        }

        /// <summary>
        /// Builds one chassis's 28 golems, standing each on its frame's cell in that frame's facing.
        /// Unprogrammed: the gallery shows pictures, not work.
        /// </summary>
        public static List<GalleryGolem> Build(SandboxWorld world, string chassisName, Vector2Int topLeft)
        {
            ChassisDefinition chassis = world.Definitions.Chassis[chassisName];
            int wanted = Facings.Length * FramesOf(Facing.South).Count;
            while (world.AssemblyBay.MaxGolemSlots < wanted + world.AssemblyBay.OccupiedSlots)
            {
                EndgameLoad.Grant(world, world.AssemblyBay.UpgradeCost);
                if (!world.AssemblyBay.TryUpgrade(world.Buffers, world.StockpileBufferId, SandboxWorld.PostBufferId))
                {
                    throw new InvalidOperationException("ArtGallery: bay upgrade refused");
                }
            }

            var built = new List<GalleryGolem>();
            foreach (Facing facing in Facings)
            {
                foreach (GolemFrameKey frame in FramesOf(facing))
                {
                    EndgameLoad.Grant(world, chassis.cost);
                    if (!world.StarterStation.TryConstructGolem(chassis, out GolemEntity golem))
                    {
                        throw new InvalidOperationException($"ArtGallery: build {chassisName}: {world.StarterStation.LastRefusalReason}");
                    }
                    golem.SetPlacement(CellFor(topLeft, frame), facing);
                    built.Add(new GalleryGolem(golem, frame));
                }
            }
            return built;
        }
    }
}
