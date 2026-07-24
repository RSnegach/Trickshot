namespace Trickshot
{
    /// <summary>
    /// Question bank for the (tongue-in-cheek) ADULT MODE knowledge gate. General "an adult should
    /// know this" trivia across finance, cooking, civics, geography, science, health, and everyday
    /// life. NOT a real gate - a wrong answer just serves another question - so the tone is light,
    /// but each Correct index is verified.
    ///
    /// Each Q holds the prompt, exactly four options, and the index (0-3) of the correct one.
    /// Served at random by CustomizeUI's adult-mode flow.
    /// </summary>
    public static class AdultQuiz
    {
        public struct Q
        {
            public string Text;
            public string[] A;   // exactly 4
            public int Correct;  // 0..3
            public Q(string t, string a0, string a1, string a2, string a3, int c)
            { Text = t; A = new[] { a0, a1, a2, a3 }; Correct = c; }
        }

        public static readonly Q[] Bank =
        {
            // ---- Finance / money ----
            new Q("What is a deductible?", "A tax refund", "The amount you pay before insurance covers the rest", "Your monthly premium", "A type of savings account", 1),
            new Q("What does IRS stand for?", "Internal Revenue Service", "International Revenue System", "Income Reporting Service", "Internal Reserve Service", 0),
            new Q("What does GDP stand for?", "Gross Domestic Product", "General Domestic Policy", "Gross Deposit Percentage", "Government Debt Payment", 0),
            new Q("What is a 401(k)?", "A type of mortgage", "An employer-sponsored retirement plan", "A credit score tier", "A tax form", 1),
            new Q("What is compound interest?", "Interest on the principal only", "Interest earned on both principal and prior interest", "A flat monthly fee", "A late payment penalty", 1),
            new Q("What is a credit score primarily used for?", "Setting your salary", "Assessing your creditworthiness to lenders", "Calculating income tax", "Rating your employer", 1),
            new Q("What does APR stand for?", "Annual Percentage Rate", "Approved Payment Ratio", "Average Principal Return", "Annual Payment Requirement", 0),
            new Q("What is a mortgage?", "A car lease", "A loan used to buy property", "A retirement fund", "An insurance premium", 1),
            new Q("What is gross income?", "Income after taxes", "Income before taxes and deductions", "Only investment income", "Income minus rent", 1),
            new Q("What is net income?", "Income before taxes", "Income after taxes and deductions", "Total company revenue", "Interest earned", 1),
            new Q("What does 'inflation' describe?", "Rising prices over time", "A stock market crash", "A drop in interest rates", "A tax increase", 0),
            new Q("What is a premium in insurance?", "The payout after a claim", "The regular amount you pay for coverage", "The deductible", "A bonus for no claims", 1),
            new Q("What is a budget?", "A type of loan", "A plan for income and spending", "A credit card limit", "A tax bracket", 1),
            new Q("What does 'liquid asset' mean?", "An asset easily converted to cash", "A real estate holding", "A long-term bond", "A retirement account", 0),
            new Q("What is a dividend?", "A loan repayment", "A share of company profits paid to shareholders", "A type of tax", "A bank fee", 1),
            new Q("What is a W-2 form used for?", "Applying for a loan", "Reporting annual wages and taxes withheld", "Opening a bank account", "Filing for unemployment", 1),
            new Q("What is the difference between debit and credit?", "Debit spends your own money; credit borrows", "They are identical", "Credit spends your own money; debit borrows", "Debit is only for online use", 0),
            new Q("What is a recession?", "A period of economic growth", "A significant decline in economic activity", "A tax holiday", "A rise in wages", 1),
            new Q("What does 'FICO' relate to?", "Credit scores", "Food safety", "Car insurance", "Passport applications", 0),
            new Q("What is escrow?", "A type of tax", "Funds held by a third party during a transaction", "A retirement plan", "A credit limit", 1),

            // ---- Cooking / food / drink ----
            new Q("What wine pairs best with pasta in a red tomato sauce?", "Champagne", "A medium-bodied red like Chianti", "Sweet dessert wine", "Cream sherry", 1),
            new Q("What temperature is considered a safe minimum for cooked chicken?", "45 C (113 F)", "74 C (165 F)", "30 C (86 F)", "55 C (131 F)", 1),
            new Q("What does 'al dente' mean?", "Overcooked and soft", "Firm to the bite", "Served cold", "Deep fried", 1),
            new Q("Which is a leavening agent?", "Baking soda", "Cornstarch", "Gelatin", "Vinegar", 0),
            new Q("What is the main ingredient in guacamole?", "Peas", "Avocado", "Zucchini", "Cucumber", 1),
            new Q("What does 'to sear' meat mean?", "Boil it slowly", "Brown the surface quickly over high heat", "Marinate overnight", "Freeze then thaw", 1),
            new Q("Which knife cut produces small cubes?", "Julienne", "Chiffonade", "Dice", "Batonnet", 2),
            new Q("What is the base of a classic hollandaise sauce?", "Egg yolks and butter", "Flour and milk", "Tomato and garlic", "Cream and cheese", 0),
            new Q("What leavens bread dough?", "Salt", "Yeast", "Sugar alone", "Oil", 1),
            new Q("What white wine is a classic pairing with oysters?", "Chablis", "Port", "Moscato", "Malbec", 0),
            new Q("What does 'simmer' mean?", "A rolling hard boil", "Cook gently just below boiling", "Cook in the oven", "Chill rapidly", 1),
            new Q("Which herb is traditional in pesto?", "Basil", "Rosemary", "Dill", "Sage", 0),
            new Q("What is 'mise en place'?", "A dessert", "Having ingredients prepped and ready before cooking", "A French wine", "A cut of beef", 1),
            new Q("Which is a whole grain?", "White rice", "Brown rice", "Semolina", "Cornflour", 1),
            new Q("What does deglazing a pan do?", "Cleans burnt food", "Uses liquid to lift browned bits for a sauce", "Sharpens flavor with salt", "Cools the pan quickly", 1),

            // ---- Civics / government / law ----
            new Q("How many U.S. senators does each state have?", "One", "Two", "Three", "Depends on population", 1),
            new Q("What document begins with 'We the People'?", "The Declaration of Independence", "The U.S. Constitution", "The Bill of Rights", "The Gettysburg Address", 1),
            new Q("How many amendments are in the Bill of Rights?", "Five", "Ten", "Twelve", "Twenty-seven", 1),
            new Q("What branch of government interprets laws?", "Executive", "Legislative", "Judicial", "Military", 2),
            new Q("How long is a U.S. presidential term?", "Two years", "Four years", "Six years", "Eight years", 1),
            new Q("What is the supreme law of the United States?", "Federal statutes", "The Constitution", "Executive orders", "State law", 1),
            new Q("What does 'veto' mean?", "To pass a law", "To reject a proposed law", "To amend a law", "To delay a vote", 1),
            new Q("Who is the head of the U.S. judicial branch?", "The President", "The Speaker of the House", "The Chief Justice", "The Vice President", 2),
            new Q("What is the minimum age to be U.S. President?", "25", "30", "35", "40", 2),
            new Q("What does 'jurisdiction' mean?", "A type of jury", "The authority to make legal decisions", "A courthouse", "A verdict", 1),
            new Q("What is a subpoena?", "A legal order to appear or produce evidence", "A jury verdict", "A type of lawyer", "A prison sentence", 0),
            new Q("What does 'bipartisan' mean?", "Against all parties", "Involving two political parties", "A single-party rule", "A type of election", 1),

            // ---- Geography / world ----
            new Q("What is the capital of Australia?", "Sydney", "Melbourne", "Canberra", "Perth", 2),
            new Q("What is the capital of Canada?", "Toronto", "Vancouver", "Ottawa", "Montreal", 2),
            new Q("Which is the largest ocean?", "Atlantic", "Indian", "Arctic", "Pacific", 3),
            new Q("Which is the longest river in the world?", "Amazon", "Nile", "Mississippi", "Yangtze", 1),
            new Q("On which continent is the Sahara Desert?", "Asia", "Africa", "Australia", "South America", 1),
            new Q("What is the capital of Japan?", "Osaka", "Kyoto", "Tokyo", "Nagoya", 2),
            new Q("Which country has the largest population?", "United States", "India", "Russia", "Brazil", 1),
            new Q("What is the tallest mountain above sea level?", "K2", "Mount Everest", "Kilimanjaro", "Denali", 1),
            new Q("What is the capital of France?", "Lyon", "Marseille", "Paris", "Nice", 2),
            new Q("Which U.S. state is the largest by area?", "Texas", "California", "Alaska", "Montana", 2),
            new Q("What is the capital of Germany?", "Munich", "Berlin", "Frankfurt", "Hamburg", 1),
            new Q("The Great Barrier Reef is off the coast of which country?", "Brazil", "Australia", "Mexico", "Thailand", 1),
            new Q("Which continent is the least populated?", "Australia", "Europe", "Antarctica", "South America", 2),
            new Q("What is the capital of Italy?", "Milan", "Venice", "Rome", "Naples", 2),
            new Q("Which country is both in Europe and Asia?", "Egypt", "Turkey", "Greece", "Morocco", 1),

            // ---- Science / nature ----
            new Q("What gas do plants absorb from the air for photosynthesis?", "Oxygen", "Carbon dioxide", "Nitrogen", "Hydrogen", 1),
            new Q("What is the chemical symbol for water?", "WO", "H2O", "CO2", "O2", 1),
            new Q("How many bones are in the adult human body?", "106", "206", "306", "406", 1),
            new Q("What planet is known as the Red Planet?", "Venus", "Jupiter", "Mars", "Saturn", 2),
            new Q("What is the powerhouse of the cell?", "Nucleus", "Ribosome", "Mitochondria", "Cell wall", 2),
            new Q("At what temperature does water freeze (at sea level)?", "0 C (32 F)", "10 C (50 F)", "-10 C (14 F)", "5 C (41 F)", 0),
            new Q("What force pulls objects toward Earth?", "Magnetism", "Gravity", "Friction", "Tension", 1),
            new Q("What is the largest planet in our solar system?", "Saturn", "Neptune", "Jupiter", "Earth", 2),
            new Q("What blood type is the universal donor?", "AB positive", "O negative", "A positive", "B negative", 1),
            new Q("What is the speed of light approximately?", "300,000 km/s", "3,000 km/s", "30 million km/s", "1,000 km/s", 0),
            new Q("What organ pumps blood through the body?", "Liver", "Lungs", "Heart", "Kidney", 2),
            new Q("What gas do humans exhale that plants use?", "Oxygen", "Carbon dioxide", "Helium", "Methane", 1),
            new Q("How many chambers does the human heart have?", "Two", "Three", "Four", "Five", 2),
            new Q("What is H on the periodic table?", "Helium", "Hydrogen", "Mercury", "Hafnium", 1),
            new Q("What causes the seasons on Earth?", "Distance from the sun", "The tilt of Earth's axis", "The moon's phases", "Ocean currents", 1),

            // ---- Health / body / everyday ----
            new Q("What does 'BMI' stand for?", "Body Mass Index", "Basic Muscle Indicator", "Blood Marker Index", "Body Metabolism Instrument", 0),
            new Q("How many days are in a leap year?", "364", "365", "366", "367", 2),
            new Q("What is the recommended amount of sleep for most adults?", "3-4 hours", "7-9 hours", "12-14 hours", "1-2 hours", 1),
            new Q("What vitamin does sunlight help your body produce?", "Vitamin A", "Vitamin C", "Vitamin D", "Vitamin K", 2),
            new Q("What is the normal human body temperature (approx.)?", "37 C (98.6 F)", "40 C (104 F)", "32 C (89.6 F)", "35 C (95 F)", 0),
            new Q("What does CPR stand for?", "Cardiopulmonary Resuscitation", "Central Pulse Recovery", "Cardiac Pressure Relief", "Chest Pump Routine", 0),
            new Q("Which nutrient is the body's main energy source?", "Carbohydrates", "Vitamins", "Water", "Fiber", 0),
            new Q("How often are U.S. federal elections for the House held?", "Every year", "Every two years", "Every four years", "Every six years", 1),
            new Q("What is dehydration?", "Excess water in the body", "A harmful lack of water in the body", "High blood sugar", "Low iron", 1),
            new Q("What does 'expiration date' on food indicate?", "The date it was made", "A date after which quality/safety may decline", "The delivery date", "The sale date", 1),

            // ---- Time / units / measurement ----
            new Q("How many minutes are in a full day?", "1,000", "1,440", "2,400", "3,600", 1),
            new Q("How many ounces are in a U.S. pound?", "12", "16", "20", "24", 1),
            new Q("How many centimeters are in a meter?", "10", "100", "1,000", "50", 1),
            new Q("How many weeks are in a year?", "48", "50", "52", "54", 2),
            new Q("How many degrees are in a right angle?", "45", "90", "180", "360", 1),
            new Q("How many sides does a hexagon have?", "Five", "Six", "Seven", "Eight", 1),
            new Q("How many millimeters are in a centimeter?", "5", "10", "100", "1,000", 1),
            new Q("How many hours are in a week?", "148", "168", "180", "200", 1),

            // ---- Work / documents / life admin ----
            new Q("What is a resume (CV)?", "A tax form", "A summary of your work experience and skills", "A rental agreement", "A credit report", 1),
            new Q("What is a lease?", "A property purchase", "A rental contract for a set term", "A mortgage payoff", "A utility bill", 1),
            new Q("What is a co-signer on a loan?", "The bank teller", "Someone who agrees to repay if the borrower can't", "A financial advisor", "The loan officer", 1),
            new Q("What does 'PTO' commonly mean at work?", "Paid Time Off", "Part Time Only", "Personal Task Order", "Prior To Onboarding", 0),
            new Q("What is a security deposit for a rental?", "The first month's rent", "Money held to cover potential damages", "A broker's fee", "A utility setup charge", 1),
            new Q("What is a warranty?", "A loan", "A guarantee to repair or replace a product", "A sales tax", "A rebate", 1),
            new Q("What does 'gross' vs 'net' pay differ by?", "Nothing", "Taxes and deductions", "Overtime only", "Bonuses only", 1),
            new Q("What is renters insurance for?", "Insuring the building", "Covering a tenant's belongings and liability", "Paying rent", "Covering the landlord's mortgage", 1),

            // ---- General knowledge / culture ----
            new Q("Who wrote the play 'Romeo and Juliet'?", "Charles Dickens", "William Shakespeare", "Mark Twain", "Jane Austen", 1),
            new Q("How many players are on a soccer team on the field?", "9", "10", "11", "12", 2),
            new Q("What is the currency of Japan?", "Won", "Yuan", "Yen", "Ringgit", 2),
            new Q("What is the currency used across much of the European Union?", "Pound", "Euro", "Franc", "Krona", 1),
            new Q("Which planet do we live on?", "Mars", "Venus", "Earth", "Mercury", 2),
            new Q("What language has the most native speakers worldwide?", "English", "Spanish", "Mandarin Chinese", "Hindi", 2),
            new Q("Who painted the Mona Lisa?", "Michelangelo", "Leonardo da Vinci", "Raphael", "Donatello", 1),
            new Q("What is the freezing point of water in Fahrenheit?", "0 F", "32 F", "100 F", "212 F", 1),
            new Q("What is the boiling point of water in Celsius at sea level?", "50 C", "90 C", "100 C", "120 C", 2),
            new Q("What does 'ATM' stand for?", "Automated Teller Machine", "Any Time Money", "Account Transfer Method", "Automatic Transaction Manager", 0),
            new Q("What does 'GPS' stand for?", "Global Positioning System", "General Purpose Server", "Ground Position Signal", "Global Path Service", 0),
            new Q("What does 'URL' stand for?", "Uniform Resource Locator", "Universal Reading Link", "User Redirect Location", "Unified Route Label", 0),
            new Q("What is the largest mammal on Earth?", "African elephant", "Blue whale", "Giraffe", "Hippopotamus", 1),
            new Q("How many continents are there?", "Five", "Six", "Seven", "Eight", 2),
            new Q("What does 'RSVP' request?", "A gift", "A reply to an invitation", "A refund", "A signature", 1),
        };
    }
}
