namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class PetsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Haustiere";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "haustier", "pet", "pets", "tier", "animal",
            "hund", "dog", "welpe", "puppy", "hundefutter",
            "katze", "cat", "kitten", "katzenfutter", "katzenstreu",
            "vogel", "bird", "käfig", "cage", "vogelhaus",
            "fisch", "fish", "aquarium", "aquaristik",
            "nager", "hamster", "meerschweinchen", "guinea", "kaninchen",
            "futter", "food", "nassfutter", "trockenfutter",
            "leckerli", "treats", "snack", "knochen", "bone",
            "leine", "leash", "halsband", "collar", "geschirr", "harness",
            "körbchen", "bed", "decke", "blanket", "kissen",
            "spielzeug", "toy", "ball", "tau", "rope",
            "kratzbaum", "scratching", "post", "katzentoilette",
            "napf", "bowl", "trinkbrunnen", "fountain",
            "bürste", "brush", "kamm", "comb", "pflege", "grooming",
            "shampoo", "floh", "flea", "zecke", "tick",
            "transportbox", "carrier", "hundebox"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["haustier"] = 2.0,
    ["pet"] = 2.0,
    ["hund"] = 1.5,
    ["katze"] = 1.5
        };
    }
    }
