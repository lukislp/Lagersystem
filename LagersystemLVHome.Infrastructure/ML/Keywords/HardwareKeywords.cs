namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class HardwareKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Baumarkt";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "baumarkt", "hardware", "store", "bau", "construction",
            "baustoffe", "building", "material", "baustoff",
            "holz", "wood", "brett", "board", "balken", "beam",
            "stein", "brick", "ziegel", "beton", "concrete",
            "zement", "cement", "mörtel", "mortar", "putz",
            "fliese", "tile", "kachel", "bodenfliese",
            "laminat", "laminate", "parkett", "vinyl", "bodenbelag",
            "teppich", "carpet", "teppichboden",
            "farbe", "paint", "lack", "lacquer", "lasur",
            "tapete", "wallpaper", "rolle", "roller", "pinsel", "brush",
            "sanitär", "plumbing", "rohr", "pipe", "fitting",
            "wasserhahn", "faucet", "tap", "armatur",
            "toilette", "toilet", "wc", "waschbecken", "sink",
            "elektro", "electrical", "kabel", "cable", "steckdose",
            "schalter", "switch", "lampe", "lamp", "leuchte",
            "tür", "door", "fenster", "window", "griff", "handle",
            "schloss", "lock", "scharnier", "hinge",
            "dämmung", "insulation", "dämmmaterial", "styropor",
            "dach", "roof", "dachziegel", "dachrinne"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["baumarkt"] = 2.0,
    ["hardware"] = 2.0,
    ["bau"] = 1.5
        };
    }
    }
