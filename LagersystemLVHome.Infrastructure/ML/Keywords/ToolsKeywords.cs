namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class ToolsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Werkzeug";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Hand tools
            "hammer", "schraubendreher", "screwdriver", "zange", "pliers", "säge", "saw",
            "bohrer", "drill", "bohrhammer", "akkuschrauber", "cordless",
            "bohrmaschine", "drilling", "schleifer", "grinder", "winkelschleifer",
            "stichsäge", "jigsaw", "kreissäge", "circular",

            // CNC milling tools
            "fräser", "schaftfräser", "end mill", "endmill", "cutter",
            "walzenfräser", "slot cutter", "nutfräser", "t-nut",
            "kugelfräser", "ball nose", "radiusfräser", "torusfräser",
            "stirnfräser", "face mill", "planfräser", "facing",
            "eckradiusfräser", "corner radius", "anfasfräser", "chamfer",
            "gewindefräser", "thread mill", "gewinde", "threading",
            "tauchfräser", "plunge cutter", "bohrstangenfräser",
            "scheibenfräser", "disc cutter", "winkelfräser", "angle cutter",
            "schaftfräser", "shank mill", "vollhartmetall", "carbide",
            "hss", "high speed steel", "schnellarbeitsstahl",
            "beschichtet", "coated", "tin", "tialn", "altin",
            "2-schneider", "2-flute", "3-schneider", "4-schneider",
            "6-schneider", "multi-flute", "langschaftfräser", "long reach",
            "kurz", "short", "extra lang", "extra long",
            "cnc", "cnc-fräse", "cnc mill", "milling machine",
            "spannzange", "collet", "er11", "er16", "er20", "er25", "er32",
            "werkzeughalter", "tool holder", "sk30", "sk40", "hsk",
            "fräskopf", "milling head", "spindel", "spindle",
            "vorschub", "feed rate", "drehzahl", "rpm", "rotation",
            "kühlmittel", "coolant", "schmiermittel", "lubricant",
            "schneidöl", "cutting oil", "fräsöl", "milling oil",

            // Brands
            "makita", "bosch", "dewalt", "milwaukee", "metabo", "hikoki",
            "einhell", "ryobi", "festool", "hilti", "stanley",
            "gühring", "garant", "titex", "dormer", "walter",
            "sandvik", "kennametal", "kyocera", "seco", "iscar",

            // Accessories
            "wasserwaage", "level", "zollstock", "maßband", "tape", "measure",
            "multimeter", "messgerät", "voltage", "tester",
            "schrauben", "screw", "nagel", "nail", "dübel", "anchor",
            "silikon", "acryl", "kartusche", "montagekleber",
            "spaten", "schaufel", "harke", "gartenschere", "rasenmäher",
            "leiter", "ladder", "stehleiter", "handschuhe", "schutzbrille",
            "werkzeug", "tool", "werkbank", "koffer"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["werkzeug"] = 2.0,
    ["tool"] = 2.0,
            // Hand tools
    ["bosch"] = 1.5,
    ["makita"] = 1.5,

            // Weight CNC tools higher
    ["fräser"] = 1.8,
    ["schaftfräser"] = 1.8,
    ["end mill"] = 1.8,
    ["cnc"] = 1.7,
    ["vollhartmetall"] = 1.5,
    ["carbide"] = 1.5,
    ["hss"] = 1.4,
    ["schneider"] = 1.3,
    ["collet"] = 1.2,
    ["spannzange"] = 1.2
        };
    }
    }
