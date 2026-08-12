namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for food & beverages
/// </summary>
public class FoodKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Lebensmittel";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Basics
            "essen", "food", "trinken", "drink", "getränk", "beverage", "nahrung",
            "lebensmittel", "grocery", "groceries", "meal", "snack",

            // Sweets
            "süüßigkeiten", "süüßigkeit", "candy", "sweets", "bonbon", "gummibärchen",
            "chips", "snack", "knabberzeug", "salzstangen", "cracker", "popcorn",
            "schokolade", "chocolate", "schoko", "kakao", "praline", "riegel",
            "keks", "cookie", "biskuit", "waffel", "kuchen", "torte", "muffin",

            // Beverages - warm
            "kaffee", "coffee", "espresso", "cappuccino", "latte", "nespresso",
            "tee", "tea", "earl", "green", "black", "kräuter", "früchte",
            "kakao", "cocoa", "hot", "chocolate", "trinkschokolade",

            // Beverages - cold
            "wasser", "water", "mineralwasser", "sprudel", "still", "quelle",
            "saft", "juice", "nektar", "smoothie", "direktsaft", "multivitamin",
            "limonade", "limo", "soft", "drink", "erfrischung",

            // Soft drinks & energy
            "cola", "coke", "pepsi", "zero", "light", "diet", "max",
            "sprite", "fanta", "schweppes", "seven", "mezzo", "up",
            "energy", "energydrink", "red", "bull", "monster", "rockstar",

            // Alcohol
            "bier", "beer", "pils", "weizen", "ale", "lager", "radler",
            "wein", "wine", "rot", "weiß", "rosé", "sekt", "champagner",
            "schnaps", "vodka", "whisky", "rum", "gin", "likör", "cognac",

            // Dairy products
            "milch", "milk", "vollmilch", "fettarm", "laktosefrei", "hafermilch",
            "joghurt", "yogurt", "quark", "skyr", "pudding", "dessert",
            "käse", "cheese", "gouda", "emmentaler", "cheddar", "mozzarella",
            "butter", "margarine", "sahne", "cream", "schmand", "frischkäse",

            // Baked goods
            "brot", "bread", "vollkorn", "toast", "baguette", "ciabatta",
            "brütchen", "semmel", "croissant", "bagel", "wrap", "pita",

            // Spreads
            "nutella", "marmelade", "jam", "honig", "honey", "erdnussbutter",
            "aufstrich", "spread", "brotaufstrich", "hummus", "pesto",

            // Cereals
            "müsli", "cereal", "cornflakes", "haferflocken", "granola",
            "porridge", "oatmeal", "frühstück", "breakfast", "crunchy",

            // Canned & ready meals
            "dose", "konserve", "canned", "ravioli", "suppe", "soup",
            "instant", "fertiggericht", "ready", "meal", "tütensuppe",
            "pasta", "nudeln", "spaghetti", "penne", "rigatoni",
            "reis", "rice", "basmati", "jasmin", "risotto",

            // Spices & ingredients
            "gewürz", "spice", "salz", "salt", "pfeffer", "pepper",
            "zucker", "sugar", "mehl", "flour", "öl", "oil", "essig",
            "ketchup", "senf", "mustard", "mayonnaise", "mayo", "soße",

            // Fruit & vegetables
            "obst", "fruit", "apfel", "apple", "banane", "banana",
            "gemüse", "vegetable", "tomate", "tomato", "gurke", "cucumber",
            "salat", "lettuce", "spinat", "spinach", "karotte", "carrot",

            // Meat & fish
            "fleisch", "meat", "wurst", "sausage", "schinken", "ham",
            "höhnchen", "chicken", "rind", "beef", "schwein", "pork",
            "fisch", "fish", "lachs", "salmon", "thunfisch", "tuna",

            // Frozen
            "tiefkühl", "frozen", "pizza", "pommes", "frites", "nuggets",
            "eis", "ice", "cream", "speiseeis", "sorbet",

            // Organic & vegan
            "bio", "organic", "öko", "vegan", "vegetarisch", "vegetarian",

            // Brands
            "haribo", "milka", "ritter", "lindt", "ferrero", "kinder",
            "mars", "snickers", "twix", "kitkat", "smarties", "mnms",
            "nutella", "coca", "pepsi", "sprite", "fanta", "nestle",
            "müller", "danone", "arla", "weihenstephan", "ehrmann"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["lebensmittel"] = 2.0,
            ["food"] = 2.0,
            ["essen"] = 1.5,
            ["trinken"] = 1.5,
            ["getränk"] = 1.5
        };
    }
}
