namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class FurnitureKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Möbel";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "möbel", "furniture", "einrichtung", "wohnen",
            "tisch", "table", "esstisch", "dining", "schreibtisch", "desk",
            "couchtisch", "coffee", "beistelltisch", "side",
            "stuhl", "chair", "sessel", "armchair", "hocker", "stool",
            "sofa", "couch", "ecksofa", "corner", "schlafsofa", "sleeper",
            "bett", "bed", "doppelbett", "double", "einzelbett", "single",
            "matratze", "mattress", "lattenrost", "slatted", "base",
            "schrank", "wardrobe", "kleiderschrank", "closet",
            "kommode", "chest", "drawers", "sideboard",
            "regal", "shelf", "bücherregal", "bookshelf", "wandregal",
            "vitrine", "display", "cabinet", "schublade", "drawer",
            "badezimmermöbel", "bathroom", "waschtisch", "vanity",
            "küchenmöbel", "kitchen", "küchenzeile", "küchenschrank",
            "garderobe", "coat", "rack", "flurgarderobe",
            "büromöbel", "office", "bürostuhl", "aktenschrank",
            "gartenmöbel", "garden", "outdoor", "balkonmöbel",
            "kindermöbel", "children", "wickelkommode",
            "ikea", "höffner", "poco", "roller", "mömax"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["möbel"] = 2.0,
            ["furniture"] = 2.0,
            ["einrichtung"] = 1.5
        };
    }
}
