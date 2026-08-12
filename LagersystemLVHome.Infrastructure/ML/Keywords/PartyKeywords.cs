namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for parties & decoration
/// </summary>
public class PartyKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Party & Fest";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // General
            "party", "feier", "fest", "celebration", "geburtstag", "birthday",
            "hochzeit", "wedding", "silvester", "newyear", "weihnachten", "christmas",
            "halloween", "ostern", "easter", "karneval", "fasching",

            // Decorations
            "dekoration", "deko", "decoration", "schmuck", "ornament",
            "girlande", "garland", "banner", "wimpelkette", "bunting",
            "luftballon", "balloon", "luftballons", "balloons", "helium",
            "konfetti", "confetti", "streudeko", "scatter",
            "luftschlangen", "streamers", "serpentinen",
            "pom-pom", "pompom", "rosette", "rosetten",

            // Table decoration
            "tischdeko", "table", "decoration", "centerpiece",
            "kerzenhalter", "candle", "holder", "teelichthalter",
            "serviettenringe", "napkin", "rings", "platzhalter",
            "tischkarten", "place", "cards", "namensschilder",
            "streuartikel", "scatter", "decoration",

            // Tableware & cutlery
            "pappteller", "paper", "plates", "einwegteller", "disposable",
            "plastikbecher", "plastic", "cups", "becher", "trinkbecher",
            "strohhalme", "straws", "trinkhalme",
            "plastikbesteck", "cutlery", "einwegbesteck",
            "servietten", "napkins", "papierservietten",

            // Costumes & accessories
            "kostüm", "costume", "verkleidung", "disguise",
            "maske", "mask", "gesichtsmaske", "augenmaske",
            "perücke", "wig", "hut", "hat", "krone", "crown",
            "umhang", "cape", "mantel", "cloak",
            "schminkfarben", "face", "paint", "schminke",

            // Games & entertainment
            "partyspiel", "party", "game", "trinkspiel", "drinking",
            "quiz", "quizspiel", "brettspiel", "board",
            "karaoke", "mikrofon", "microphone", "sing",
            "fotobox", "photobooth", "foto", "requisiten", "props",

            // Lighting & effects
            "lichterkette", "fairy", "lights", "string",
            "partybeleuchtung", "lighting", "discokugel",
            "led", "lampe", "lamp", "stroboskop", "strobe",
            "nebelmaschine", "fog", "machine", "seifenblasen", "bubbles",

            // Invitations & cards
            "einladungskarte", "invitation", "card", "einladung",
            "geburtstagskarte", "birthday", "glückwunschkarte",
            "dankeskarte", "thank", "you", "umschlag", "envelope",

            // Candles
            "kerze", "candle", "zahlenkerze", "number", "kerzen",
            "geburtstagskerze", "wunderkerzen", "sparklers",
            "teelichter", "tea", "lights", "duftkerzen", "scented",

            // Gift wrapping
            "geschenkpapier", "wrapping", "paper", "geschenktüte", "gift", "bag",
            "schleife", "ribbon", "bow", "geschenkband", "geschenkbox",

            // Cakes & baking supplies
            "tortenständer", "cake", "stand", "tortendeko", "topper",
            "muffinfürmchen", "cupcake", "cases", "backformen",
            "zahnstocher", "toothpicks", "picker", "spieße", "skewers",

            // Wedding specifics
            "gastgeschenk", "wedding", "favor", "hochzeitsdeko",
            "ringkissen", "ring", "pillow", "cushion",
            "blumenkinder", "flower", "girl", "streublumen",
            "sektgläser", "champagne", "glasses", "prosecco",

            // Themes
            "motto", "theme", "themed", "themenparty",
            "einhorn", "unicorn", "meerjungfrau", "mermaid",
            "piraten", "pirate", "prinzessin", "princess",
            "superheld", "superhero", "dschungel", "jungle"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["party"] = 2.0,
            ["feier"] = 2.0,
            ["fest"] = 1.5,
            ["dekoration"] = 1.5,
            ["geburtstag"] = 1.5
        };
    }
}
