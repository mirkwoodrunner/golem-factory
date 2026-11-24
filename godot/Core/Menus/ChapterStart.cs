using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Tutorial;
using GolemFactory.World;

namespace GolemFactory.Menus
{
    /// <summary>
    /// A new game that starts at a later chapter, for testing (playtest mode's "Start at chapter"
    /// on the title, or <c>-- --chapter N</c> on the command line). It is built as the title's
    /// workshop is: the playtest kit performs every earlier chapter's steps with the player's
    /// verbs, on the world's own clock, as fast as the machine allows. So the game opens on the
    /// ground a player who did those chapters stands on, and the playtest report lists each
    /// chapter the kit did.
    /// </summary>
    public static class ChapterStart
    {
        /// <summary>The first chapter a start can name: chapter 1 is New game.</summary>
        public const int First = 2;

        /// <summary>One past the guide's last chapter: every chapter done, the guide finished.</summary>
        public static int Finished => TutorialGuide.LastChapter + 1;

        /// <summary>Frames of 1/30 s each chapter may take before the kit gives up on it.</summary>
        private const int FramesPerChapter = 3600;
        private const float FrameSeconds = 1f / 30f;

        /// <summary>A start inside the range a start can name: <see cref="First"/> to <see cref="Finished"/>.</summary>
        public static int Clamp(int chapter) => Math.Max(First, Math.Min(Finished, chapter));

        /// <summary>
        /// Plays <paramref name="chapters"/> of the guide's chapters through the playtest kit, from
        /// wherever the guide stands, quietly (no landing, dust, sound or popup: it all happens in
        /// one frame, as a load does). The world needs its guide and a playing clock. Returns the
        /// chapter the guide reached, <see cref="Finished"/> once it is done.
        /// </summary>
        public static int FastForward(SandboxWorld world, int chapters)
        {
            if (world?.Tutorial == null)
            {
                throw new InvalidOperationException("a chapter start is built by the guide's kit; this world has no guide");
            }
            world.Quietly(() =>
            {
                for (int chapter = 0; chapter < chapters; chapter++)
                {
                    if (!world.Tutorial.StartKit())
                    {
                        break;
                    }
                    for (int frame = 0; frame < FramesPerChapter && world.Tutorial.KitRunning; frame++)
                    {
                        world.Advance(FrameSeconds);
                    }
                }
                return 0;
            });
            return Reached(world);
        }

        /// <summary>
        /// Takes a fresh game to the start of chapter <paramref name="chapter"/>, and the player to
        /// the factory the kit built: the game's spawn is a dozen cells from it, where a player
        /// who built it would never be standing.
        /// </summary>
        public static int Stage(SandboxWorld world, int chapter)
        {
            int reached = FastForward(world, Clamp(chapter) - 1);
            if (BesideTheFactory(world) is Vector2Int cell)
            {
                world.Interactor.Teleport(new Vector3(cell.x, cell.y, 0f));
            }
            return reached;
        }

        /// <summary>
        /// The free ground just north of everything built (the street is to the south), nearest
        /// the middle; null with nothing built.
        /// </summary>
        public static Vector2Int? BesideTheFactory(SandboxWorld world)
        {
            var taken = new HashSet<Vector2Int>(world.Build.Buildings.Where(b => !b.IsRemoved).Select(b => b.Cell)
                .Concat(world.Golems.Where(g => g != null && !g.IsRemoved).Select(g => g.Cell)));
            if (taken.Count == 0)
            {
                return null;
            }
            int middle = (taken.Min(c => c.x) + taken.Max(c => c.x)) / 2;
            int north = taken.Max(c => c.y) + 1;
            for (int y = north; y >= north - 12; y--)
            {
                for (int dx = 0; dx <= 12; dx++)
                {
                    foreach (int x in new[] { middle - dx, middle + dx })
                    {
                        var cell = new Vector2Int(x, y);
                        if (!taken.Contains(cell) && world.Bounds.IsGround(cell))
                        {
                            return cell;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>The chapter the guide is on, <see cref="Finished"/> when it is done.</summary>
        public static int Reached(SandboxWorld world)
        {
            TutorialGuide guide = world.Tutorial;
            return guide.IsFinished || guide.Current?.Id == "done" ? Finished : guide.ChapterOf(guide.Index);
        }
    }
}
