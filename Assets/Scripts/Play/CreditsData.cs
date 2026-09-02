namespace Trickshot
{
    /// <summary>
    /// The credits roll, as data. One table, so adding an asset is a one-line edit here and
    /// nothing in SettingsMenu's layout moves: every entry is one of a handful of kinds and the
    /// roll measures itself from the list (see SettingsMenu.DrawCredits).
    ///
    /// The asset lines are sourced from the licence files kept beside each asset
    /// (Resources/*/*-License.txt, Resources/Cosmetics/LICENSES.md). CC-BY entries MUST stay
    /// listed for as long as the asset ships; the CC0 packs are listed as a courtesy, which is
    /// the norm. Every CC-BY author in the cosmetics manifest is credited, grouped by author,
    /// whether or not their model was kept (the candidates were staged outside the project):
    /// an unused credit costs nothing, a missing one is a licence breach.
    /// </summary>
    public static class CreditsData
    {
        public enum Kind { Heading, Sub, Strong, Line, Gap }

        public readonly struct Entry
        {
            public readonly Kind Kind;
            public readonly string Text;
            public readonly float Size;   // Gap only: its height in virtual px
            public Entry(Kind kind, string text, float size = 0f) { Kind = kind; Text = text; Size = size; }
        }

        static Entry Heading(string t) => new Entry(Kind.Heading, t);
        static Entry Sub(string t)     => new Entry(Kind.Sub, t);
        static Entry Strong(string t)  => new Entry(Kind.Strong, t);
        static Entry Line(string t)    => new Entry(Kind.Line, t);
        static Entry Gap(float px)     => new Entry(Kind.Gap, null, px);

        // "what - who - licence", the one shape every asset line takes.
        static Entry Asset(string what, string who, string licence) =>
            Line(what + " \u2014 " + who + " \u2014 " + licence);

        public static readonly Entry[] Entries =
        {
            Gap(18f),
            Heading("TRICKSHOT"),
            Gap(26f),
            Sub("CREATED BY"),
            Strong("Roman Snegach"),
            Gap(34f),
            Sub("ASSETS"),
            Gap(4f),

            // Font, audio, props, hair, sky, turf (Resources/*/*-License.txt, PropKit).
            Asset("Barlow Condensed", "Jeremy Tribby", "SIL Open Font License 1.1"),
            Asset("Impact Sounds (crossbar hit)", "Kenney", "CC0"),
            Asset("Nature Kit, Racing Kit, City Kit, Car Kit", "Kenney", "CC0"),
            Asset("Prototype Kit, Mini Characters, Food Kit", "Kenney", "CC0"),
            Asset("KayKit Character Pack: Adventurers", "Kay Lousberg", "CC0"),
            Asset("Hair Alphas For Days", "OwlishMedia", "CC0"),
            Asset("Sky panoramas", "Poly Haven", "CC0"),
            Asset("Grass005 turf textures", "ambientCG", "CC0"),

            // Cosmetics (poly.pizza), CC0 packs.
            Asset("Lollypop", "Kenney", "CC0"),
            Asset("Necklaces", "Quaternius", "CC0"),
            Asset("Hat Stylised, Sombrero", "hat_my_guy", "CC0"),
            Asset("Glasses, Party Glasses, Ski Goggles", "iPoly3D", "CC0"),

            // Cosmetics (poly.pizza), CC BY 3.0: attribution required.
            Asset("Hat", "Cael Wood", "CC BY 3.0"),
            Asset("Security Hat", "Casey Tumbers", "CC BY 3.0"),
            Asset("Gas Mask", "Cody Stricker", "CC BY 3.0"),
            Asset("Aussie Style, Beanie, Cap, Cone, Cowboy Hat, Mask, Sunglasses, Top Hat, Witch Hat", "J-Toastie", "CC BY 3.0"),
            Asset("Glasses", "Jake Blakeley", "CC BY 3.0"),
            Asset("Baseball Cap, Diamond Stud Earrings, Hoop Earrings, Pearl Earrings, Top Hat", "Jarlan Perez", "CC BY 3.0"),
            Asset("Gas Mask", "Marisha", "CC BY 3.0"),
            Asset("Glasses", "Michael Fuchs", "CC BY 3.0"),
            Asset("Hats", "Minh Nguyen Tri", "CC BY 3.0"),
            Asset("Aviator Sunglasses, Baseball Cap, Cigar, Cowboy Hat, Fedora, Lollipop, Monocle, Mustache, Pipe, Saddle, Sombrero, Wizard Hat", "Poly by Google", "CC BY 3.0"),
            Asset("Time Hotel 5.10 Aviator Glasses", "S. Paul Michael", "CC BY 3.0"),
            Asset("Necklaces, Ring", "Zsky", "CC BY 3.0"),
            Asset("Glasses, Mustache, Sunglasses", "jeremy", "CC BY 3.0"),

            Gap(34f),
            Strong("Thank you for playing"),
            Gap(60f),
        };
    }
}
