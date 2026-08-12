namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class HouseholdKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Haushalt";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "geschirr", "dishes", "teller", "plate", "tasse", "cup", "mug",
            "schüssel", "bowl", "glas", "glass", "weinglas", "becher",
            "besteck", "cutlery", "messer", "knife", "gabel", "fork", "löffel", "spoon",
            "topf", "pot", "kochtopf", "pfanne", "pan", "bratpfanne", "wok",
            "schneebesen", "whisk", "schneidebrett", "cutting", "board",
            "wasserkocher", "kettle", "toaster", "kaffeemaschine", "coffee",
            "mixer", "standmixer", "blender", "küchenmaschine",
            "vorratsdose", "container", "tupperware", "müllbeutel", "mülleimer",
            "schwamm", "sponge", "spülmittel", "dish", "soap",
            "staubsauger", "vacuum", "wischmopp", "mop", "putzen", "cleaning",
            "tisch", "table", "stuhl", "chair", "regal", "shelf",
            "schrank", "cupboard", "kommode", "sofa", "couch", "sessel",
            "vase", "kerze", "candle", "bilderrahmen", "kissen", "decke",
            "haushalt", "household", "küche", "kitchen", "wohnen", "living"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["haushalt"] = 2.0,
            ["household"] = 2.0,
            ["küche"] = 1.5,
            ["kitchen"] = 1.5
        };
    }
}
