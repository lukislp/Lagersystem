namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for automotive (car & motorcycle)
/// </summary>
public class AutomotiveKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Automotive";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Vehicle basics
            "auto", "car", "fahrzeug", "vehicle", "kfz", "pkw",
            "motorrad", "motorcycle", "bike", "roller", "scooter",

            // Engine & drivetrain
            "motor", "engine", "getriebe", "transmission", "kupplung",
            "öl", "oil", "motoröl", "getriebeöl", "filter", "ölfilter",
            "zündkerze", "spark", "plug", "kerze", "glühkerze",
            "luftfilter", "kraftstofffilter", "fuel",

            // Tires & wheels
            "reifen", "tire", "pneu", "rad", "wheel", "felge",
            "sommer", "winter", "ganzjahres", "allseason",
            "schneekette", "chain", "reifenwechsel",

            // Brakes
            "bremse", "brake", "bremsbelag", "bremsscheibe", "bremsflössigkeit",

            // Lighting
            "lampe", "lamp", "scheinwerfer", "headlight", "rücklicht",
            "blinker", "birne", "bulb", "led", "xenon", "halogen",

            // Battery & electrics
            "batterie", "battery", "akku", "starterbatterie",
            "lichtmaschine", "alternator", "anlasser", "starter",

            // Bodywork
            "stoßstange", "bumper", "kotflügel", "fender",
            "spiegel", "mirror", "außenspiegel", "scheibe", "windschutzscheibe",
            "scheibenwischer", "wiper", "blade", "wischerblatt",

            // Interior
            "sitzbezug", "seat", "cover", "fußmatte", "mat",
            "lenkrad", "steering", "wheel", "lenkradbezug",

            // Care & maintenance
            "autopflege", "car", "care", "wash", "wachs", "polish",
            "reiniger", "cleaner", "shampoo", "politur",
            "cockpitspray", "lederpflege", "leather",

            // Tools & accessories
            "wagenheber", "jack", "abschleppseil", "tow", "rope",
            "starthilfe", "jumper", "cable", "warndreick",
            "verbandskasten", "first", "aid", "warnweste",

            // Electronics
            "navi", "navigation", "gps", "dashcam", "kamera",
            "radio", "autoradio", "freisprecheinrichtung", "bluetooth",
            "adapter", "lader", "charger", "usb", "halterung",
            "obd", "diagnosegerät", "diagnostic",

            // Tuning & performance
            "chiptuning", "tuning", "sportauspuff", "auspuff", "exhaust",
            "luftfilter", "air", "intake", "spoiler",

            // Brands & manufacturers
            "bosch", "mann", "filter", "castrol", "shell", "mobil",
            "sonax", "würth", "liqui", "moly",

            // Miscellaneous
            "automotive", "zubehör", "accessory", "ersatzteil", "spare", "part"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["auto"] = 2.0,
    ["car"] = 2.0,
    ["automotive"] = 2.0,
    ["kfz"] = 1.5,
    ["motorrad"] = 1.5
        };
    }
    }
