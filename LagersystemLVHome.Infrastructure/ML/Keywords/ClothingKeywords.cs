namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class ClothingKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Kleidung";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "shirt", "tshirt", "hemd", "bluse", "top", "polo",
            "pullover", "sweater", "sweatshirt", "strick",
            "hoodie", "kapuze", "kapuzenpullover", "zip",
            "jacke", "jacket", "mantel", "coat", "blazer", "parka", "winter",
            "hose", "pants", "jeans", "denim", "chino", "cargo",
            "shorts", "leggings", "jogginghose",
            "rock", "skirt", "kleid", "dress", "abendkleid",
            "schuhe", "shoes", "sneaker", "turnschuhe", "sportschuhe",
            "boots", "stiefel", "sandalen", "pumps", "heels",
            "socken", "socks", "strümpfe", "unterwäsche", "underwear",
            "sportbekleidung", "sport", "training", "fitness", "lauf",
            "gürtel", "belt", "schal", "scarf", "mütze", "cap", "beanie",
            "adidas", "nike", "puma", "under", "armour", "levi",
            "zara", "hm", "tommy", "calvin", "klein",
            "kleidung", "clothing", "mode", "fashion", "textil"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["kleidung"] = 2.0,
    ["clothing"] = 2.0,
    ["mode"] = 1.5
        };
    }
    }
