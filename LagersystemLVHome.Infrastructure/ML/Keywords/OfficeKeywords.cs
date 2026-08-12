namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for office supplies & stationery
/// </summary>
public class OfficeKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Bürobedarf";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Stationery
            "stift", "pen", "kugelschreiber", "ballpoint", "füller", "fountain",
            "bleistift", "pencil", "druckbleistift", "mechanical",
            "fineliner", "fasermaler", "filzstift", "marker", "edding",
            "textmarker", "highlighter", "leuchtmarker", "stabilo",

            // Paper
            "papier", "paper", "kopierpapier", "druckerpapier", "a4", "a3",
            "block", "notizblock", "notebook", "collegeblock", "ringbuch",
            "heft", "schulheft", "kariert", "liniert", "blanko",

            // Organization
            "ordner", "folder", "aktenordner", "ringordner", "lever", "arch",
            "hefter", "schnellhefter", "binder", "mappe",
            "locher", "hole", "punch", "perforator", "lochzange",
            "tacker", "stapler", "heftgerät", "klammern", "staples",
            "büroklammer", "clip", "paper", "foldback",

            // Glue & correction
            "klebeband", "tape", "tesa", "scotch", "kleber", "glue",
            "klebestift", "stick", "sekundenkleber", "super",
            "radiergummi", "eraser", "tipp-ex", "correction", "korrektur",

            // Measuring tools
            "lineal", "ruler", "geodreieck", "triangle", "winkelmesser",
            "zirkel", "compass", "maßstab", "scale",

            // Mail & shipping
            "briefumschlag", "envelope", "umschlag", "kuvert",
            "briefmarke", "stamp", "porto", "postage",
            "paketband", "packband", "versand", "shipping",

            // Presentation
            "flipchart", "whiteboard", "tafel", "board", "magnettafel",
            "moderationskarten", "cards", "pinwand", "pinnwand",

            // Miscellaneous
            "post-it", "klebezettel", "haftnotiz", "sticky", "notes",
            "spitzer", "sharpener", "anspitzer",
            "schreibtischunterlage", "desk", "pad", "unterlage",
            "büro", "office", "schreibtisch", "arbeitsplatz"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["bürobedarf"] = 2.0,
            ["office"] = 2.0,
            ["büro"] = 1.5
        };
    }
}
