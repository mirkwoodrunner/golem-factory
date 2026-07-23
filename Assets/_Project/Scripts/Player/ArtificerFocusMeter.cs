using UnityEngine;

namespace GolemFactory.Player
{
    // A distinct resource from the passive SimulationClock -- gates *intellectual*
    // Artificer actions (reprogramming a golem via the Workbench, patenting a
    // blueprint) while raw building/placement stays free and instant. Per-player
    // (OwnerId) from the start per the multiplayer-compatible-seams convention, even
    // though v1 only ever has one LocalPlayer.
    public sealed class ArtificerFocusMeter
    {
        public string OwnerId { get; }
        public float MaxFocus { get; }
        public float RegenPerSecond { get; }
        public float CurrentFocus { get; private set; }

        public ArtificerFocusMeter(string ownerId, float maxFocus = 100f, float regenPerSecond = 5f)
        {
            OwnerId = ownerId;
            MaxFocus = maxFocus;
            RegenPerSecond = regenPerSecond;
            CurrentFocus = maxFocus;
        }

        public void Regen(float deltaTime)
        {
            CurrentFocus = Mathf.Min(MaxFocus, CurrentFocus + RegenPerSecond * deltaTime);
        }

        public bool TryConsume(float amount)
        {
            if (amount < 0f || CurrentFocus < amount)
            {
                return false;
            }

            CurrentFocus -= amount;
            return true;
        }

        // For rolling back a TryConsume when the action it paid for turned out to fail
        // for an unrelated reason (see WorkbenchController.EngageGears's defensive
        // chassis-rejection path). Not "spending in reverse" -- just correcting the
        // ledger, so it bypasses TryConsume's own non-negative-amount guard.
        public void Refund(float amount)
        {
            CurrentFocus = Mathf.Min(MaxFocus, CurrentFocus + Mathf.Max(0f, amount));
        }
    }
}
