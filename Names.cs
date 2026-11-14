namespace Dotburg;

// The name maker (0.78): given names and family names from five traditions (tongues), each built several ways
// (curated lists, Old English two-part names, pet forms, word names, patronymics, place compounds, trades,
// nicknames). Every town leans toward one tongue, so Oakford can sound northern and Fenwick Flemish, while
// newcomers keep the mix varied. Pure functions of a Random: callers seed it from a hash, so names never touch the
// town's random stream. Uniqueness inside a town is the caller's job (Sim.Christen).
static class Names
{
    public const int English = 0, Old = 1, Norse = 2, Celtic = 3, Low = 4, Tongues = 5;

    // A stable hash of a string (FNV-1a). string.GetHashCode changes every run, so it cannot be used for names.
    public static int Seed(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h = unchecked(h * 16777619); }
        return (int)(h & 0x7fffffff);
    }

    // How a town (or a sister) leans: a base mix every town shares, plus a large share for its own home tongue.
    public static double[] Lean(int seed)
    {
        var w = new[] { 0.40, 0.18, 0.14, 0.14, 0.14 };
        var r = new Random(seed);
        double roll = r.NextDouble();
        int home = roll < 0.36 ? English : roll < 0.52 ? Old : roll < 0.68 ? Norse : roll < 0.84 ? Celtic : Low;
        w[home] += 0.55;
        return w;
    }

    public static int TongueOf(Random r, double[] lean)
    {
        double sum = lean.Sum(), x = r.NextDouble() * sum;
        for (int i = 0; i < lean.Length; i++) { x -= lean[i]; if (x < 0) return i; }
        return English;
    }

    // A whole person: tongue, given name and family name. Family names drift toward English a little, as names
    // do when a family settles somewhere.
    public static (string first, string last) Person(Random r, double[] lean)
    {
        int t = TongueOf(r, lean);
        string first = Given(r, t);
        int ft = r.NextDouble() < 0.82 ? t : English;
        string last = Family(r, ft);
        for (int i = 0; i < 6 && Clash(first, last); i++) last = Family(r, ft);
        return (first, last);
    }

    // A first name that would read oddly with this family name: the same word, or one inside the other.
    static bool Clash(string first, string last)
    {
        string f = first.ToLowerInvariant(), l = last.ToLowerInvariant();
        return l.Contains(f) || (f.Length >= 4 && l.StartsWith(f[..4]));
    }

    // ---------- given names ----------
    public static string Given(Random r, int tongue)
    {
        double x = r.NextDouble();
        // Dotburg's own homegrown names turn up in every tongue now and then.
        if (x < 0.08) return P(r, Homegrown);
        x = r.NextDouble();
        switch (tongue)
        {
            case English:
                return x < 0.58 ? P(r, EnglishGiven) : x < 0.78 ? P(r, PetNames) : P(r, WordNames);
            case Old:
                return x < 0.8 ? Compound(r) : P(r, EnglishGiven);
            case Norse:
                return P(r, NorseGiven);
            case Celtic:
                return P(r, CelticGiven);
            default:
                return P(r, LowGiven);
        }
    }

    // An Old English two-part name, as the Anglo-Saxons built them: Ed + ric, Wulf + stan, Hild + gyth.
    static string Compound(Random r)
    {
        for (int i = 0; i < 12; i++)
        {
            string a = P(r, OldFront), b = P(r, r.NextDouble() < 0.5 ? OldBackA : OldBackB);
            string al = a.ToLowerInvariant();
            if (al.Contains(b) || b.Contains(al) || al[^1] == b[0]) continue;   // Wulf+ulf, Hild+hild, Ed+dred
            string n = a + b;
            if (MaxConsonantRun(n) >= 5 || !Clean(n)) continue;
            return n;
        }
        return P(r, EnglishGiven);
    }

    // ---------- family names ----------
    public static string Family(Random r, int tongue)
    {
        double x = r.NextDouble();
        switch (tongue)
        {
            case Norse:
                return x < 0.45 ? Patronym(r, NorseGiven, "sen", "son") : x < 0.85 ? Place(r, NorseFront, NorseBack) : P(r, NorseBynames);
            case Celtic:
                return x < 0.42 ? P(r, CelticFamily) : x < 0.67 ? Mac(r) : x < 0.85 ? Place(r, CornishFront, CornishBack) : EnglishFamily(r);
            case Low:
                return x < 0.5 ? P(r, LowFamily) : x < 0.78 ? Van(r) : x < 0.9 ? Patronym(r, LowGiven, "s", "sen") : EnglishFamily(r);
            default:
                return EnglishFamily(r);
        }
    }

    static string EnglishFamily(Random r)
    {
        double x = r.NextDouble();
        return x < 0.27 ? P(r, Trades)
            : x < 0.58 ? Place(r, EnglishFront, EnglishBack)
            : x < 0.70 ? P(r, Patronymics)
            : x < 0.78 ? Patronym(r, EnglishGiven, "son", "")
            : x < 0.90 ? P(r, Nicknames)
            : P(r, Features);
    }

    // A place-made name: Ash + ford, Lind + berg, Tre + wen. Never the same word twice, never a rude one.
    static string Place(Random r, string[] front, string[] back)
    {
        for (int i = 0; i < 12; i++)
        {
            string a = P(r, front), b = P(r, back);
            string al = a.ToLowerInvariant();
            if (al.Contains(b) || b.Contains(al) || al[^1] == b[0]) continue;
            string n = a + b;
            if (MaxConsonantRun(n) >= 5 || !Clean(n)) continue;
            return n;
        }
        return P(r, Trades);
    }

    // A father's name made into a family name: Gunnar to Gunnarsen, Hal to Halson. Names ending in a vowel take
    // the second ending, or a name that ends cleanly.
    static string Patronym(Random r, string[] pool, string afterConsonant, string afterVowel)
    {
        for (int i = 0; i < 10; i++)
        {
            string g = P(r, pool);
            if (g.Length > 7) continue;
            bool vowel = "aeiouy".Contains(char.ToLowerInvariant(g[^1]));
            // English: Gilbertson from a short name; never Alices, Dorcasson or Hannahson.
            if (afterVowel == "" && (vowel || g.Length > 6 || "shx".Contains(char.ToLowerInvariant(g[^1])))) continue;
            string n = vowel ? (afterVowel == "son" ? g[..^1] + "son" : g + afterVowel) : g + afterConsonant;
            if (n.Length > 11 || MaxConsonantRun(n) >= 4 || !Clean(n)) continue;
            return n;
        }
        return P(r, Patronymics);
    }

    // Mac + a Gaelic name: MacFergus, MacNiall.
    static string Mac(Random r)
    {
        for (int i = 0; i < 10; i++)
        {
            string g = P(r, MacStems);
            if (Clean(g)) return "Mac" + g;
        }
        return P(r, CelticFamily);
    }

    // van Dijk, van der Meer, van den Bosch.
    static string Van(Random r)
    {
        double x = r.NextDouble();
        return x < 0.4 ? "van " + P(r, VanPlain) : x < 0.75 ? "van der " + P(r, VanDer) : "van den " + P(r, VanDen);
    }

    // ---------- checks ----------
    static int MaxConsonantRun(string s)
    {
        int run = 0, most = 0;
        foreach (char c in s.ToLowerInvariant())
        {
            if (c == ' ') { run = 0; continue; }
            run = "aeiouy".Contains(c) ? 0 : run + 1;
            most = Math.Max(most, run);
        }
        return most;
    }

    // Generated names must not spell anything rude. Checked as substrings of the lowercase name.
    static readonly string[] Rude = { "shit", "fuck", "cunt", "dick", "cock", "piss", "twat", "arse", "slut", "whore", "fag", "nig", "rape", "turd", "bum", "tit", "puke", "poo", "fart", "anal", "sex", "porn", "kill", "nazi" };
    static bool Clean(string n) { string l = n.ToLowerInvariant(); return !Rude.Any(l.Contains); }

    static string P(Random r, string[] a) => a[r.Next(a.Length)];

    // Every distinct name the lists alone can make (the generators add far more), for --names.
    public static int ListedGiven => Homegrown.Length + EnglishGiven.Length + PetNames.Length + WordNames.Length + NorseGiven.Length + CelticGiven.Length + LowGiven.Length;

    // ---------- the lists ----------
    // Dotburg's own: the names the town has always had (the first 40 were the whole list before 0.78).
    static readonly string[] Homegrown =
    {
        "Ada", "Bram", "Cora", "Dell", "Edda", "Finn", "Greta", "Hob", "Ivo", "Juno", "Kit", "Lark", "Mabel", "Ned", "Odo", "Pim",
        "Quill", "Rosa", "Sim", "Tilda", "Ulf", "Vera", "Wynn", "Yara", "Zeb", "Agnes", "Bertil", "Clem", "Dora", "Esme", "Fenn",
        "Gil", "Hester", "Isolde", "Jory", "Kester", "Lotte", "Merry", "Nell", "Osric",
        "Tansy", "Pell", "Wick", "Dob", "Nim", "Tuck", "Brin", "Moll", "Pip", "Rook", "Sorrel", "Tam", "Bel", "Jonquil", "Fen",
    };

    static readonly string[] EnglishGiven =
    {
        "Alice", "Amabel", "Amice", "Annis", "Avice", "Beatrix", "Bess", "Bridget", "Cecily", "Christian", "Clemency", "Constance",
        "Dorcas", "Edith", "Elinor", "Emmot", "Felice", "Grace", "Hawise", "Isobel", "Jennet", "Joan", "Juliana", "Lettice",
        "Margery", "Marion", "Maud", "Millicent", "Muriel", "Petronel", "Philippa", "Rose", "Sabine", "Sybil", "Tamsin", "Ursula",
        "Wilmot", "Winifred", "Martha", "Hannah", "Susanna", "Judith", "Lydia", "Abigail", "Thomasina", "Joyce", "Alison", "Eleanor",
        "Adam", "Alan", "Benedict", "Clement", "Colin", "Crispin", "Denis", "Elias", "Everard", "Fulk", "Gervase", "Gilbert",
        "Giles", "Godfrey", "Guy", "Hamon", "Hugh", "Humphrey", "Jasper", "Jocelin", "Lambert", "Laurence", "Matthew", "Miles",
        "Nicholas", "Noel", "Piers", "Ralph", "Randal", "Reynold", "Robin", "Roger", "Simon", "Stephen", "Theobald", "Thomas",
        "Tobias", "Walter", "Warin", "Abel", "Ambrose", "Barnaby", "Cuthbert", "Ezra", "Gideon", "Jethro", "Josiah", "Silas",
        "Rufus", "Bartholomew", "Benjamin", "Edmund", "Francis", "Geoffrey", "Henry", "Jeremy", "Lionel", "Martin", "Oliver", "Peter",
    };

    static readonly string[] PetNames =
    {
        "Hobkin", "Watkin", "Perkin", "Jenkin", "Tibby", "Dawkin", "Halkin", "Tomkin", "Wilkin", "Simkin", "Jankin", "Hodge",
        "Dobbin", "Malkin", "Gillot", "Annot", "Jonet", "Mattie", "Bessie", "Kitty", "Molly", "Polly", "Dolly", "Sukey", "Tibbie",
        "Nettie", "Lettie", "Neddy", "Jem", "Sandy", "Hal", "Wat", "Jack", "Will", "Nan", "Meg", "Tess", "Sib", "Bet", "Phyll",
        "Larkin", "Hankin", "Bennet", "Dickon", "Rafe", "Kate", "Effie", "Tilly", "Hetty", "Jinny",
    };

    // Plants, birds and virtues, as country people named their children.
    static readonly string[] WordNames =
    {
        "Bramble", "Clover", "Wren", "Hazel", "Rowan", "Linnet", "Briony", "Primrose", "Heather", "Holly", "Ivy", "Marigold",
        "Sage", "Willow", "Robin", "Finch", "Teasel", "Yarrow", "Mercy", "Patience", "Temperance", "Honor", "Faith", "Comfort",
        "Prudence", "Charity", "Verity", "Constant", "Hope", "Joy", "Blythe", "Hawthorn", "Ash", "Alder", "Rue", "Sloe",
    };

    static readonly string[] OldFront = { "Al", "Ald", "Bern", "Ced", "Cuth", "Dun", "Ed", "Eg", "Elf", "Ethel", "God", "Here", "Hild", "Ken", "Leof", "Mild", "Ord", "Os", "Sig", "Wig", "Wil", "Win", "Wulf", "Ead", "Burg", "Cyn", "Tid", "Sel", "Beorn", "Ath" };
    static readonly string[] OldBackA = { "ric", "win", "mund", "stan", "gar", "helm", "wald", "ward", "ulf", "bert", "red", "frith", "noth", "here", "wine", "mer", "las", "ferth" };
    static readonly string[] OldBackB = { "gyth", "wyn", "hild", "burga", "fled", "ith", "swith", "run", "gifu", "ware", "lin", "rada", "isa", "eda" };

    static readonly string[] NorseGiven =
    {
        "Arne", "Asa", "Astrid", "Bjorn", "Bodil", "Brand", "Dagny", "Eirik", "Eskil", "Freya", "Frode", "Gisla", "Gorm", "Gudrun",
        "Gunnar", "Halla", "Halvard", "Hauk", "Helga", "Ingrid", "Ivar", "Jorund", "Ketil", "Knut", "Leif", "Liv", "Orm", "Ragna",
        "Runa", "Rolf", "Sigrid", "Signe", "Solvi", "Sten", "Svala", "Thora", "Thorvald", "Tove", "Unn", "Vigdis", "Yngvar",
        "Alva", "Embla", "Geir", "Hedda", "Kari", "Oddny", "Sune", "Toke", "Trygve", "Yrsa", "Asgeir", "Bergljot", "Dag", "Egil",
        "Finnbogi", "Grim", "Hakon", "Ingeborg", "Jarl", "Kolbein", "Ljot", "Olav", "Ragnhild", "Saga", "Solveig", "Torvi", "Vali",
        "Aud", "Brynja", "Eydis", "Gyda", "Hallgerd", "Rannveig", "Steinunn", "Thyra", "Arnbjorg", "Hrafn", "Sindri", "Vidar",
    };

    static readonly string[] CelticGiven =
    {
        "Aed", "Aine", "Alun", "Bran", "Bride", "Cai", "Cadoc", "Ciaran", "Deri", "Dilys", "Elin", "Emrys", "Eira", "Fergus",
        "Finnian", "Gwen", "Gwilym", "Hywel", "Idris", "Ifor", "Mabon", "Maeve", "Morwen", "Nia", "Niall", "Nessa", "Oona", "Owain",
        "Padrig", "Rhian", "Rhys", "Ronan", "Seren", "Sian", "Tegan", "Tudur", "Una", "Deirdre", "Ailsa", "Morag", "Cormac",
        "Donal", "Kenna", "Merryn", "Peran", "Senara", "Elowen", "Locryn", "Angharad", "Bronwen", "Cerys", "Dafydd", "Eluned",
        "Gethin", "Iolo", "Llew", "Myfanwy", "Rhodri", "Tomos", "Branwen", "Catriona", "Ewan", "Fiona", "Iona", "Lachlan", "Mairi",
        "Ruairi", "Sorcha", "Brannoc", "Demelza", "Jago", "Kerensa", "Tristan", "Wenna", "Aoife", "Cian", "Eithne", "Orla",
    };

    static readonly string[] LowGiven =
    {
        "Aart", "Anke", "Anneke", "Bartel", "Brecht", "Coen", "Dirk", "Eelke", "Elke", "Femke", "Floris", "Gerrit", "Griet", "Hanne",
        "Hendrik", "Hilde", "Jelle", "Joost", "Jutta", "Kees", "Klaas", "Luuk", "Maaike", "Marijke", "Mies", "Nelleke", "Okke",
        "Pieter", "Rutger", "Saskia", "Teun", "Ute", "Wim", "Wouter", "Ilse", "Kaatje", "Lieve", "Mareike", "Ida", "Amalia",
        "Arend", "Baukje", "Cornelis", "Diederik", "Evert", "Fenna", "Geertje", "Harmen", "Ingeborg", "Jannetje", "Joris", "Lambrecht",
        "Machteld", "Niels", "Otto", "Roel", "Sander", "Tjalling", "Willem", "Ysbrand", "Aleid", "Barend", "Clara", "Frans",
        "Gijs", "Hester", "Jacoba", "Lysbet", "Marten", "Reinout", "Stijn", "Trude", "Veerle", "Wendel",
    };

    static readonly string[] Trades =
    {
        "Baker", "Brewer", "Carter", "Chandler", "Cooper", "Cotter", "Draper", "Fletcher", "Fowler", "Glover", "Hayward", "Hooper",
        "Lister", "Mason", "Miller", "Parker", "Potter", "Reeve", "Sawyer", "Shepherd", "Slater", "Smith", "Tanner", "Thatcher",
        "Turner", "Walker", "Webb", "Wright", "Ward", "Wainwright", "Cartwright", "Tyler", "Collier", "Fisher", "Fuller", "Dyer",
        "Saddler", "Spicer", "Tasker", "Sumner", "Bowyer", "Arkwright", "Barker", "Brewster", "Butler", "Clark", "Cook", "Croft",
        "Dexter", "Falconer", "Farrier", "Forester", "Gardner", "Harper", "Hunter", "Kemp", "Knight", "Marshall", "Mercer", "Napier",
        "Packer", "Palmer", "Pedlar", "Plowman", "Porter", "Roper", "Salter", "Skinner", "Steward", "Tucker", "Vintner", "Weaver",
        "Woodward", "Lorimer", "Hogg", "Shearer", "Thresher", "Bellman", "Kettle",
    };

    static readonly string[] EnglishFront = { "Ash", "Birch", "Black", "Brook", "Clay", "Cold", "Crane", "Elder", "Fern", "Green", "Hawk", "Hazel", "Holly", "Lang", "Mill", "Moss", "North", "Oak", "Red", "Rush", "Stan", "Thorn", "Under", "West", "White", "Wild", "Wolf", "Yew", "Ald", "Bram", "Cal", "Brad", "Hart", "Kings", "Lamb", "Mar", "Pen", "Rad", "Sand", "Stock", "Sut", "Wake", "Whit", "East", "South", "Cran", "Fox" };
    static readonly string[] EnglishBack = { "wood", "ford", "field", "ley", "ton", "by", "ham", "well", "brook", "hill", "dale", "worth", "hurst", "combe", "croft", "gate", "more", "stead", "thwaite", "wick", "cliffe", "bridge", "low", "shaw", "den", "holme", "land", "ridge", "stone", "well", "beck", "garth" };
    static readonly string[] Patronymics =
    {
        "Hobson", "Hobbs", "Watson", "Watkins", "Jackson", "Tomlin", "Tompkins", "Wilson", "Wilkins", "Robson", "Roberts", "Hudson",
        "Dobson", "Dobbs", "Hodgson", "Hodges", "Perkins", "Jenkins", "Simpson", "Simms", "Dawson", "Dawkins", "Gibson", "Gibbs",
        "Dixon", "Higgins", "Harrison", "Atkins", "Adams", "Batson", "Pearson", "Rawson", "Stevens", "Fitzhugh", "Fitzwalter",
        "Allison", "Bateson", "Collins", "Dickens", "Edison", "Ellison", "Gilson", "Hewitt", "Jennings", "Matson", "Nelson",
        "Randall", "Sanderson", "Tennyson", "Wilcox", "Paulson", "Mabbs", "Emmott", "Cutts", "Hutchins", "Pascoe",
    };
    static readonly string[] Nicknames =
    {
        "Armstrong", "Blythe", "Bold", "Bright", "Doughty", "Fairweather", "Fairchild", "Goodchild", "Goodman", "Golightly",
        "Gray", "Hardy", "Keen", "Lightfoot", "Littlejohn", "Longman", "Merriman", "Moody", "Noble", "Proudfoot", "Read", "Russell",
        "Sharp", "Small", "Sparrowhawk", "Swift", "Truelove", "Wise", "Young", "Whitlock", "Goodbody", "Bonner", "Fairfax",
        "Gentle", "Starling", "Lovegood", "Wildgoose", "Crookshank", "Pennyfather", "Fullalove", "Toogood", "Makepeace", "Brown",
    };
    static readonly string[] Features =
    {
        "Brook", "Hill", "Marsh", "Wood", "Field", "Lane", "Atwood", "Bywater", "Underhill", "Attwell", "Moor", "Heath", "Dale",
        "Green", "Ford", "Banks", "Bush", "Hollins", "Townsend", "Nash", "Noakes", "Rivers", "Fell", "Combe", "Holt", "Birch",
        "Reed", "Thorne", "Coates", "Shaw", "Styles", "Wells", "Pike", "Sorrel", "Nettle", "Sparrow", "Rook", "Flint", "Quarry",
    };

    static readonly string[] NorseFront = { "Berg", "Dal", "Lind", "Sand", "Holm", "Ek", "Strom", "Nord", "Ask", "Bjork", "Lund", "Hag", "Fjell", "Gran", "Alm", "Sol", "Vass", "Sten", "Ulv" };
    static readonly string[] NorseBack = { "strom", "berg", "lund", "holm", "dal", "quist", "gren", "by", "vik", "ness", "stad", "rud", "heim", "mark", "land", "sjo" };
    static readonly string[] NorseBynames = { "Strand", "Berg", "Dahl", "Lund", "Holm", "Vik", "Moe", "Haug", "Fjeld", "Ness", "Bakke", "Rud", "Sand", "Lind", "Hauge", "Myhre", "Tveit", "Aas", "Bru", "Fosse", "Rygg", "Kvam", "Brekke" };

    static readonly string[] CelticFamily =
    {
        "Price", "Pryce", "Powell", "Bevan", "Pugh", "Griffith", "Lloyd", "Morgan", "Vaughan", "Madoc", "Ellis", "Gwynne", "Tudor",
        "Kennedy", "Brennan", "Doyle", "Quinn", "Rooney", "Cassidy", "Moran", "Nolan", "Byrne", "Duffy", "Munro", "Prosser",
        "Bowen", "Probert", "Parry", "Owens", "Hughes", "Cadwallader", "Gallagher", "Brady", "Keane", "Flynn", "Lennox", "Galbraith",
        "Kerr", "Ross", "Baird", "Carmichael", "Drummond", "Lamont", "Rennie", "Tolley", "Kitto", "Jago", "Rowe", "Nance",
    };
    static readonly string[] MacStems = { "Fergus", "Niall", "Cormac", "Ivor", "Lachlan", "Aulay", "Leod", "Kay", "Rae", "Gill", "Bain", "Dowall", "Intyre", "Kinnon", "Lennan", "Nab", "Phail", "Quarrie", "Taggart", "Allister", "Cready" };
    static readonly string[] CornishFront = { "Tre", "Pen", "Pol", "Ros", "Lan", "Car", "Nan" };
    static readonly string[] CornishBack = { "wen", "rose", "dower", "gelly", "vean", "nance", "harne", "morva", "lowen", "wartha", "goose", "veor", "kerris" };

    static readonly string[] LowFamily =
    {
        "Bakker", "Visser", "Smit", "de Vries", "de Wit", "de Groot", "de Jong", "Mulder", "Kuiper", "Brouwer", "Timmer", "Dekker",
        "Schipper", "Verhoeven", "Hoekstra", "Postma", "Wouters", "Janssen", "Peeters", "Claes", "Martens", "Goossens", "Maes",
        "Verbeek", "Vos", "Haas", "Koster", "Meijer", "de Boer", "Brink", "Prins", "Kramer", "Hoogland", "Zwart", "Molenaar",
        "Kok", "Wever", "Verlinden", "de Smet", "Vermeulen", "Dijkstra", "Bosma", "Terpstra", "Boon", "Stoffels", "Roos",
    };
    static readonly string[] VanPlain = { "Dijk", "Dam", "Leeuwen", "Rijn", "Wijk", "Beek", "Loon", "Hout", "Laar", "Gent", "Aken", "Dongen", "Vliet", "Hal" };
    static readonly string[] VanDer = { "Meer", "Berg", "Linde", "Heide", "Veen", "Wal", "Sluis", "Brugge", "Poel", "Weide", "Kamp", "Hoeven" };
    static readonly string[] VanDen = { "Bosch", "Broek", "Hoek", "Akker", "Brink", "Heuvel", "Dool", "Eynde" };
}

