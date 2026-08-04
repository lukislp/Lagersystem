namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for wine, spirits & beverages
/// </summary>
public class BeveragesAlcoholKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Wein & Spirituosen";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Wine
            "wein", "wine", "rotwein", "red", "weißwein", "white",
            "rosé", "rose", "rose", "prosecco", "sekt", "champagner",
            "champagne", "cava", "crémant",
            "bordeaux", "burgund", "rioja", "chianti", "barolo",
            "chardonnay", "riesling", "sauvignon", "merlot", "cabernet",
            "pinot", "noir", "grigio", "syrah", "shiraz",
            "jahrgang", "vintage", "flasche", "bottle", "magnum",
            "weinberg", "vineyard", "château", "château", "weingut",

            // Sparkling wine
            "schaumwein", "sparkling", "perlwein", "secco",
            "winzersekt", "brut", "extra", "dry", "demi-sec",

            // Spirits - whisky
            "whisky", "whiskey", "bourbon", "scotch", "single", "malt",
            "blend", "blended", "rye", "irish", "japan", "japanisch",
            "barrel", "cask", "aged", "jahre", "years", "old",
            "islay", "speyside", "highland", "lowland",

            // Spirits - other
            "vodka", "wodka", "gin", "london", "dry", "tonic",
            "rum", "bacardi", "captain", "morgan", "havana",
            "tequila", "mezcal", "agave", "silver", "gold", "añejo",
            "cognac", "brandy", "armagnac", "calvados",
            "likör", "liqueur", "cream", "amaretto", "baileys",
            "absinth", "absinthe", "grappa", "ouzo", "raki",
            "schnapps", "schnaps", "obstler", "williams", "kirsch",

            // Beer - premium
            "craftbeer", "craft", "beer", "ipa", "pale", "ale",
            "stout", "porter", "weizen", "hefeweizen", "dunkel",
            "pils", "pilsner", "lager", "bock", "doppelbock",
            "trappist", "abbey", "kloster", "belgisch", "belgian",

            // Sake & Asian
            "sake", "reiswein", "shochu", "soju", "baijiu",

            // Accessories
            "weinglas", "glas", "glass", "kelch", "karaffe",
            "dekanter", "decanter", "korkenzieher", "corkscrew",
            "weinkühlschrank", "wine", "cooler", "klimaschrank",
            "flaschenöffner", "opener", "bottle", "cap",
            "untersetzer", "coaster", "weinregal", "rack",

            // Cocktails & mixing
            "cocktail", "mixer", "shaker", "jigger", "barkeeper",
            "strainer", "muddler", "bar", "spoon", "löffel",

            // General
            "alkohol", "alcohol", "spirituose", "spirit", "destillat",
            "prozent", "vol", "alcohol", "content", "abv",
            "flasche", "bottle", "liter", "ml", "cl",
            "edel", "premium", "luxury", "limited", "edition"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["wein"] = 2.0,
    ["wine"] = 2.0,
    ["whisky"] = 2.0,
    ["spirituosen"] = 2.0,
    ["alkohol"] = 1.5
        };
    }
    }
