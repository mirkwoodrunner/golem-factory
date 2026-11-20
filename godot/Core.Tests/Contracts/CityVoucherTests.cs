using System;
using System.IO;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Contracts;
using GolemFactory.Economy;
using GolemFactory.Supply;
using GolemFactory.Tests.EditMode;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Contracts
{
    /// <summary>
    /// City vouchers (2026-11-19): what contracts and golem orders pay, each the next step of any
    /// upgrade or unlock, spent on the Assembly Line tab's Voucher buttons or a cart's voucher rows.
    /// </summary>
    public class CityVoucherTests
    {
        private SandboxWorld _world;

        [SetUp]
        public void SetUp() => _world = CrateWorld.Compose();

        [TearDown]
        public void TearDown() => _world.Dispose();

        private AssemblyLineRow Row(AssemblyLineRowKind kind) => _world.AssemblyLineBoard.Rows().First(r => r.Kind == kind);

        private void EmptyThePack()
        {
            foreach (string good in _world.Wallet.Pack.Quantities.Keys.ToList())
            {
                _world.Wallet.Pack.TryWithdraw(good, _world.Wallet.Pack.GetQuantity(good));
            }
        }

        [Test]
        public void WithNoVouchers_NoRowOffersOne()
        {
            Assert.IsFalse(_world.AssemblyLineBoard.Rows().Any(r => r.VoucherButton));
            Assert.IsFalse(_world.AssemblyLineBoard.PayWithVoucher(Row(AssemblyLineRowKind.Floor)));
        }

        [Test]
        public void AVoucher_ClaimsACard_ForNoGoods()
        {
            _world.TechTree.Ledger.RecordItem(ItemType.Scrap);
            _world.Vouchers.Give(1);
            EmptyThePack();
            AssemblyLineRow row = _world.AssemblyLineBoard.Rows().First(r => r.Kind == AssemblyLineRowKind.Slot && r.Card.HasBundleCost);
            Assert.IsTrue(row.VoucherButton);
            Assert.IsFalse(row.Affordable, "precondition: no goods to pay with");

            Assert.IsTrue(_world.AssemblyLineBoard.PayWithVoucher(row), _world.AssemblyLineBoard.Status);

            Assert.IsTrue(_world.AssemblyLine.IsClaimed(row.Card));
            Assert.AreEqual(0, _world.Vouchers.Count, "one spent");
            Assert.IsFalse(_world.AssemblyLineBoard.Rows().Any(r => r.VoucherButton), "none left to offer");
        }

        [Test]
        public void AVoucher_ExtendsTheFloor_UpgradesThePack_ForNoGoods()
        {
            _world.Vouchers.Give(2);
            EmptyThePack();
            int north = _world.Bounds.NorthExtent;
            int cap = _world.Wallet.Pack.CapacityPerType;
            _world.TechTree.Ledger.RecordItem(ItemType.IronPlate);
            _world.TechTree.Ledger.RecordItem(ItemType.Gear);

            Assert.IsTrue(_world.AssemblyLineBoard.PayWithVoucher(Row(AssemblyLineRowKind.Floor)), _world.AssemblyLineBoard.Status);
            Assert.Greater(_world.Bounds.NorthExtent, north);
            Assert.IsTrue(_world.AssemblyLineBoard.PayWithVoucher(Row(AssemblyLineRowKind.Pack)), _world.AssemblyLineBoard.Status);
            Assert.Greater(_world.Wallet.Pack.CapacityPerType, cap);
            Assert.AreEqual(0, _world.Vouchers.Count);
        }

        [Test]
        public void ARowLockedByAnUnseenGood_OffersNoVoucher()
        {
            _world.Vouchers.Give(1);
            AssemblyLineRow pack = Row(AssemblyLineRowKind.Pack);
            Assume.That(pack.CostText, Does.StartWith("Requires"), "the pack's first rung names Gear, not yet made");
            Assert.IsFalse(pack.VoucherButton, "a voucher pays for a step, never skips the tree");
        }

        [Test]
        public void AVoucher_BuysACartsRung_WhichRefundsNothing()
        {
            CartSpot spot = _world.Supply.Spots.First(s => s.IsEmpty);
            Assert.IsTrue(_world.GrantCart(spot.Cell, ItemType.Coal).Done);
            Assert.IsFalse(_world.SupplyPanel.Rows(spot).Any(r => r.Action == SupplyAction.RateVoucher), "no voucher, no row");

            _world.Vouchers.Give(1);
            Assert.IsFalse(_world.SupplyPanel.Rows(spot).Any(r => r.Action == SupplyAction.RateVoucher),
                "its rung names goods not yet made: a voucher never skips the tree");
            _world.TechTree.Ledger.RecordItem(ItemType.IronPlate);
            _world.TechTree.Ledger.RecordItem(ItemType.Gear);
            SupplyRow rate = _world.SupplyPanel.Rows(spot).Single(r => r.Action == SupplyAction.RateVoucher);
            int paid = spot.Cart.Paid.Sum(p => p.quantity);

            Assert.IsTrue(_world.SupplyPanel.Press(spot, rate).Done);

            Assert.AreEqual(1, spot.Cart.RateRung);
            Assert.AreEqual(0, _world.Vouchers.Count);
            Assert.AreEqual(paid, spot.Cart.Paid.Sum(p => p.quantity), "a voucher's rung is paid nothing, so a removal refunds nothing of it");
        }

        [Test]
        public void ARefusedPurchase_SpendsNothing()
        {
            _world.Vouchers.Give(1);
            Assert.IsFalse(_world.Vouchers.TrySpend(() => false));
            Assert.AreEqual(1, _world.Vouchers.Count);
        }

        [Test]
        public void ASave_KeepsTheVouchers()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-vouchers-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                _world.Vouchers.Give(3);
                _world.SaveTo(path);
                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    Assert.AreEqual(3, loaded.Vouchers.Count);
                }
                finally
                {
                    loaded.Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