// The town side of naming: each dot-man is christened once, saved, and never shares a full name with anyone
// living in town (nor a first name, while there are names to spare). Draws come from a hash of the id, never Rng.
partial class Sim
{
    const double KinShare = 0.15;        // a newcomer who turns out to be a cousin of a family already here
    double[]? lean; string leanFor = "";

    // The town's lean, from its name: the same every run, nothing saved.
    double[] TownLean
    {
        get
        {
            if (lean == null || leanFor != Name) { lean = Names.Lean(Names.Seed(Name + Founded)); leanFor = Name; }
            return lean;
        }
    }

    // Give a dot-man a name nobody living in town has. A family name, if given (a child's parent's), is kept.
    public void Christen(Walker w, string? family = null)
    {
        // Kin: some newcomers share a family name with someone already here.
        if (family == null && Trait01(w.Id, 899) < KinShare)
        {
            var fams = Walkers.Where(x => x != w && !x.Gone && x.Kind == "" && x.Last != "").Select(x => x.Last).Distinct().ToList();
            if (fams.Count >= 4) family = fams[(int)(Trait01(w.Id, 898) * fams.Count) % fams.Count];
        }
        string first = "", last = "";
        for (int k = 0; k < 80; k++)
        {
            var r = new Random((int)(Trait01(w.Id, 900 + k) * int.MaxValue));
            var (f, l) = Names.Person(r, TownLean);
            if (family != null) l = family;
            bool firstTaken = false, fullTaken = false;
            foreach (var x in Walkers)
            {
                if (x == w || x.Gone || x.Kind != "" || x.First != f) continue;
                firstTaken = true;
                if (x.Last == l) { fullTaken = true; break; }
            }
            if (k == 0 || !fullTaken) { first = f; last = l; }
            // A first name of one's own is wanted for the first 50 tries; after that a unique full name will do.
            if (!fullTaken && (!firstTaken || k >= 50)) break;
        }
        w.First = first;
        w.Last = last;
    }

