namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class BabyKidsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Baby & Kind";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "baby", "babies", "kind", "kinder", "kids", "children",
            "windeln", "diapers", "windel", "pampers", "höschenwindel",
            "feuchttücher", "wipes", "babypflege", "care",
            "babynahrung", "milk", "milch", "flasche", "bottle",
            "schnuller", "pacifier", "dummy", "nuckel",
            "brei", "food", "beikost", "gläschen",
            "babykleidung", "clothing", "strampler", "romper",
            "body", "lätzchen", "bib", "mütze", "söckchen",
            "kinderwagen", "stroller", "buggy", "pram",
            "autositz", "car", "seat", "babyschale",
            "wickeltasche", "bag", "wickelauflage",
            "bett", "bettchen", "crib", "wiege", "cradle",
            "spielzeug", "toy", "rassel", "greifling", "mobile",
            "babybadewanne", "bath", "tub", "badethermometer",
            "hochstuhl", "high", "chair", "tripp", "trapp",
            "laufgitter", "playpen", "laufstall",
            "tragetuch", "sling", "carrier", "babytrage",
            "stillkissen", "nursing", "pillow"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["baby"] = 2.0,
    ["kind"] = 2.0,
    ["kinder"] = 1.5
        };
    }
    }
