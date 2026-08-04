namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class HealthKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Gesundheit";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "vitamin", "vitamine", "supplements", "nahrungsergänzung",
            "medikament", "medicine", "tablette", "tablet",
            "schmerzmittel", "ibuprofen", "paracetamol", "aspirin",
            "erkältung", "cold", "husten", "schnupfen",
            "pflaster", "bandage", "verband", "desinfektionsmittel",
            "shampoo", "duschgel", "shower", "seife", "soap",
            "creme", "cream", "lotion", "deo", "deodorant",
            "zahnbürste", "toothbrush", "zahnpasta", "toothpaste",
            "zahnseide", "dental", "floss", "mundwasser", "mouthwash",
            "rasierer", "razor", "rasier", "shaving",
            "kosmetik", "cosmetics", "makeup", "make-up",
            "tampons", "binden", "windeln", "diapers",
            "toilettenpapier", "toilet", "paper", "taschentücher",
            "thermometer", "blutdruck", "gesundheit", "health", "pflege", "care"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["gesundheit"] = 2.0,
    ["health"] = 2.0,
    ["medikament"] = 1.5,
    ["pflege"] = 1.5
        };
    }
    }
