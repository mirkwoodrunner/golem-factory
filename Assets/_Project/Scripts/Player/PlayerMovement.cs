using UnityEngine;

namespace GolemFactory.Player
{
    // Pure math extracted from PlayerController so it's unit-testable without a scene,
    // mirroring World/GridCoordinateConverter.cs and World/YSortUtility.cs's split
    // between plain-C# math and the MonoBehaviour that applies it.
    public static class PlayerMovement
    {
        public static Vector3 ComputeDisplacement(Vector2 moveInput, float speed, float deltaTime)
        {
            Vector2 clamped = Vector2.ClampMagnitude(moveInput, 1f);
            return new Vector3(clamped.x, clamped.y, 0f) * (speed * deltaTime);
        }
    }
}
