namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class BooksMediaKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Bücher & Medien";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "buch", "book", "bücher", "books", "roman", "novel",
            "sachbuch", "non-fiction", "ratgeber", "guide",
            "krimi", "thriller", "fantasy", "sci-fi",
            "hörbuch", "audiobook", "audible",
            "ebook", "e-book", "kindle", "epub", "pdf",
            "dvd", "blu-ray", "bluray", "4k", "uhd", "film", "movie",
            "cd", "musik", "music", "album", "single",
            "vinyl", "schallplatte", "lp", "record",
            "game", "videospiel", "videogame", "spiel",
            "pc", "playstation", "ps5", "xbox", "nintendo", "switch",
            "zeitschrift", "magazine", "zeitung", "newspaper",
            "comic", "manga", "graphic", "novel",
            "streaming", "netflix", "prime", "disney",
            "medien", "media", "unterhaltung", "entertainment"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["buch"] = 2.0,
            ["book"] = 2.0,
            ["medien"] = 1.5
        };
    }
}
