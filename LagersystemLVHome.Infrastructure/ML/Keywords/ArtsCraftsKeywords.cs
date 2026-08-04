namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for arts & crafts
/// </summary>
public class ArtsCraftsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Kunst & Basteln";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Painting & drawing
            "farbe", "farben", "paint", "painting", "malen",
            "acrylfarbe", "acrylic", "acryl", "wasserfarbe", "aquarell",
            "ölfarbe", "oil", "paint", "gouache", "tempera",
            "pinsel", "brush", "borstenpinsel", "haarpinsel",
            "palette", "mischpalette", "mixing", "spachtel",
            "leinwand", "canvas", "keilrahmen", "stretched",
            "staffelei", "easel", "tischstaffelei", "standstaffelei",

            // Drawing
            "zeichnen", "drawing", "sketch", "skizze",
            "bleistift", "pencil", "graphit", "graphite",
            "buntstift", "colored", "pencil", "farbstift",
            "kohle", "charcoal", "kohlestifte", "zeichenkohle",
            "kreide", "chalk", "pastellkreide", "pastell",
            "marker", "filzstift", "fasermaler", "fineliner",
            "tusche", "ink", "indian", "china", "tinte",

            // Paper & surfaces
            "zeichenpapier", "drawing", "paper", "skizzenpapier",
            "aquarellpapier", "watercolor", "paper",
            "bristolkarton", "bristol", "board",
            "leinenpapier", "cotton", "paper", "bütten",
            "skizzenbuch", "sketchbook", "zeichenblock",

            // Crafting & handiwork
            "basteln", "crafting", "craft", "diy",
            "schere", "scissors", "bastelschere", "craft",
            "kleber", "glue", "bastelkleber", "heißkleber",
            "klebeband", "tape", "doppelseitiges", "washi",

            // Paper & cardboard
            "tonpapier", "construction", "paper", "farbig",
            "tonkarton", "cardstock", "bastelpapier",
            "transparentpapier", "tracing", "pauspapier",
            "seidenpapier", "tissue", "paper",
            "krepppapier", "crepe", "paper",

            // Scrapbooking
            "scrapbooking", "scrapbook", "album",
            "stempel", "stamp", "stempelkissen", "ink", "pad",
            "stanzer", "punch", "motivstanzer", "border",
            "embossing", "prägung", "prägestempel",

            // Beads & jewelry
            "perlen", "beads", "glasperlen", "holzperlen",
            "rocailles", "seed", "beads", "delica",
            "schmuckdraht", "wire", "jewelry", "draht",
            "verschluss", "clasp", "karabiner", "lobster",
            "biegeringe", "jump", "rings", "Ösen",

            // Felting & textile
            "filz", "felt", "filzen", "felting",
            "wolle", "wool", "filzwolle", "märchenwolle",
            "filznadel", "felting", "needle", "nadelfilzen",
            "stoff", "fabric", "textil", "textile",

            // Modeling & shaping
            "modelliermasse", "modeling", "clay", "ton",
            "fimo", "polymer", "clay", "lufttrocknend",
            "knete", "play-doh", "plastilin",
            "gips", "plaster", "gipsbinden", "bandage",

            // Miscellaneous material
            "glitter", "glitzer", "pailletten", "sequins",
            "federn", "feathers", "pompoms", "wackelaugen",
            "pfeifenreiniger", "pipe", "cleaners", "chenille",
            "holzstäbchen", "wooden", "sticks", "eisstiele",

            // Tools
            "cutter", "bastelmesser", "schneidematte",
            "cutting", "mat", "lineal", "ruler",
            "zirkel", "compass", "schablone", "template",

            // Brands
            "faber-castell", "staedtler", "copic", "prismacolor",
            "derwent", "caran", "dache", "koh-i-noor",
            "winsor", "newton", "schmincke", "liquitex",

            // General
            "kunst", "art", "kreativ", "creative",
            "bastelbedarf", "craft", "supplies", "künstlerbedarf"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["kunst"] = 2.0,
            ["basteln"] = 2.0,
            ["malen"] = 2.0,
            ["craft"] = 2.0,
            ["farbe"] = 1.5,
            ["pinsel"] = 1.5
        };
    }
}
