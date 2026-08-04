namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for nutritional supplements
/// </summary>
public class SupplementsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Nahrungsergänzung";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Basic supplements
            "nahrungsergänzung", "supplement", "supplements", "nutrition",
            "vitamin", "vitamine", "vitamins", "mineralstoffe", "minerals",
            "multivitamin", "multi", "komplex", "complex",

            // Vitamins
            "vitamin", "a", "b", "c", "d", "e", "k",
            "b1", "b2", "b6", "b12", "thiamin", "riboflavin",
            "niacin", "folsäure", "folic", "acid", "biotin",
            "ascorbinsäure", "ascorbic", "acid",
            "tocopherol", "retinol", "calciferol",

            // Minerals
            "calcium", "kalzium", "magnesium", "zink", "zinc",
            "eisen", "iron", "selen", "selenium", "jod", "iodine",
            "kalium", "potassium", "natrium", "sodium",
            "chrom", "chromium", "kupfer", "copper", "mangan",

            // Omega & fatty acids
            "omega", "3", "6", "9", "fettsäure", "fatty", "acid",
            "fischöl", "fish", "oil", "algenöl", "algae",
            "leinöl", "flaxseed", "krillöl", "krill",
            "dha", "epa", "ala",

            // Proteins & aminos
            "protein", "proteinpulver", "whey", "casein", "isolate",
            "aminosäure", "amino", "acid", "bcaa", "eaa",
            "glutamin", "glutamine", "arginin", "arginine",
            "kreatin", "creatine", "monohydrat", "monohydrate",

            // Sports & fitness
            "pre-workout", "preworkout", "booster", "pump",
            "post-workout", "recovery", "regeneration",
            "gainer", "mass", "weight", "muskelaufbau",
            "fatburner", "fettverbrennung", "thermogenic",
            "carb", "blocker", "kohlenhydrat",

            // Health
            "probiotika", "probiotics", "präbiotika", "prebiotics",
            "darmbakterien", "lactobacillus", "bifidobacterium",
            "enzyme", "verdauungsenzym", "digestive",
            "collagen", "kollagen", "gelenkpulver", "joint",
            "glucosamin", "glucosamine", "chondroitin", "msm",

            // Plant-based
            "extrakt", "extract", "pulver", "powder", "kapsel", "capsule",
            "tablette", "tablet", "tropfen", "drops", "flössig", "liquid",
            "kurkuma", "turmeric", "ingwer", "ginger", "ashwagandha",
            "maca", "spirulina", "chlorella", "ginkgo", "ginseng",

            // Specialty
            "melatonin", "schlaf", "sleep", "5-htp", "tryptophan",
            "coenzym", "q10", "ubiquinol", "ubichinon",
            "resveratrol", "antioxidant", "antioxidantien",
            "adaptogen", "nootropic", "nootropikum",

            // Shapes
            "kapseln", "capsules", "tabletten", "tablets", "pills",
            "softgel", "weichkapsel", "dragee", "lutschtablette",
            "brausetablette", "effervescent", "sprudel",
            "sirup", "syrup", "gummibärchen", "gummies",
            "riegel", "bar", "shake", "drink",

            // Properties
            "bio", "organic", "vegan", "vegetarisch", "vegetarian",
            "glutenfrei", "gluten", "free", "laktosefrei", "lactose",
            "zuckerfrei", "sugar", "gmo", "gentechnikfrei",
            "natürlich", "natural", "rein", "pure", "hochdosiert"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["nahrungsergänzung"] = 2.0,
            ["supplement"] = 2.0,
            ["vitamin"] = 1.5,
            ["protein"] = 1.5,
            ["omega"] = 1.5
        };
    }
}
