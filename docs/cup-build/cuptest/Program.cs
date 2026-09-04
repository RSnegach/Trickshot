using System;
using Trickshot;

namespace CupTest
{
    static class Program
    {
        static int Main()
        {
            try
            {
                Console.WriteLine(CupSelfTest.Run());
                // A sample draw for the eyes.
                var humans = new System.Collections.Generic.List<(int, int, string)>
                {
                    (CupNationTable.IndexOf("Brazil"), 1, "Alice"),
                    (CupNationTable.IndexOf("Jolly Roger"), 2, "Bob"),
                    (CupNationTable.IndexOf("Wales"), 5, "Cara"),
                };
                var b = CupBracket.Build(20260903u, CupFormat.Penalties, humans);
                Console.WriteLine("fresh draw: " + b.ToBytes().Length + " bytes");
                CupSim.SimulateRemaining(b, CupStage.RoundOf32, new SeededRng(b.Seed));
                Console.WriteLine("finished:   " + b.ToBytes().Length + " bytes");
                Console.WriteLine(b.Describe());
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return 1;
            }
        }
    }
}
