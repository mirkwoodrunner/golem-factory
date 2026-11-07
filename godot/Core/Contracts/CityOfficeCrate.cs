using GolemFactory.Buildings;
using GolemFactory.Economy;

namespace GolemFactory.Contracts
{
    /// <summary>
    /// The City Office's crate (docs/contracts-design.md §1): golems push into it, belts feed it,
    /// and the player puts goods in from the Crate screen, like any crate. Its buffer's room is the
    /// contract's (<see cref="ContractBoard.OfficeBuffer"/>), so it takes only what the taken
    /// contract still needs, and the board counts and spends what lands at once. No label: the
    /// contract is its label.
    /// </summary>
    public sealed class CityOfficeCrate : IIntakeCrate
    {
        private readonly ContractBoard _board;

        public CityOfficeCrate(ContractBoard board) => _board = board;

        public ContractBoard Board => _board;

        public string CrateId => ContractBoard.OfficeBufferId;

        public StorageBuffer Buffer => _board.OfficeBuffer;

        /// <summary>Its own, so the screens call it a crate, not a shared store. Never in a save's building list.</summary>
        public bool OwnsBuffer => true;

        public string FilterItemType => null;

        public bool CanLabel => false;

        public void SetFilter(string itemType)
        {
        }

        public string Title => ContractText.OfficeName;

        public string CapLine => ContractText.CrateLine(_board);

        public string RefusalFor(string itemType) => ContractText.CrateRefusal(_board, itemType);
    }
}
