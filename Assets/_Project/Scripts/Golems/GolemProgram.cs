using System;
using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    public enum GolemState
    {
        Idle,
        Running,
        Stalled
    }

    [Serializable]
    public sealed class GolemProgram
    {
        public ChassisDefinition chassis;
        public LogicCoreDefinition logicCore;
        public List<AppendageActionDefinition> appendages = new List<AppendageActionDefinition>();

        // Per-slot Haul/Extract batch size, kept exactly parallel to `appendages`.
        //
        // It lives here and not on AppendageActionDefinition because that is a SHARED asset:
        // one HaulScrap.asset is referenced by every golem that slots the card, so writing a
        // player's chosen batch size onto it would retune every golem in the factory at once.
        // progression-design §2 "Consequence 4" makes batch size the Workbench's one remaining
        // real decision, so it has to be per-golem-per-slot state, which means savable program
        // state -- exactly what this class is.
        //
        // JsonUtility-friendly parallel lists rather than a List<struct>, matching SaveData's
        // BufferEntry idiom.
        public List<int> appendageQuantities = new List<int>();

        public int CurrentStepIndex { get; set; }
        public GolemState State { get; set; } = GolemState.Idle;

        // Ticks the current step has been processing for (M5: recipe-over-N-ticks
        // support, e.g. Refine). Zero means "not yet begun" -- GolemEntity.Tick uses that
        // to gate the once-only TryBeginStep call. Reset whenever the step changes.
        public int StepProgressTicks { get; set; }

        // M7 Threshold trigger: true means "ready to fire on the next at-or-above-
        // threshold check." Starts armed so an already-above-threshold buffer can still
        // fire once. Disarmed immediately after firing; re-armed only once the watched
        // quantity dips back below the threshold -- edge-triggered, not level-triggered,
        // so a continuously-full buffer doesn't refire every tick.
        public bool ThresholdArmed { get; set; } = true;

        // M7 Signal trigger: latched true by GolemEntity's GolemCompleted subscription
        // when the watched golem finishes a cycle, consumed (and reset) the next time
        // this golem is Idle and checks its trigger. Queues a signal that arrives while
        // this golem is busy rather than dropping it; multiple signals while busy coalesce
        // into one pending fire (not queued individually).
        public bool PendingSignal { get; set; }

        public AppendageActionDefinition CurrentStep =>
            CurrentStepIndex >= 0 && CurrentStepIndex < appendages.Count ? appendages[CurrentStepIndex] : null;

        public void AdvanceStep()
        {
            StepProgressTicks = 0;
            CurrentStepIndex++;
            if (CurrentStepIndex >= appendages.Count)
            {
                CurrentStepIndex = 0;
            }
        }

        // Chassis capacity is enforced only here, at assembly time (per the M3 design
        // note); execution never re-checks slot counts.
        public bool TryAssignChassis(ChassisDefinition newChassis)
        {
            if (newChassis == null || appendages.Count > newChassis.maxAppendageSlots)
            {
                return false;
            }

            chassis = newChassis;
            return true;
        }

        public bool TryAddAppendage(AppendageActionDefinition appendage)
        {
            if (appendage == null || chassis == null || appendages.Count >= chassis.maxAppendageSlots)
            {
                return false;
            }

            // §6: "The Overclocker alone may hold a Repeat(n) appendage." Refused at ASSEMBLY
            // time rather than stalled at run time, because this is not a precondition that can
            // come true later -- a Scavenger will never grow the ability -- and the rigid-stall
            // rule is for conditions the world can change.
            if (!CanHold(appendage))
            {
                return false;
            }

            SyncQuantities();
            appendages.Add(appendage);
            appendageQuantities.Add(ClampQuantity(appendage.haulQuantity));
            return true;
        }

        /// <summary>
        /// Whether <paramref name="appendage"/> is legal on this program's chassis at all.
        /// Public so the Workbench can grey a card out rather than letting the player drag it
        /// into a socket and discover on Engage Gears that it was never allowed.
        /// </summary>
        public bool CanHold(AppendageActionDefinition appendage)
        {
            if (appendage == null)
            {
                return false;
            }

            if (appendage.actionType == AppendageActionType.Repeat)
            {
                return chassis != null && chassis.allowsRepeat;
            }

            return true;
        }

        public void RemoveAppendageAt(int index)
        {
            if (index < 0 || index >= appendages.Count)
            {
                return;
            }

            SyncQuantities();
            appendages.RemoveAt(index);
            appendageQuantities.RemoveAt(index);
        }

        // --- Per-slot quantity ---------------------------------------------------------------

        /// <summary>
        /// The batch size for the appendage in <paramref name="index"/>, or 1 for a slot that
        /// doesn't exist. Never throws on a desynced parallel list -- see SyncQuantities.
        /// </summary>
        public int GetQuantityAt(int index)
        {
            SyncQuantities();
            if (index < 0 || index >= appendageQuantities.Count)
            {
                return 1;
            }

            return ClampQuantity(appendageQuantities[index]);
        }

        /// <summary>
        /// Sets the batch size for a slot, clamped to 1..GolemInventory.CapacityPerType. The
        /// upper clamp is not cosmetic: a Haul asking for more than the golem's per-type cap
        /// could never complete its own batch, so it would look like throughput tuning and
        /// behave like a permanent partial take.
        /// </summary>
        public void SetQuantityAt(int index, int quantity)
        {
            SyncQuantities();
            if (index < 0 || index >= appendageQuantities.Count)
            {
                return;
            }

            appendageQuantities[index] = ClampQuantity(quantity);
        }

        /// <summary>
        /// True if any slot holds an Assemble card. This is the test behind the pure-logistics
        /// rule (progression-design §2): a program with no Assemble treats its input stock as
        /// its output stock, so Haul -> Push cycles forever instead of filling input to the
        /// per-type cap and stalling. Only Assemble counts -- legacy Refine is id-routed
        /// buffer-to-buffer and never touches the golem's internal stocks at all.
        /// </summary>
        public bool HasAssembleStep
        {
            get
            {
                for (int i = 0; i < appendages.Count; i++)
                {
                    if (appendages[i] != null && appendages[i].actionType == AppendageActionType.Assemble)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // Self-heals the parallel list instead of throwing or asserting. It can legitimately
        // be the wrong length: a save written before quantities existed, a scene whose
        // GolemProgram was hand-edited in the Inspector, or one of the demo bootstraps that
        // appends straight to `appendages` past TryAddAppendage (HardcodedDemoProgram does
        // exactly that, on purpose). A desync there must not be able to break a golem's tick.
        private void SyncQuantities()
        {
            while (appendageQuantities.Count > appendages.Count)
            {
                appendageQuantities.RemoveAt(appendageQuantities.Count - 1);
            }

            while (appendageQuantities.Count < appendages.Count)
            {
                AppendageActionDefinition appendage = appendages[appendageQuantities.Count];
                appendageQuantities.Add(appendage != null ? ClampQuantity(appendage.haulQuantity) : 1);
            }
        }

        private static int ClampQuantity(int quantity)
        {
            if (quantity < 1)
            {
                return 1;
            }

            return quantity > GolemInventory.CapacityPerType ? GolemInventory.CapacityPerType : quantity;
        }
    }
}
