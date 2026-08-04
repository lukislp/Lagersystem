namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class JewelryKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Schmuck & Uhren";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "schmuck", "jewelry", "jewellery", "accessoire",
            "kette", "necklace", "halskette", "chain",
            "ring", "ehering", "wedding", "verlobungsring",
            "armband", "bracelet", "armreif", "bangle",
            "ohrringe", "earrings", "creolen", "stecker",
            "anhänger", "pendant", "charm", "medaillon",
            "brosche", "brooch", "anstecknadel", "pin",
            "uhr", "watch", "armbanduhr", "wristwatch",
            "smartwatch", "fitness", "tracker",
            "wanduhr", "wall", "clock", "wecker", "alarm",
            "gold", "silber", "silver", "platin", "platinum",
            "edelstein", "gemstone", "diamant", "diamond",
            "perle", "pearl", "kristall", "crystal",
            "edelstahl", "stainless", "steel", "titan", "titanium",
            "schmuckkästchen", "jewelry", "box", "aufbewahrung",
            "reinigung", "cleaning", "pflege", "polish"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["schmuck"] = 2.0,
            ["jewelry"] = 2.0,
            ["uhr"] = 1.5,
            ["watch"] = 1.5
        };
    }
}
