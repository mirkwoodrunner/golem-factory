using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using TMPro;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode: Refresh() instantiates real GameObjects (rows + a Claim Button)
    // under a RectTransform, and the test drives the real Button.onClick, same idiom as
    // WorkbenchControllerTests' *ViaButton helpers.
    public class AssemblyLinePanelTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private (AssemblyLinePanel panel, AssemblyLineStateHolder line, StorageBufferRegistryHolder buffers, RectTransform content)
            Build()
        {
            _root = new GameObject("Root");
            var line = _root.AddComponent<AssemblyLineStateHolder>();
            var buffers = _root.AddComponent<StorageBufferRegistryHolder>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_root.transform);
            var panel = _root.AddComponent<AssemblyLinePanel>();
            panel.Configure(line, buffers, "ScrapBuffer");
            panel.ConfigureUI(content, null);

            // The line starts with no candidates seeded (production seeds it via
            // AssemblyLineDemoBootstrap) -- seed one so slot 0 has a real card to claim.
            var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
            card.baseCost = 10;
            card.minCost = 2;
            card.decayPerSecond = 0f;
            line.State.SeedCandidates(new[] { card });

            return (panel, line, buffers, content);
        }

        [UnityTest]
        public IEnumerator Refresh_BuildsOneRowPerSlot_PlusTheWalletHeader()
        {
            (AssemblyLinePanel panel, AssemblyLineStateHolder line, _, RectTransform content) = Build();
            yield return null;

            panel.Refresh();

            // Row 0 is the wallet balance -- the cost columns below are meaningless without
            // it, and the player should not have to switch tabs to find out what they can
            // afford.
            Assert.AreEqual(line.State.SlotCount + 1, content.childCount);
        }

        [UnityTest]
        public IEnumerator Refresh_UnaffordableSlot_DisablesItsClaimButton()
        {
            (AssemblyLinePanel panel, _, StorageBufferRegistryHolder buffers, RectTransform content) = Build();
            yield return null;

            panel.Refresh();
            Button broke = FindClaimButton(content);
            Assume.That(broke, Is.Not.Null);
            Assert.IsFalse(broke.interactable, "An unaffordable card should say so before the click, not after");

            buffers.Registry.Deposit("ScrapBuffer", ItemType.Scrap, 1000);
            panel.Refresh();
            yield return null;

            Assert.IsTrue(FindClaimButton(content).interactable);
        }

        [UnityTest]
        public IEnumerator ClaimButton_InsufficientFunds_ShowsStatusMessage()
        {
            (AssemblyLinePanel panel, _, _, RectTransform content) = Build();
            var status = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            status.transform.SetParent(_root.transform);
            panel.ConfigureUI(content, status);
            yield return null;

            panel.Refresh();
            Button claimButton = FindClaimButton(content);
            Assume.That(claimButton, Is.Not.Null, "Fresh AssemblyLineState should have a card in slot 0 to claim.");
            claimButton.onClick.Invoke();

            Assert.AreEqual("Not enough Scrap to claim that card.", status.text);
        }

        [UnityTest]
        public IEnumerator ClaimButton_SufficientFunds_WithdrawsAndRefillsSlot()
        {
            (AssemblyLinePanel panel, AssemblyLineStateHolder line, StorageBufferRegistryHolder buffers, RectTransform content) = Build();
            var status = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            status.transform.SetParent(_root.transform);
            panel.ConfigureUI(content, status);
            yield return null;

            int cost = line.State.GetCurrentCost(0);
            for (int i = 0; i < cost; i++)
            {
                buffers.Registry.Deposit("ScrapBuffer", ItemType.Scrap);
            }

            panel.Refresh();
            Button claimButton = FindClaimButton(content);
            claimButton.onClick.Invoke();

            Assert.IsTrue(status.text.StartsWith("Claimed"));
        }

        private static Button FindClaimButton(RectTransform content)
        {
            foreach (Transform row in content)
            {
                Transform claim = row.Find("Claim");
                if (claim != null)
                {
                    return claim.GetComponent<Button>();
                }
            }
            return null;
        }
    }
}
