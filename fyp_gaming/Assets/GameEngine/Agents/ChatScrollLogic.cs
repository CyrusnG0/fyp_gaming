using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Pure scroll math for the observer chat. Kept out of the Unity partial so the harness can test
    /// the clamp and follow rules without an editor.
    /// </summary>
    public static class ChatScrollLogic
    {
        public const float BottomTolerance = 0.5f;

        public static float MaxScroll(float contentHeight, float viewportHeight)
            => Math.Max(0f, contentHeight - viewportHeight);

        public static float ClampOffset(float offset, float maxScroll)
            => Math.Max(0f, Math.Min(offset, Math.Max(0f, maxScroll)));

        public static float OffsetAfterWheel(float offset, float wheelDelta, float step, float maxScroll)
            => ClampOffset(offset + wheelDelta * step, maxScroll);

        public static bool FollowsAfterScroll(float offset, float maxScroll)
            => offset >= Math.Max(0f, maxScroll) - BottomTolerance;
    }
}
