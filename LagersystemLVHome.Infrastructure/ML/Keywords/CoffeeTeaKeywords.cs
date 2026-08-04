namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for tea, coffee & hot beverages
/// </summary>
public class CoffeeTeaKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Kaffee & Tee";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Coffee - beans & powder
            "kaffee", "coffee", "kaffeebohnen", "beans",
            "espresso", "espressobohnen", "ristretto",
            "cappuccino", "latte", "macchiato", "americano",
            "arabica", "robusta", "blend", "single", "origin",
            "fair", "trade", "fairtrade", "bio", "organic",
            "ganze", "bohne", "gemahlen", "ground",

            // Coffee - preparation methods
            "filterkaffee", "filter", "handfilter", "pour", "over",
            "french", "press", "stempelkanne", "cafetière",
            "mokka", "türkisch", "greek", "ibrik", "cezve",
            "cold", "brew", "kaltextrakt", "eiskaffee",

            // Coffee - capsules & pads
            "kaffeekapseln", "kapseln", "capsules",
            "nespresso", "dolce", "gusto", "tassimo",
            "senseo", "pads", "coffee", "pods",
            "kompatibel", "compatible", "nachfüllbar",

            // Coffee - instant & soluble
            "instantkaffee", "instant", "coffee", "löslich",
            "gefriergetrocknet", "freeze-dried", "granulat",

            // Tea - varieties
            "tee", "tea", "teeblätter", "leaves",
            "schwarztee", "black", "tea", "ceylon", "assam", "darjeeling",
            "grüntee", "green", "tea", "sencha", "matcha", "gyokuro",
            "weißer", "tee", "white", "tea", "silver", "needle",
            "oolong", "wulong", "halbfermentiert",
            "pu-erh", "puerh", "roter", "tee",

            // Herbal & fruit tea
            "kräutertee", "herbal", "tea", "tisane",
            "früchtetee", "fruit", "tea", "hibiskus",
            "pfefferminz", "peppermint", "kamille", "chamomile",
            "rooibos", "rotbusch", "honeybush",
            "ingwer", "ginger", "zitrone", "lemon",

            // Specialty teas
            "chai", "masala", "chai-latte", "gewürztee",
            "earl", "grey", "bergamotte", "lady", "grey",
            "english", "breakfast", "afternoon",
            "jasmin", "jasmine", "blütentee", "flower",

            // Tea - forms
            "teebeutel", "tea", "bags", "beutel",
            "pyramide", "pyramid", "sachets",
            "loser", "tee", "loose", "leaf",
            "teeblumen", "blooming", "flowering",

            // Cocoa & chocolate
            "kakao", "cocoa", "kakaopulver", "powder",
            "trinkschokolade", "hot", "chocolate", "drinking",
            "schokopulver", "chocolate", "mix",
            "carob", "johannisbrot",

            // Other hot beverages
            "milchpulver", "milk", "powder", "cappuccino-pulver",
            "chai-pulver", "latte-mix",
            "glühwein", "mulled", "wine", "punsch", "punch",

            // Sweeteners & additives
            "zucker", "sugar", "rohrzucker", "cane",
            "honig", "honey", "ahornsirup", "maple", "syrup",
            "agavendicksaft", "agave", "stevia", "süüstoff",
            "sirup", "syrup", "flavored", "aroma",

            // Milk & cream
            "milchpulver", "milk", "powder", "kondensmilch",
            "kaffeesahne", "coffee", "creamer", "whitener",
            "kokosmilch", "coconut", "hafermilch", "oat",

            // Preparation
            "filter", "filtertüten", "paper", "filters",
            "dauerfilter", "permanent", "gold",
            "teefilter", "tea", "infuser", "sieb",
            "teekanne", "teapot", "kaffeekanne", "pot",
            "french", "press", "aeropress", "chemex",

            // Brands
            "jacobs", "dallmayr", "tchibo", "melitta",
            "lavazza", "illy", "segafredo", "julius", "meinl",
            "teekanne", "meßmer", "messmer", "ronnefeldt",
            "twinings", "ahmad", "clipper", "yogi",

            // General
            "heißgetränk", "hot", "beverage", "getränk",
            "aufguss", "infusion", "brew", "brühen"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["kaffee"] = 2.5,
            ["coffee"] = 2.5,
            ["tee"] = 2.5,
            ["tea"] = 2.5,
            ["espresso"] = 2.0,
            ["cappuccino"] = 2.0,
            ["nespresso"] = 2.0
        };
    }
}
