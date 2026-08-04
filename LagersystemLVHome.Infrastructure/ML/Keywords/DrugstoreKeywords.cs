namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for drugstore products (personal care, household chemicals)
/// </summary>
public class DrugstoreKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Drogerie";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // General
            "drogerie", "drugstore", "dm", "rossmann", "müller",

            // Hair care
            "shampoo", "conditioner", "spülung", "haarspülung", "haarkur",
            "haarmaske", "mask", "treatment", "repair", "pflege",
            "haarfarbe", "coloration", "tönung", "tint", "fürben",
            "haarspray", "gel", "wachs", "mousse", "schaum",
            "föhn", "glätteisen", "lockenstab", "haarbürste", "kamm",

            // Body care
            "duschgel", "shower", "gel", "dusche", "bad",
            "bodylotion", "body", "lotion", "milk", "butter",
            "handcreme", "hand", "cream", "nivea", "neutrogena",
            "gesichtscreme", "face", "cream", "anti", "aging",
            "peeling", "scrub", "körperpeeling", "gesichtspeeling",
            "maske", "gesichtsmaske", "sheet", "mask",
            "reinigung", "cleansing", "waschgel", "mizellenwasser",

            // Sun protection
            "sonnencreme", "sunscreen", "sun", "protection", "spf",
            "après", "after", "sun", "sonnenmilch", "sunblock",
            "selbstbräuner", "self", "tanner", "bräunung",

            // Deodorant & perfume
            "deo", "deodorant", "antitranspirant", "roll-on",
            "parfum", "perfume", "eau", "toilette", "cologne",
            "bodyspray", "body", "mist", "duft", "fragrance",

            // Shaving & hair removal
            "rasierer", "razor", "rasierschaum", "rasiergel",
            "aftershave", "rasierwasser", "klingen", "blades",
            "epilierer", "epilator", "wachs", "wax", "strips",
            "enthaarungscreme", "hair", "removal", "cream",

            // Oral hygiene
            "zahnbürste", "toothbrush", "zahnpasta", "toothpaste",
            "zahnseide", "floss", "mundwasser", "mouthwash",
            "zahnaufhellung", "whitening", "bleaching",

            // Feminine hygiene
            "binden", "pads", "tampons", "menstruation",
            "slipeinlagen", "panty", "liners", "intim",

            // Baby care
            "babyöl", "baby", "oil", "babypuder", "powder",
            "feuchttücher", "wipes", "windeln", "diapers",
            "babycreme", "wundschutzcreme", "diaper", "rash",

            // Household chemicals
            "waschmittel", "detergent", "waschpulver", "flössig",
            "weichspüler", "softener", "fabric", "conditioner",
            "fleckenentferner", "stain", "remover", "bleiche",
            "allzweckreiniger", "all-purpose", "cleaner",
            "glasreiniger", "window", "cleaner", "fensterreiniger",
            "badreiniger", "bathroom", "cleaner", "kalklöser",
            "küchenreiniger", "kitchen", "cleaner", "fettlöser",
            "bodenreiniger", "floor", "cleaner", "wischer",

            // Paper products
            "toilettenpapier", "toilet", "paper", "klopapier",
            "küchenpapier", "kitchen", "roll", "zewa",
            "taschentücher", "tissues", "tempo", "kleenex",
            "servietten", "napkins", "papierservietten",

            // Miscellaneous
            "müllbeutel", "trash", "bags", "abfallbeutel",
            "alufolie", "aluminum", "foil", "frischhaltefolie",
            "backpapier", "baking", "paper", "pergament",
            "streichhölzer", "matches", "feuerzeug", "lighter",
            "kerzen", "candles", "teelichter", "duftkerzen"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["drogerie"] = 2.0,
    ["pflege"] = 1.5,
    ["reiniger"] = 1.5,
    ["waschmittel"] = 1.5
        };
    }
    }
