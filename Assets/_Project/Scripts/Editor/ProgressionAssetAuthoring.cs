using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Editor
{
    // Authors every ScriptableObject docs/progression-design.md §5.2 and §6 specify: the 19
    // crafting recipes, one Assemble appendage card per recipe, the Push card §1.1 left
    // unauthored, and the five chassis cost bundles.
    //
    // IDEMPOTENT AND REPEATABLE, which is the whole point of it existing. §12 explicitly expects
    // a tuning pass ("treat every number in §7 and §9 as a target, not a fact", and the first
    // thing to try is Lens at 3 Glass), and the boiler fuel ratio is called out as "the number to
    // playtest first". So this is the tuning tool: edit the table below, re-run, and every .asset
    // is updated in place. It never creates a duplicate, never renumbers a GUID, and never
    // touches an asset that is already correct beyond re-serializing it.
    //
    // Run headless, with the Editor closed:
    //
    //   "<UnityPath>/Unity.exe" -batchmode -quit \
    //     -projectPath "<repo>" \
    //     -executeMethod GolemFactory.Editor.ProgressionAssetAuthoring.AuthorAll \
    //     -logFile "<somewhere>/author.log"
    //
    // ---------------------------------------------------------------------------------------
    // EVERY NUMBER HERE IS COPIED FROM §5.2's TABLE, NOT DERIVED. §5.2 has been independently
    // verified twice and §7's raw-cost table is built on it, so a number that "looks wrong"
    // here is a finding to report against the design, not a thing to quietly improve. The one
    // discrepancy found while authoring is recorded at RecipeCount below.
    // ---------------------------------------------------------------------------------------
    public static class ProgressionAssetAuthoring
    {
        private const string RecipeRoot = "Assets/_Project/ScriptableObjects/Recipes/";
        private const string AppendageRoot = "Assets/_Project/ScriptableObjects/Appendages/";
        private const string ChassisRoot = "Assets/_Project/ScriptableObjects/Chassis/";

        /// <summary>
        /// §5.2 lists R1..R19 -- NINETEEN crafting recipes, not the twenty its own prose claims
        /// ("20 crafting + 5 extraction = 25 recipes", repeated in §12's breadth line). The
        /// table's own ladder tally settles it: Presser 5 + Hauler 8 + Overclocker 4 + Zeppelin
        /// 2 = 19, and the rows are numbered R1 through R19 with no gaps and no duplicates.
        ///
        /// Authored as 19. Inventing a twentieth to match the prose would put a recipe in the
        /// game that no reviewer has ever costed, and §5.2's numbers are the ones §7's raw-cost
        /// table and §9's rates are derived from.
        /// </summary>
        public const int RecipeCount = 19;

        // One row of §5.2. Written as a literal table so a tuning pass edits data, not code.
        private readonly struct RecipeRow
        {
            public readonly string AssetName;
            public readonly string CardName;
            public readonly RecipeIngredient[] Inputs;
            public readonly string Output;
            public readonly int OutputQuantity;
            public readonly string Byproduct;
            public readonly int ByproductQuantity;
            public readonly int DurationTicks;

            public RecipeRow(
                string assetName, string cardName, RecipeIngredient[] inputs,
                string output, int outputQuantity, int durationTicks,
                string byproduct = null, int byproductQuantity = 0)
            {
                AssetName = assetName;
                CardName = cardName;
                Inputs = inputs;
                Output = output;
                OutputQuantity = outputQuantity;
                Byproduct = byproduct;
                ByproductQuantity = byproductQuantity;
                DurationTicks = durationTicks;
            }
        }

        private static RecipeIngredient In(string itemType, int quantity) =>
            new RecipeIngredient(itemType, quantity);

        // §5.2, in the table's own order: the five Presser recipes, the eight Hauler recipes,
        // the four Overclocker recipes, then the two Zeppelin recipes. Assets are named for
        // their id so the recipe number the design argues about is the file you open.
        private static readonly RecipeRow[] Recipes =
        {
            // --- 1 input: Brass Presser (3 slots = 1 Haul + Assemble + Push) -----------------
            new RecipeRow("R1_Coking", "AssembleCoking",
                new[] { In(ItemType.Coal, 1) }, ItemType.Coke, 1, 12),
            new RecipeRow("R2_ScrapReclamation", "AssembleScrapReclamation",
                new[] { In(ItemType.Scrap, 1) }, ItemType.IronPlate, 1, 24),
            new RecipeRow("R3_Glassmaking", "AssembleGlassmaking",
                new[] { In(ItemType.Slag, 1) }, ItemType.Glass, 1, 20),
            new RecipeRow("R8_GearCutting", "AssembleGearCutting",
                new[] { In(ItemType.IronPlate, 2) }, ItemType.Gear, 1, 16),
            new RecipeRow("R19_WireDrawing", "AssembleWireDrawing",
                new[] { In(ItemType.CopperIngot, 1) }, ItemType.CopperWire, 3, 18),

            // --- 2 inputs: Aether-Hauler (4 slots) -------------------------------------------
            // R4 IS THE ONLY RECIPE IN THE GAME WITH A BYPRODUCT (§5.2), and that 1 Slag is the
            // entire §5.3(c) disposal economy: with per-type buffer capacity a full Slag slot
            // stops the smelter depositing, so Slag must be routed every cycle.
            new RecipeRow("R4_IronSmelting", "AssembleIronSmelting",
                new[] { In(ItemType.Scrap, 2), In(ItemType.Coke, 1) }, ItemType.IronPlate, 2, 24,
                ItemType.Slag, 1),
            new RecipeRow("R5_CopperSmelting", "AssembleCopperSmelting",
                new[] { In(ItemType.CopperOre, 2), In(ItemType.Coke, 1) }, ItemType.CopperIngot, 1, 30),
            new RecipeRow("R6_ZincSmelting", "AssembleZincSmelting",
                new[] { In(ItemType.ZincOre, 2), In(ItemType.Coke, 1) }, ItemType.ZincIngot, 1, 30),
            new RecipeRow("R7_BrassAlloying", "AssembleBrassAlloying",
                new[] { In(ItemType.CopperIngot, 2), In(ItemType.ZincIngot, 1) }, ItemType.Brass, 3, 30),
            new RecipeRow("R9_CasingPress", "AssembleCasingPress",
                new[] { In(ItemType.IronPlate, 4), In(ItemType.Brass, 1) }, ItemType.Casing, 1, 28),
            new RecipeRow("R10_LensGrinding", "AssembleLensGrinding",
                new[] { In(ItemType.Glass, 2), In(ItemType.Brass, 1) }, ItemType.Lens, 1, 24),
            new RecipeRow("R11_MainspringWinding", "AssembleMainspringWinding",
                new[] { In(ItemType.Brass, 3), In(ItemType.Gear, 2) }, ItemType.Mainspring, 1, 40),
            new RecipeRow("R12_AetherContainment", "AssembleAetherContainment",
                new[] { In(ItemType.Aether, 1), In(ItemType.Lens, 2) }, ItemType.AetherCell, 1, 36),

            // --- 3 inputs: Mainspring Overclocker (5 slots) ----------------------------------
            new RecipeRow("R13_MechanismAssembly", "AssembleMechanismAssembly",
                new[] { In(ItemType.Mainspring, 1), In(ItemType.Gear, 3), In(ItemType.Casing, 1) },
                ItemType.Mechanism, 1, 60),
            // 10 Casing is deliberate and load-bearing: §6 notes it is what makes Repeat on R15
            // impossible, because two iterations would need 20 Casing against the 12-per-type
            // input cap. Do not "round" it in a tuning pass without re-reading that argument.
            new RecipeRow("R15_FrameSection", "AssembleFrameSection",
                new[] { In(ItemType.Casing, 10), In(ItemType.IronPlate, 6), In(ItemType.Brass, 4) },
                ItemType.FrameSection, 1, 90),
            new RecipeRow("R16_GreatCog", "AssembleGreatCog",
                new[] { In(ItemType.Mechanism, 4), In(ItemType.Brass, 6), In(ItemType.Mainspring, 2) },
                ItemType.GreatCog, 1, 100),
            new RecipeRow("R18_AetherConduit", "AssembleAetherConduit",
                new[] { In(ItemType.AetherCell, 3), In(ItemType.Brass, 4), In(ItemType.CopperWire, 6) },
                ItemType.AetherConduit, 1, 80),

            // --- 4 inputs: Zeppelin Freight Loader (6 slots) ---------------------------------
            new RecipeRow("R14_Regulator", "AssembleRegulator",
                new[]
                {
                    In(ItemType.AetherCell, 1), In(ItemType.Brass, 2),
                    In(ItemType.Gear, 1), In(ItemType.CopperWire, 4)
                },
                ItemType.Regulator, 1, 56),
            new RecipeRow("R17_ChronometerCore", "AssembleChronometerCore",
                new[]
                {
                    In(ItemType.Regulator, 2), In(ItemType.Mechanism, 2),
                    In(ItemType.Lens, 2), In(ItemType.CopperWire, 2)
                },
                ItemType.ChronometerCore, 1, 120),
        };

        // §6's cost table, verbatim. Ordered exactly as §6 writes each cost, because
        // ConstructionCostPolicy prints and charges in authored order and the design's own
        // phrasing is what the player will read back.
        private static readonly (string AssetName, RecipeIngredient[] Cost)[] ChassisCosts =
        {
            ("ClockworkScavenger", new[] { In(ItemType.Scrap, 12) }),
            ("BrassPresser", new[]
            {
                In(ItemType.Scrap, 60), In(ItemType.IronPlate, 20), In(ItemType.Gear, 10)
            }),
            ("AetherHauler", new[]
            {
                In(ItemType.IronPlate, 80), In(ItemType.Gear, 40), In(ItemType.Coke, 30)
            }),
            ("MainspringOverclocker", new[]
            {
                In(ItemType.Mainspring, 2), In(ItemType.Brass, 20),
                In(ItemType.Casing, 24), In(ItemType.Gear, 12)
            }),
            ("ZeppelinFreightLoader", new[]
            {
                In(ItemType.Mainspring, 6), In(ItemType.Lens, 8), In(ItemType.AetherCell, 3),
                In(ItemType.Casing, 30), In(ItemType.Brass, 40)
            }),
        };

        [MenuItem("Tools/Golem Factory/Author Progression Assets")]
        public static void AuthorAll()
        {
            int recipes = AuthorRecipes();
            int cards = AuthorAssembleCards();
            cards += AuthorPushCard();
            cards += RepurposeLegacyRefineCard();
            int chassis = AuthorChassisCosts();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"ProgressionAssetAuthoring: {recipes} recipes, {cards} appendage cards, " +
                      $"{chassis} chassis cost bundles written to disk.");
        }

        private static int AuthorRecipes()
        {
            EnsureFolder(RecipeRoot);

            int count = 0;
            foreach (RecipeRow row in Recipes)
            {
                RecipeDefinition recipe = LoadOrCreate<RecipeDefinition>(RecipeRoot + row.AssetName + ".asset");

                // Rebuilt rather than mutated in place: a re-run after an input type was removed
                // from the table must not leave the old entry behind. "Always re-render from
                // data", exactly as WorkbenchController.RebuildUI and BeltNetwork.Relink do.
                recipe.inputs = new List<RecipeIngredient>(row.Inputs);
                recipe.outputItemType = row.Output;
                recipe.outputQuantity = row.OutputQuantity;
                recipe.byproductItemType = row.Byproduct;
                recipe.byproductQuantity = row.Byproduct == null ? 1 : row.ByproductQuantity;
                recipe.durationTicks = row.DurationTicks;

                // Fails the authoring run, not the player's game. RecipeDefinition.IsWellFormed
                // is deliberately an authoring-edge check rather than a throw inside Tick, so
                // this is the edge it belongs at.
                string problem;
                if (!recipe.IsWellFormed(out problem))
                {
                    Debug.LogError($"ProgressionAssetAuthoring: {row.AssetName} is malformed -- {problem}");
                }

                EditorUtility.SetDirty(recipe);
                count++;
            }

            return count;
        }

        private static int AuthorAssembleCards()
        {
            EnsureFolder(AppendageRoot);

            int count = 0;
            foreach (RecipeRow row in Recipes)
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(
                    RecipeRoot + row.AssetName + ".asset");
                if (recipe == null)
                {
                    Debug.LogError($"ProgressionAssetAuthoring: recipe {row.AssetName} missing for card {row.CardName}");
                    continue;
                }

                AppendageActionDefinition card =
                    LoadOrCreate<AppendageActionDefinition>(AppendageRoot + row.CardName + ".asset");

                card.actionType = AppendageActionType.Assemble;
                card.recipe = recipe;

                // Assemble reads ONLY the recipe (see AppendageActionDefinition). The three
                // fields below exist for Refine and Haul and are cleared here so an Assemble
                // card cannot look, in the Inspector, as if it routes through a buffer.
                card.sourceId = null;
                card.destinationId = null;
                card.inputItemType = null;
                card.outputItemType = null;

                // Assemble takes its recipe's durationTicks, so the authored one is unread.
                // Mirrored anyway so the Inspector does not show a card claiming 1 tick for a
                // 120-tick Chronometer Core.
                card.durationTicks = row.DurationTicks;

                EditorUtility.SetDirty(card);
                count++;
            }

            return count;
        }

        /// <summary>
        /// The Push card §1.1 deliberately left unauthored (the roster cards were held back
        /// until §1.3 had replaced Assemble's data model with a RecipeDefinition, so authoring
        /// them earlier would have been throwaway work).
        ///
        /// <para>
        /// No ids and no item type: Push empties the golem's ENTIRE output stock onto the tile
        /// in front, mixed types and all (§2). That is what makes a byproduct free -- an iron
        /// smelter emitting Iron Plate and Slag still needs exactly one Push -- so a typed or
        /// routed Push would be a different, worse verb.
        /// </para>
        ///
        /// <para>
        /// durationTicks is left at 1 and is never read: Push derives its duration as
        /// <c>2 + unitCount</c> (§2).
        /// </para>
        /// </summary>
        private static int AuthorPushCard()
        {
            EnsureFolder(AppendageRoot);

            AppendageActionDefinition push =
                LoadOrCreate<AppendageActionDefinition>(AppendageRoot + "PushOutput.asset");

            push.actionType = AppendageActionType.Push;
            push.recipe = null;
            push.sourceId = null;
            push.destinationId = null;
            push.inputItemType = null;
            push.outputItemType = null;
            push.durationTicks = 1;

            EditorUtility.SetDirty(push);
            return 1;
        }

        /// <summary>
        /// §11 item 6: "repurpose RefineBrass.asset to Scrap -> Iron Plate".
        ///
        /// <para>
        /// DONE, AND THE ASSET IS RENAMED TO RefineIronPlate -- but the reason is no longer the
        /// one §11 gave. §11 item 6 was written when Refine was the only crafting verb, so the
        /// repurposing was how Scrap -> Iron Plate would exist at all. R2 now provides that as a
        /// proper Assemble, on the machine model, spatially routed, on a chassis. What still
        /// applies is the OTHER half: §5.1 makes Brass manufactured (R7: 2 Copper Ingot + 1 Zinc
        /// Ingot -> 3 Brass) and never dug up, and a vault card turning Scrap straight into
        /// Brass would let the player skip the entire copper-and-zinc line -- two node types,
        /// two smelting recipes and the alloying recipe that is Brass's only source. That is a
        /// hole in the economy, not a leftover.
        /// </para>
        ///
        /// <para>
        /// REPURPOSED RATHER THAN DELETED, and renamed via AssetDatabase.RenameAsset so the GUID
        /// survives: the asset is referenced by three roster arrays it cannot see from here
        /// (Main.unity, Sandbox.unity and WorkbenchCanvas.prefab), and deleting it would leave
        /// three dangling references that only an Editor pass could clean up. Renaming keeps
        /// every one of them resolving, at a card that no longer lies about what it makes.
        /// </para>
        ///
        /// <para>
        /// Refine itself stays legacy. It is id-routed, explicitly exempt from spatial routing,
        /// and superseded by Assemble; nothing new is built on it and no new Refine card is
        /// authored. This one exists to keep three existing references honest.
        /// </para>
        /// </summary>
        private static int RepurposeLegacyRefineCard()
        {
            const string oldPath = AppendageRoot + "RefineBrass.asset";
            const string newPath = AppendageRoot + "RefineIronPlate.asset";

            string path = AssetDatabase.LoadAssetAtPath<AppendageActionDefinition>(newPath) != null
                ? newPath
                : oldPath;

            var card = AssetDatabase.LoadAssetAtPath<AppendageActionDefinition>(path);
            if (card == null)
            {
                // Not an error: a fresh checkout that never had the M3-era card needs no
                // repurposing, and authoring a brand-new legacy Refine card would be exactly
                // the "nothing new should be built on Refine" mistake.
                Debug.Log("ProgressionAssetAuthoring: no legacy Refine card to repurpose.");
                return 0;
            }

            card.actionType = AppendageActionType.Refine;
            card.inputItemType = ItemType.Scrap;
            card.outputItemType = ItemType.IronPlate;
            card.sourceId = "ScrapBuffer";
            card.destinationId = "IronPlateBuffer";
            card.recipe = null;
            EditorUtility.SetDirty(card);

            if (path == oldPath)
            {
                string error = AssetDatabase.RenameAsset(oldPath, "RefineIronPlate");
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError("ProgressionAssetAuthoring: could not rename RefineBrass -- " + error);
                }
            }

            return 1;
        }

        private static int AuthorChassisCosts()
        {
            int count = 0;
            foreach ((string assetName, RecipeIngredient[] cost) in ChassisCosts)
            {
                string path = ChassisRoot + assetName + ".asset";
                var chassis = AssetDatabase.LoadAssetAtPath<ChassisDefinition>(path);
                if (chassis == null)
                {
                    // Chassis assets are hand-authored content with sprites and slot counts
                    // wired in; creating a blank one here would silently produce a spriteless
                    // 2-slot chassis that looks like the real thing in the roster.
                    Debug.LogError("ProgressionAssetAuthoring: missing chassis asset " + path);
                    continue;
                }

                chassis.cost = new List<RecipeIngredient>(cost);

                // maxAppendageSlots is NOT touched. §1.1 already set the roster to 2/3/4/5/6 and
                // slot count is THE tier gate in this design -- a tuning script quietly
                // rewriting it would be rewriting which recipes each chassis can run.
                EditorUtility.SetDirty(chassis);
                count++;
            }

            return count;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureFolder(string folderWithTrailingSlash)
        {
            string folder = folderWithTrailingSlash.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int split = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder.Substring(0, split), folder.Substring(split + 1));
        }
    }
}
