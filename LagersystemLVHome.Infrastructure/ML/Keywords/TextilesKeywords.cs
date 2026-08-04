namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for textiles & bedding
/// </summary>
public class TextilesKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Textilien";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Bed linen
            "bettwäsche", "bedding", "bettlaken", "sheet", "spannbettlaken",
            "bettbezug", "duvet", "cover", "kopfkissenbezug", "pillowcase",
            "kissenbezug", "satin", "jersey", "flanell", "biber",

            // Duvets & pillows
            "bettdecke", "duvet", "steppdecke", "daunendecke", "down",
            "kissen", "pillow", "kopfkissen", "nackenstützkissen",
            "seitenschläferkissen", "memory", "foam", "latex",

            // Towels
            "handtuch", "towel", "badetuch", "duschtuch",
            "handtücher", "towels", "gästehandtuch", "waschlappen",
            "bademantel", "bathrobe", "robe", "morgenmantel",
            "frottee", "terry", "cloth", "bambus", "bamboo",

            // Table linen
            "tischdecke", "tablecloth", "tischläufer", "runner",
            "platzdeckchen", "placemat", "stoffservietten", "napkins",

            // Curtains & drapes
            "gardine", "curtain", "vorhang", "drape", "scheibengardine",
            "raffrollo", "roman", "shade", "schiebegardine",
            "gardinenstange", "rod", "pole", "Ösen", "schlaufen",

            // Blankets & throws
            "decke", "blanket", "wohndecke", "throw", "kuscheldecke",
            "plaid", "fleece", "fleecedecke", "strickdecke",
            "tagesdecke", "bedspread", "quilt", "patchwork",

            // Mattress protection
            "matratzenschoner", "mattress", "protector", "topper",
            "unterbett", "pad", "auflage", "molton",

            // Fabrics
            "stoff", "fabric", "meterware", "textil", "textile",
            "baumwolle", "cotton", "leinen", "linen", "seide", "silk",
            "polyester", "mikrofaser", "microfiber", "samt", "velvet",

            // Sewing accessories
            "nähgarn", "thread", "zwirn", "nähnadeln", "needles",
            "knöpfe", "buttons", "reißverschluss", "zipper", "zip",
            "schere", "scissors", "nähmaschine", "sewing", "machine",

            // Home textiles
            "sofakissen", "cushion", "dekokissen", "zierkissen",
            "sitzkissen", "seat", "pad", "stuhlkissen",
            "auflagen", "cushions", "polster", "padding",

            // Rugs & mats
            "teppich", "carpet", "rug", "läufer", "runner",
            "fußmatte", "doormat", "badematte", "bath", "mat",
            "vorleger", "rug", "teppichboden", "carpeting",

            // Miscellaneous
            "textilien", "textiles", "heimtextilien", "home",
            "wäsche", "laundry", "linen", "haushalt", "household"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["textilien"] = 2.0,
    ["bettwäsche"] = 1.5,
    ["handtuch"] = 1.5,
    ["decke"] = 1.5
        };
    }
    }
