#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Editor entry points for the cup foundation's checks. The self-test is pure (no play mode
    /// needed); the nation validation touches JerseyDesigns and logs one warning per drifted row.
    /// </summary>
    public static class CupSelfTestMenu
    {
        [MenuItem("Trickshot/Cup/Run self-test")]
        public static void RunSelfTest()
        {
            try
            {
                Debug.Log(CupSelfTest.Run());
            }
            catch (CupSelfTestException e)
            {
                Debug.LogError("Cup self-test FAILED\n" + e.Message);
            }
            catch (Exception e)
            {
                Debug.LogError("Cup self-test crashed: " + e);
            }
        }

        [MenuItem("Trickshot/Cup/Validate nation table")]
        public static void ValidateNations()
        {
            CupNations.ClearCache();
            int missing = CupNations.Validate();
            Debug.Log("Cup nation table: " + CupNations.Count + " rows, " + missing + " unresolved, "
                + CupNations.ResolvedPool().Count + " in the AI pool");
        }
    }
}
#endif
