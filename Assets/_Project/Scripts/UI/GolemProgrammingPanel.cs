using UnityEngine;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // Minimal list-based programming UI for M3: pick a chassis, a logic core, and
    // add/remove appendages from fixed rosters assigned in the Inspector. The full
    // Workbench/Card Vault drag-and-drop visual treatment lands in M8.
    public sealed class GolemProgrammingPanel : MonoBehaviour
    {
        [SerializeField] private GolemEntity targetGolem;
        [SerializeField] private ChassisDefinition[] availableChassis = new ChassisDefinition[0];
        [SerializeField] private LogicCoreDefinition[] availableLogicCores = new LogicCoreDefinition[0];
        [SerializeField] private AppendageActionDefinition[] availableAppendages = new AppendageActionDefinition[0];

        private Vector2 _scroll;
        private string _statusMessage = "";

        private void OnGUI()
        {
            if (targetGolem == null)
            {
                return;
            }

            GolemProgram program = targetGolem.Program;
            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(10, 10, 320, Screen.height - 20), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label($"Programming: {targetGolem.GolemId}", boldLabel);

            GUILayout.Space(6);
            GUILayout.Label("Chassis", boldLabel);
            foreach (ChassisDefinition candidate in availableChassis)
            {
                bool isCurrent = program.chassis == candidate;
                string label = $"{candidate.name} (slots {candidate.maxAppendageSlots}, tier {candidate.tier})";
                if (GUILayout.Toggle(isCurrent, label, GUI.skin.button) && !isCurrent)
                {
                    _statusMessage = program.TryAssignChassis(candidate)
                        ? ""
                        : "Cannot switch chassis: remove appendages to fit its slot count first.";
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("Logic Core", boldLabel);
            foreach (LogicCoreDefinition candidate in availableLogicCores)
            {
                bool isCurrent = program.logicCore == candidate;
                if (GUILayout.Toggle(isCurrent, candidate.name, GUI.skin.button) && !isCurrent)
                {
                    program.logicCore = candidate;
                }
            }

            GUILayout.Space(6);
            int slots = program.chassis != null ? program.chassis.maxAppendageSlots : 0;
            GUILayout.Label($"Appendages ({program.appendages.Count}/{slots})", boldLabel);
            int removeIndex = -1;
            for (int i = 0; i < program.appendages.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i + 1}. {program.appendages[i].name}");
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    removeIndex = i;
                }
                GUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                program.RemoveAppendageAt(removeIndex);
            }

            GUILayout.Space(4);
            GUILayout.Label("Add appendage:");
            foreach (AppendageActionDefinition candidate in availableAppendages)
            {
                if (GUILayout.Button($"+ {candidate.name}"))
                {
                    _statusMessage = program.TryAddAppendage(candidate)
                        ? ""
                        : "Cannot add appendage: chassis is at capacity.";
                }
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Space(6);
                GUILayout.Label(_statusMessage);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
