using System;

namespace HireCrew
{
    /// <summary>Random first/last names for hire candidates and debug hires.</summary>
    public static class CrewNames
    {
        private static readonly string[] FirstNames =
        {
            "Alex", "Blake", "Casey", "Dana", "Ellis", "Finn", "Gray", "Harper",
            "Ira", "Jules", "Kai", "Lane", "Morgan", "Nico", "Owen", "Quinn",
            "Remy", "Sage", "Tate", "Val", "Wren", "Yael", "Zane", "Avery",
            "Cameron", "Drew", "Emery", "Frankie", "Hayden", "Jamie", "Kerry", "Logan",
            "Milan", "Noel", "Parker", "Reese", "Shannon", "Terry", "Vesper", "Winter"
        };

        private static readonly string[] LastNames =
        {
            "Adler", "Brooks", "Chen", "Drake", "Ellis", "Ford", "Garcia", "Hayes",
            "Ito", "Jansen", "Kovacs", "Lopez", "March", "Nguyen", "Ortega", "Patel",
            "Quinn", "Reyes", "Sato", "Torres", "Ueda", "Vega", "Walsh", "Xu",
            "Young", "Zimmerman", "Ashford", "Brennan", "Costa", "Delgado", "Everett", "Farrell"
        };

        public static void RollName(Random rng, out string first, out string last)
        {
            if (rng == null) rng = new Random();
            first = FirstNames[rng.Next(FirstNames.Length)];
            last = LastNames[rng.Next(LastNames.Length)];
        }

        public static string RollFullName(Random rng)
        {
            string first, last;
            RollName(rng, out first, out last);
            return first + " " + last;
        }
    }
}
