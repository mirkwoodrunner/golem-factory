using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // OnGUI panel (same style as UI/GolemProgrammingPanel.cs -- this isn't drag-and-drop,
    // so no UGUI needed) that PlayerInteractor opens when the player interacts with a
    // GolemConstructionStation. Lists its chassis roster with cost; picking one spends the
    // resources and hands the new golem straight to the already-open Workbench.
    public sealed class GolemConstructionPanel : MonoBehaviour
    {
        private GolemConstructionStation _station;
        private string _statusMessage = "";

        public bool IsOpen { get; private set; }

        public void Open(GolemConstructionStation station)
        {
            _station = station;
            _statusMessage = "";
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        private void OnGUI()
        {
            if (!IsOpen || _station == null)
            {
                return;
            }

            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(Screen.width - 330, 10, 320, 400), GUI.skin.box);
            GUILayout.Label("Construct Golem", boldLabel);

            foreach (ChassisDefinition chassis in _station.ChassisRoster)
            {
                if (chassis == null)
                {
                    continue;
                }

                string label = $"{chassis.name} (Scrap {chassis.scrapCost}, Brass {chassis.brassCost}, {chassis.maxAppendageSlots} slots)";
                if (GUILayout.Button(label))
                {
                    _statusMessage = _station.TryConstructGolem(chassis, out GolemEntity _)
                        ? ""
                        : $"Not enough resources for {chassis.name}.";
                }
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Space(6);
                GUILayout.Label(_statusMessage);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Close"))
            {
                Close();
            }

            GUILayout.EndArea();
        }
    }
}
