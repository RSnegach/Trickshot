using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The shared shape of a mode's SETUP screen, so single player and the multiplayer host stop
    /// drawing the same controls two different ways.
    ///
    /// The layout is the accuracy PRACTICE screen, which is the reference every setup screen
    /// follows:
    ///
    ///     [ stat card ]   [ TITLE                  Reset ]   [ placement map ]
    ///                     [ Goal:          (picture)      ]
    ///                     [ Goalkeeper:  None .. Insane   ]   <- or a plain Yes / No
    ///                     [ ...whatever the mode adds     ]
    ///
    /// The goal picture and the keeper row are the SAME `GoalEditor` in both, so a change to either
    /// lands on every screen at once. Anything a mode adds goes BELOW the keeper row - that is what
    /// makes the screens read as one family rather than as a set of one-offs.
    ///
    /// This is a layout helper, not a MonoBehaviour: the owning screen still holds its own state and
    /// decides what its rows mean. It only owns where things sit and how tall the panel is.
    /// </summary>
    public static class SetupPanel
    {
        public const float PanelW = 480f;
        public const float RowH = 52f;     // one slider/picker row
        public const float HeadH = 78f;    // title band
        public const float FootH = 18f;    // slack under the last row (Back/Start sit on the screen)
        public const float MapW = 300f, MapH = 300f, MapGap = 16f;

        /// <summary>Panel height for a screen with `rows` extra rows under the goal picture.</summary>
        public static float Height(int rows, bool goalPicture = true)
            => HeadH + rows * RowH + FootH + (goalPicture ? GoalEditor.ContentH + 8f : 0f);

        /// <summary>Top-left of the centred panel, for a panel of this height.</summary>
        public static Vector2 Origin(float panelH)
            => new Vector2(MenuScale.Width * 0.5f - PanelW * 0.5f,
                           MenuScale.Height * 0.5f - panelH * 0.5f);

        /// <summary>
        /// Scrim, plate, title and the hairline under it. Returns the y the first row starts at.
        /// `reset` is the optional Reset All action - pass null on a screen with nothing to reset,
        /// and no button is drawn.
        /// </summary>
        public static float Begin(float x, float y, float panelH, string title, System.Action reset)
        {
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.40f, PanelW + 520f);
            UITheme.Panel(new Rect(x, y, PanelW, panelH), UITheme.Blue);

            // One line, full width left of the Reset button: the longer mode names wrapped and
            // clipped in a narrower rect.
            var st = _titleSt ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UITheme.Ink }, wordWrap = false, clipping = TextClipping.Overflow
            };
            UITheme.Shadowed(new Rect(x + 30f, y + 14f, PanelW - 160f, 44f), title, st, UITheme.Ink, 0.75f, 2f);
            UITheme.Fill(new Rect(x + 30f, y + 52f, 54f, 2.5f), UITheme.Gold);
            UITheme.Divider(x + 30f, y + HeadH - 12f, PanelW - 60f);

            if (reset != null)
            {
                var small = _smallSt ??= new GUIStyle(GUI.skin.button) { fontSize = 13 };
                if (UITheme.Button(new Rect(x + PanelW - 130f, y + 20f, 110f, 30f), "Reset All", small))
                    reset();
            }
            return y + HeadH;
        }

        /// <summary>
        /// The goal picture with the keeper row under it, advancing `row` past both. `width`/`height`
        /// are metres and `keeperLevel` indexes SimConfig.AiLevelNames (0 = None / No). `locked`
        /// freezes the goal at regulation and `yesNo` reduces the keeper row to two buttons - what a
        /// scored CHALLENGE wants, where the goal must not vary and the keeper's strength is the
        /// mode's to set.
        /// </summary>
        public static void GoalRow(GoalEditor editor, float x, ref float row,
                                   ref float width, ref float height, ref int keeperLevel,
                                   bool locked = false, bool yesNo = false)
        {
            editor.Draw(new Rect(x + 30f, row + 4f, PanelW - 60f, GoalEditor.ContentH),
                        ref width, ref height, ref keeperLevel,
                        framed: false, locked: locked,
                        keeperRow: yesNo ? GoalEditor.KeeperRow.YesNo : GoalEditor.KeeperRow.Ladder);
            row += GoalEditor.ContentH + 8f;
        }

        /// <summary>The placement map, in its standard slot to the RIGHT of the panel.</summary>
        public static void Map(float x, float y, ref Vector3 ball, ref Vector3 wall,
                               ref int editing, ref bool random, string randomTip, bool showWall)
        {
            SetPieceMap.DrawSetupPanel(x + PanelW + MapGap, y, MapW, MapH,
                                       ref ball, ref wall, ref editing, ref random, randomTip,
                                       showWall: showWall);
        }

        static GUIStyle _titleSt, _smallSt;
    }
}
