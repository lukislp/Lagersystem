namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class GardenKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Garten";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "garten", "garden", "outdoor", "pflanzen", "plant",
            "spaten", "schaufel", "harke", "rechen", "gartenschere",
            "rasenmäher", "mower", "rasen", "lawn", "trimmer",
            "hecke", "hedge", "heckenschere",
            "gießkanne", "watering", "can", "schlauch", "hose",
            "sprinkler", "bewässerung", "irrigation",
            "blumentopf", "pot", "pflanzkasten", "kübel",
            "erde", "soil", "kompost", "dünger", "fertilizer",
            "samen", "seed", "saat", "zwiebel", "bulb",
            "grill", "bbq", "barbecue", "kohle", "charcoal",
            "gartenmöbel", "furniture", "tisch", "stuhl", "liege",
            "sonnenschirm", "parasol", "pavillon", "gazebo",
            "solarleuchte", "solar", "light", "gartenbeleuchtung",
            "teich", "pond", "pool", "schwimmbecken",
            "gewächshaus", "greenhouse", "treibhaus"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["garten"] = 2.0,
    ["garden"] = 2.0
        };
    }
    }