    // A notable or a sister's ruler: a given name from a seed and a lean (theirs, or our town's).
    public static string GivenFrom(int seed, double[] lean)
    {
        var r = new Random(seed & 0x7fffffff);
        return Names.Given(r, Names.TongueOf(r, lean));
    }
    public static double[] LeanOf(string place) => Names.Lean(Names.Seed(place));

    // The names before 0.78 (40 first names, 32 family names, from a hash of the id): kept so dot-men from an older
    // save keep the names their bonds and the chronicle already use.
    static readonly string[] OldFirst =
    {
        "Ada", "Bram", "Cora", "Dell", "Edda", "Finn", "Greta", "Hob", "Ivo", "Juno", "Kit", "Lark", "Mabel", "Ned", "Odo", "Pim",
        "Quill", "Rosa", "Sim", "Tilda", "Ulf", "Vera", "Wynn", "Yara", "Zeb",
        "Agnes", "Bertil", "Clem", "Dora", "Esme", "Fenn", "Gil", "Hester", "Isolde", "Jory", "Kester", "Lotte", "Merry", "Nell", "Osric",
    };
    static readonly string[] OldBy =
    {
        "Thatch", "Miller", "Ashby", "Crook", "Fell", "Barley", "Cobb", "Hale", "Rook", "Marsh", "Tanner", "Wick", "Penny", "Hart",
        "Moss", "Ware", "Brook", "Quarry", "Flint", "Sorrel", "Pike", "Lamb", "Cotter", "Drum", "Kettle", "Reed", "Holt", "Birch",
        "Gage", "Nettle", "Sparrow", "Combe",
    };
    public static void LegacyName(Walker w)
    {
        w.First = OldFirst[(int)(Trait01(w.Id, 777) * OldFirst.Length) % OldFirst.Length];
        w.Last = OldBy[(int)(Trait01(w.Id, 778) * OldBy.Length) % OldBy.Length];
    }

