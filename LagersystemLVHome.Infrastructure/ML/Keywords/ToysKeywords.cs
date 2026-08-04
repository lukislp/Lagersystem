namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class ToysKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Spielzeug";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "spielzeug", "toy", "toys", "spiel", "game", "spielen",
            "kinder", "kids", "children", "baby", "kleinkind",
            "puppe", "doll", "barbie", "teddy", "plösch", "plush",
            "auto", "car", "fahrzeug", "lkw", "truck",
            "ball", "fußball", "basketball",
            "lego", "duplo", "bausteine", "building", "blocks",
            "playmobil", "figuren", "figures",
            "brettspiel", "board", "game", "gesellschaftsspiel",
            "kartenspiel", "cards", "uno", "monopoly",
            "puzzle", "puzzel", "teile", "pieces",
            "actionfigur", "action", "figure", "marvel", "superheld",
            "nerf", "blaster", "dart",
            "malen", "paint", "basteln", "craft", "knete", "play-doh",
            "sandkasten", "sandbox", "schaukel", "rutsche", "roller",
            "ferngesteuert", "remote", "control", "drohne", "drone",
            "kuscheltier", "stofftier"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["spielzeug"] = 2.0,
    ["toy"] = 2.0,
    ["lego"] = 1.5,
    ["playmobil"] = 1.5
        };
    }
    }
