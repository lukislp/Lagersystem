namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for model building & hobby
/// </summary>
public class HobbyModelBuildingKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Modellbau & Hobby";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Model building - general
            "modellbau", "model", "building", "kit", "bausatz",
            "miniatur", "miniature", "maßstab", "scale",
            "detailliert", "detailed", "sammlung", "collection",

            // Aircraft
            "flugzeug", "aircraft", "airplane", "plane", "jet",
            "revell", "airfix", "tamiya", "hasegawa", "italeri",
            "propeller", "düse", "cockpit", "fahrwerk",

            // Cars & vehicles
            "modellauto", "car", "auto", "fahrzeug", "vehicle",
            "oldtimer", "classic", "rennwagen", "racing", "rally",
            "lkw", "truck", "bus", "panzer", "tank",
            "kettenfahrzeug", "military", "militär",

            // Ships
            "schiff", "ship", "boot", "boat", "segelschiff",
            "kriegsschiff", "warship", "u-boot", "submarine",
            "bismarck", "titanic", "kreuzfahrtschiff", "cruise",

            // Railway
            "eisenbahn", "train", "lok", "locomotive", "waggon",
            "schiene", "track", "gleis", "bahnhof", "station",
            "spur", "gauge", "h0", "n", "z", "tt", "märklin",

            // Figures & diorama
            "figur", "figure", "miniature", "soldat", "soldier",
            "diorama", "scenery", "landschaft", "gelände", "terrain",
            "base", "sockel", "platte", "board",

            // RC models
            "rc", "ferngesteuert", "remote", "control", "funk",
            "sender", "transmitter", "empfänger", "receiver",
            "servo", "motor", "brushless", "lipo", "akku",
            "quadcopter", "drohne", "helikopter", "helicopter",
            "buggy", "monstertruck", "crawler", "drift",

            // Colors & tools
            "modellfarbe", "paint", "acrylic", "acryl", "email",
            "pinsel", "brush", "airbrush", "spritzpistole",
            "lackierung", "finish", "primer", "grundierung",
            "verdünner", "thinner", "reiniger", "cleaner",
            "kleber", "glue", "leim", "sekundenkleber", "cyanacrylat",
            "spachtel", "putty", "filler", "schleifpapier", "sandpaper",
            "skalpell", "messer", "knife", "cutter", "zange", "nipper",

            // Decoration
            "decal", "abziehbild", "nassschiebebild", "transfer",
            "weathering", "verwitterung", "alterung", "patina",
            "pigment", "wash", "filter", "effekt", "effect",

            // Manufacturers & brands
            "tamiya", "revell", "airfix", "italeri", "academy",
            "trumpeter", "dragon", "meng", "zvezda", "hasegawa",
            "bandai", "gundam", "warhammer", "games", "workshop",

            // Crafts
            "basteln", "craft", "diy", "do-it-yourself", "handarbeit",
            "scrapbooking", "stempel", "stamp", "stanzer", "punch",
            "papier", "cardstock", "karton", "glitter", "pailletten",
            "perlen", "beads", "draht", "wire", "filz", "felt",

            // Painting & drawing
            "malen", "paint", "aquarell", "watercolor", "acryl",
            "ölfarbe", "oil", "leinwand", "canvas", "staffelei",
            "palette", "spachtel", "zeichnen", "draw", "sketch",
            "bleistift", "pencil", "kohle", "charcoal", "kreide",

            // Miscellaneous
            "hobby", "freizeit", "leisure", "sammler", "collector",
            "limited", "edition", "exklusiv", "exclusive", "selten", "rare"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["modellbau"] = 2.0,
    ["model"] = 2.0,
    ["hobby"] = 1.5,
    ["bausatz"] = 1.5,
    ["rc"] = 1.5
        };
    }
    }