    // After loading an older save: everyone keeps their old name, except a dot-man who shares a full name, or a
    // first name, with someone earlier in the list; they are named anew. The leader and anyone with a body (former
    // leaders) keep theirs, since the seat and the chronicle know them by it.
    public void RenameDuplicates()
    {
        var firsts = new HashSet<string>();
        var fulls = new HashSet<string>();
        var keep = Walkers.Where(w => w.Lead || Bodies.Any(b => b.Id == w.Id)).ToList();
        foreach (var w in keep) { firsts.Add(w.First); fulls.Add(w.First + " " + w.Last); }
        foreach (var w in Walkers)
        {
            if (w.Gone || keep.Contains(w)) continue;
            if (firsts.Contains(w.First) || fulls.Contains(w.First + " " + w.Last)) { w.First = ""; w.Last = ""; Christen(w); }
            firsts.Add(w.First); fulls.Add(w.First + " " + w.Last);
        }
    }

    // --names: a sample of townsfolk names for each lean, and how often names repeat in a big town. Returns lines.
    public static IEnumerable<string> NameSamples()
    {
        yield return $"listed given names: {Names.ListedGiven} (plus Old English compounds)";
        string[] towns = { "Ashford", "Oakmere", "Fenwick", "Thornbury", "Brookdale", "Heronstead" };
        foreach (var t in towns)
        {
            var l = LeanOf(t);
            var r = new Random(Names.Seed(t));
            var people = Enumerable.Range(0, 12).Select(_ => Names.Person(r, l)).Select(p => $"{p.first} {p.last}");
            yield return $"{t} (home tongue {Array.IndexOf(l, l.Max())}): {string.Join(", ", people)}";
        }
        // Repeats: 2000 draws for one town, before the uniqueness check.
        var lean0 = LeanOf("Ashford");
        var rr = new Random(1);
        var all = Enumerable.Range(0, 2000).Select(_ => Names.Person(rr, lean0)).ToList();
        yield return $"2000 draws: {all.Select(p => p.first).Distinct().Count()} distinct first names, {all.Select(p => p.last).Distinct().Count()} distinct family names, {all.Distinct().Count()} distinct full names";
    }
}
