namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class OutdoorKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Camping & Outdoor";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "camping", "outdoor", "camp", "zelten", "trekking",
            "zelt", "tent", "igluzelt", "kuppelzelt", "tunnelzelt",
            "schlafsack", "sleeping", "bag", "mumienschlafsack",
            "isomatte", "sleeping", "pad", "luftmatratze", "mat",
            "rucksack", "backpack", "trekkingrucksack", "wanderrucksack",
            "kocher", "stove", "campingkocher", "gaskocher",
            "kochgeschirr", "cookware", "topf", "pot", "pfanne",
            "kühlbox", "cooler", "cooling", "kühltasche",
            "taschenlampe", "flashlight", "stirnlampe", "headlamp",
            "laterne", "lantern", "campinglampe",
            "kompass", "compass", "gps", "navigation",
            "karte", "map", "wanderkarte", "outdoor",
            "messer", "knife", "taschenmesser", "multitool",
            "seil", "rope", "paracord", "karabiner", "carabiner",
            "wasserflasche", "water", "bottle", "trinkflasche",
            "wasserfilter", "water", "filter", "reinigung",
            "survival", "überlebensausrüstung", "notfall", "emergency",
            "anzünder", "lighter", "feuerzeug", "streichholz",
            "hängematte", "hammock", "campingstuhl", "chair",
            "campingtisch", "table", "falttisch"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["camping"] = 2.0,
    ["outdoor"] = 2.0,
    ["zelt"] = 1.5,
    ["tent"] = 1.5
        };
    }
    }
