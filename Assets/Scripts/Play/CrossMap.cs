using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Reusable top-down penalty-box map for choosing where a cross lands. Draws the goal
    /// mouth along the top edge and the box out to a depth; a click drops a marker and maps
    /// it to a world target in front of the goal. Shared by the striker-mode in-match
    /// overlay and (optionally) the prematch aim map.
    /// </summary>
    public static class CrossMap
    {
        // Pitch band shown on the map: full goal width + margin across, this deep out.
        const float DepthShown = 18f;
        static float HalfShown => SimConfig.GoalWidth * 0.5f + 3f;

        // Draw the map filling `rect`. Reads/writes `target` (world). Returns true if the
        // marker was moved this frame. `interactive` gates click handling.
        public static bool Draw(Rect rect, ref Vector3 target, bool interactive)
        {
            var prev = GUI.color;
            // Grass field.
            GUI.color = new Color(0.16f, 0.4f, 0.18f, 0.96f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(rect, GUIContent.none);   // border

            // Goal bar along the top edge.
            float goalPxHalf = (SimConfig.GoalWidth * 0.5f / HalfShown) * (rect.width * 0.5f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(rect.center.x - goalPxHalf, rect.y + 2f, goalPxHalf * 2f, 5f), Texture2D.whiteTexture);
            // Six-yard-ish inner box (visual guide).
            float innerHalf = goalPxHalf * 1.6f, innerDepth = rect.height * 0.33f;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            DrawRectOutline(new Rect(rect.center.x - innerHalf, rect.y + 2f, innerHalf * 2f, innerDepth), 1.5f);
            GUI.color = prev;

            bool moved = false;
            Event e = Event.current;
            if (interactive && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                float fx = (e.mousePosition.x - rect.x) / rect.width;   // 0..1 left->right
                float fy = (e.mousePosition.y - rect.y) / rect.height;  // 0..1 top(goal)->out
                float wx = Mathf.Lerp(-HalfShown, HalfShown, fx);
                float wz = SimConfig.GoalCenter.z - Mathf.Lerp(0f, DepthShown, fy);
                target = new Vector3(wx, 0.25f, wz);
                moved = true;
                e.Use();
            }

            // Current marker.
            float mfx = Mathf.InverseLerp(-HalfShown, HalfShown, target.x);
            float mfy = Mathf.InverseLerp(0f, DepthShown, SimConfig.GoalCenter.z - target.z);
            GUI.color = new Color(1f, 0.85f, 0.2f);
            var m = new Rect(rect.x + mfx * rect.width - 6f, rect.y + mfy * rect.height - 6f, 12f, 12f);
            GUI.DrawTexture(m, Texture2D.whiteTexture);
            GUI.color = prev;
            return moved;
        }

        static void DrawRectOutline(Rect r, float t)
        {
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
        }
    }
}
