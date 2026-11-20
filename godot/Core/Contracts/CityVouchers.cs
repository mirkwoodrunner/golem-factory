using System;

namespace GolemFactory.Contracts
{
    /// <summary>
    /// The city's vouchers (2026-11-19, the user's call): what a contract and a golem order pay
    /// instead of goods. One buys the next step of any upgrade or unlock the player could buy with
    /// goods: a card, Floor Expansion's or the Docks' next rows, the pack's next rung, or a cart's
    /// next Rate or Capacity rung. A voucher never becomes goods: what it buys refunds nothing, so
    /// the city stays no cheaper a way to make goods than making them (contracts-design §4).
    /// </summary>
    public sealed class CityVouchers
    {
        /// <summary>Vouchers held.</summary>
        public int Count { get; private set; }

        /// <summary>Bumped on every change a reader might draw.</summary>
        public int Version { get; private set; }

        public bool Any => Count > 0;

        public void Give(int count)
        {
            if (count > 0)
            {
                Count += count;
                Version++;
            }
        }

        /// <summary>
        /// Spends one on <paramref name="purchase"/>, which makes the purchase for nothing and says
        /// whether it went through. Nothing is spent on a refusal.
        /// </summary>
        public bool TrySpend(Func<bool> purchase)
        {
            if (Count <= 0 || purchase == null || !purchase())
            {
                return false;
            }
            Count--;
            Version++;
            return true;
        }

        /// <summary>A load: the saved count. Never clamps.</summary>
        public void Restore(int count)
        {
            Count = count;
            Version++;
        }

        /// <summary>"1 voucher", "3 vouchers".</summary>
        public static string Describe(int count) => count + (count == 1 ? " voucher" : " vouchers");
    }
}
