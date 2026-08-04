namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for sports & fitness
/// </summary>
public class SportsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Sport";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // General
            "sport", "fitness", "training", "workout", "gym",
            "übung", "exercise", "bewegung", "athletic",

            // Clothing
            "sportbekleidung", "sportswear", "trikot", "jersey",
            "laufshirt", "running", "shirt", "laufhose", "shorts",
            "leggings", "tights", "compression", "kompression",
            "funktionsshirt", "function", "quick", "dry", "atmungsaktiv",

            // Shoes
            "laufschuhe", "running", "shoes", "trainingsschuhe", "sneaker",
            "fußballschuhe", "football", "boots", "stollen",
            "hallenschuhe", "indoor", "wanderschuhe", "hiking",

            // Fitness equipment
            "hantel", "dumbbell", "kettlebell", "gewicht", "weight",
            "langhantel", "barbell", "scheibe", "plate",
            "matte", "mat", "yogamatte", "fitness", "gymnastikmatte",
            "rolle", "foam", "roller", "massage",
            "springseil", "jump", "rope", "skip",
            "ball", "medizinball", "medicine", "gymnastikball",
            "band", "resistance", "theraband", "gummiband",
            "bank", "bench", "hantelbank", "weight",

            // Cardio
            "laufband", "treadmill", "ergometer", "heimtrainer",
            "crosstrainer", "elliptical", "rudergerät", "rowing",
            "stepper", "spin", "bike", "indoor", "cycling",

            // Outdoor
            "fahrrad", "bicycle", "bike", "mountainbike", "rennrad",
            "wandern", "hiking", "trekking", "outdoor",
            "klettern", "climbing", "karabiner", "seil", "rope",
            "camping", "zelt", "tent", "schlafsack", "sleeping",

            // Ball sports
            "fußball", "football", "soccer", "ball",
            "basketball", "korb", "hoop",
            "volleyball", "tennis", "schläger", "racket",
            "tischtennis", "ping", "pong", "paddle",

            // Water sports
            "schwimmen", "swimming", "schwimmbrille", "goggles",
            "badekappe", "cap", "schwimmflossen", "fins",
            "schnorchel", "snorkel", "tauchen", "diving",
            "surfbrett", "surfboard", "sup", "paddle", "board",

            // Winter sports
            "ski", "snowboard", "schlitten", "sled",
            "schlittschuhe", "ice", "skates",

            // Electronics & tracking
            "smartwatch", "fitness", "tracker", "pulsmesser",
            "heart", "rate", "monitor", "schrittzähler", "pedometer",
            "sportuhr", "watch", "polar", "garmin",

            // Brands
            "adidas", "nike", "puma", "under", "armour",
            "reebok", "asics", "new", "balance", "salomon",
            "decathlon", "domyos", "kipsta"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["sport"] = 2.0,
    ["fitness"] = 2.0,
    ["training"] = 1.5,
    ["workout"] = 1.5
        };
    }
    }
