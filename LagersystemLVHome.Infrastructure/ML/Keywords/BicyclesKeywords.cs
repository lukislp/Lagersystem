namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for bicycles & e-bikes
/// </summary>
public class BicyclesKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Fahrräder & E-Bikes";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Bicycle types
            "fahrrad", "bike", "bicycle", "rad",
            "mountainbike", "mtb", "mountain", "geländerad",
            "rennrad", "road", "bike", "racing",
            "trekkingrad", "trekking", "touring", "reiserad",
            "cityrad", "city", "bike", "stadtrad",
            "hollandrad", "dutch", "bike", "citycruiser",
            "crossbike", "cross", "fitnessbike", "fitness",
            "bmx", "freestyle", "dirt", "jump",
            "klapprad", "faltrad", "folding", "bike", "brompton",

            // E-bikes & pedelecs
            "e-bike", "ebike", "pedelec", "elektrofahrrad",
            "elektrisch", "electric", "motor", "akku",
            "mittelmotorpedelec", "mittelmotivierung", "bosch", "shimano", "brose",
            "speed-pedelec", "s-pedelec", "45", "kmh",
            "frontmotor", "heckmotor", "nabenmotor", "hub",
            "antrieb", "drive", "system",

            // Kids & teens
            "kinderfahrrad", "kinderrad", "kids", "bike",
            "laufrad", "balance", "bike", "draisine",
            "jugendrad", "youth", "bike", "teenager",
            "12", "zoll", "14", "16", "18", "20", "24",

            // Frame & sizes
            "rahmen", "frame", "aluminium", "alu", "carbon",
            "stahl", "steel", "titan", "titanium",
            "rahmengrße", "size", "zoll", "inch", "cm",
            "28", "zoll", "26", "29", "27.5",
            "damen", "herren", "unisex", "trapez", "wave",

            // Gearing & drivetrain
            "schaltung", "gears", "gangschaltung",
            "kettenschaltung", "derailleur", "umwerfer",
            "nabenschaltung", "hub", "gears", "nexus", "alfine",
            "shimano", "sram", "campagnolo", "eagle",
            "kette", "chain", "kassette", "cassette",
            "zahnrad", "sprocket", "kettenblatt", "chainring",
            "kurbel", "crank", "tretlager", "bottom", "bracket",

            // Brakes
            "bremse", "brake", "bremsen", "braking",
            "scheibenbremse", "disc", "brake", "hydraulisch",
            "felgenbremse", "rim", "brake", "v-brake",
            "rücktrittbremse", "coaster", "brake",

            // Wheels & tires
            "laufrad", "wheel", "felge", "rim",
            "reifen", "tire", "tyre", "mantel",
            "schlauch", "tube", "tubeless", "schlauchlos",
            "speiche", "spoke", "nabe", "hub",
            "schnellspanner", "quick", "release", "steckachse",

            // Suspension
            "federung", "suspension", "dämpfer", "shock",
            "federgabel", "fork", "luftfederung", "air",
            "stahlfederung", "coil", "fully", "fullsuspension",
            "hardtail", "ungefedert",

            // Handlebars & grips
            "lenker", "handlebar", "riser", "flat",
            "rennlenker", "drop", "bar", "bullhorn",
            "griffe", "grips", "lenkerband", "bar", "tape",
            "vorbau", "stem", "ahead", "gewinde",

            // Saddle & seatpost
            "sattel", "saddle", "seat", "selle",
            "sattelstütze", "seatpost", "teleskop", "dropper",
            "gefedert", "suspended", "ungefedert",

            // Pedals
            "pedal", "pedale", "pedals", "klickpedal",
            "flat", "pedal", "plattform", "platform",
            "kombipedal", "dual", "purpose",

            // Lighting
            "licht", "light", "beleuchtung", "lighting",
            "frontlicht", "rücklicht", "dynamo", "nabendynamo",
            "led", "akku", "batterie", "usb",

            // Accessories
            "fahrradtasche", "bag", "packtasche", "pannier",
            "korb", "basket", "gepäckträger", "rack", "carrier",
            "schloss", "lock", "bügelschloss", "u-lock",
            "kette", "chain", "faltschloss", "folding",
            "helm", "helmet", "fahrradhelm",
            "pumpe", "pump", "standpumpe", "minipumpe",
            "werkzeug", "tool", "multitool", "flickzeug",
            "trinkflasche", "water", "bottle", "halter",
            "computer", "tacho", "tachometer", "gps",
            "schutzblech", "fender", "spritzschutz",
            "ständer", "kickstand", "seitenständer",

            // Brands
            "cube", "trek", "giant", "scott", "specialized",
            "canyon", "focus", "bulls", "ktm", "haibike",
            "kalkhoff", "pegasus", "gazelle", "riese", "müller",
            "bosch", "shimano", "yamaha", "brose", "bafang",

            // General
            "cycling", "radfahren", "radtour", "tour",
            "mountainbiking", "downhill", "enduro", "trail"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["fahrrad"] = 2.5,
            ["bike"] = 2.5,
            ["e-bike"] = 3.0,
            ["ebike"] = 3.0,
            ["pedelec"] = 2.5,
            ["mountainbike"] = 2.0,
            ["rennrad"] = 2.0
        };
    }
}
