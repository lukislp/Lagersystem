namespace LagersystemLVHome.Data;

public static class CategoryIcons
{
    public static readonly Dictionary<string, List<CategoryIconInfo>> IconsByCategory = new()
    {
        ["Lebensmittel"] = new()
        {
            new("bi-cup-straw", "Getränk"),
            new("bi-egg", "Ei"),
            new("bi-cup-hot", "Heiße Getränke"),
            new("bi-basket", "Korb"),
            new("bi-apple", "Obst")
        },
        ["Getränke"] = new()
        {
            new("bi-cup-straw", "Becher"),
            new("bi-cup-hot", "Heiß"),
            new("bi-droplet", "Tropfen"),
            new("bi-moisture", "Feuchtigkeit")
        },

        ["Elektronik"] = new()
        {
            new("bi-phone", "Telefon"),
            new("bi-laptop", "Laptop"),
            new("bi-tv", "TV"),
            new("bi-keyboard", "Tastatur"),
            new("bi-mouse", "Maus"),
            new("bi-headphones", "Kopfhörer"),
            new("bi-speaker", "Lautsprecher"),
            new("bi-camera", "Kamera"),
            new("bi-router", "Router"),
            new("bi-cpu", "CPU")
        },
        ["Computer"] = new()
        {
            new("bi-laptop", "Laptop"),
            new("bi-pc-display", "Desktop"),
            new("bi-keyboard", "Tastatur"),
            new("bi-mouse", "Maus"),
            new("bi-cpu", "Prozessor"),
            new("bi-memory", "Speicher"),
            new("bi-hdd", "Festplatte"),
            new("bi-usb-drive", "USB"),
            new("bi-router", "Router")
        },

        ["Werkzeuge"] = new()
        {
            new("bi-hammer", "Hammer"),
            new("bi-wrench", "Schraubenschlüssel"),
            new("bi-screwdriver", "Schraubendreher"),
            new("bi-tools", "Werkzeug"),
            new("bi-gear", "Zahnrad"),
            new("bi-nut", "Mutter")
        },
        ["Garten"] = new()
        {
            new("bi-flower1", "Blume"),
            new("bi-tree", "Baum"),
            new("bi-scissors", "Schere"),
            new("bi-bucket", "Eimer")
        },

        ["Möbel"] = new()
        {
            new("bi-house", "Haus"),
            new("bi-door-open", "Tür"),
            new("bi-door-closed", "Tür geschlossen"),
            new("bi-lamp", "Lampe")
        },
        ["Küche"] = new()
        {
            new("bi-egg-fried", "Bratpfanne"),
            new("bi-cup-hot", "Tasse"),
            new("bi-basket", "Korb"),
            new("bi-egg", "Ei")
        },
        ["Reinigung"] = new()
        {
            new("bi-bucket", "Eimer"),
            new("bi-droplet", "Reiniger"),
            new("bi-trash", "Mülleimer")
        },

        ["Kleidung"] = new()
        {
            new("bi-bag", "Tasche"),
            new("bi-handbag", "Handtasche"),
            new("bi-backpack", "Rucksack")
        },
        ["Schuhe"] = new()
        {
            new("bi-bag", "Schuhe"),
            new("bi-backpack", "Stiefel")
        },

        ["Sport"] = new()
        {
            new("bi-bicycle", "Fahrrad"),
            new("bi-trophy", "Trophäe"),
            new("bi-heart-pulse", "Fitness"),
            new("bi-activity", "Aktivität")
        },
        ["Fitness"] = new()
        {
            new("bi-heart-pulse", "Puls"),
            new("bi-activity", "Aktivität"),
            new("bi-trophy", "Erfolg")
        },

        ["Büro"] = new()
        {
            new("bi-pencil", "Stift"),
            new("bi-pen", "Füller"),
            new("bi-file-text", "Dokument"),
            new("bi-folder", "Ordner"),
            new("bi-clipboard", "Zwischenablage"),
            new("bi-calculator", "Rechner"),
            new("bi-paperclip", "Büroklammer"),
            new("bi-envelope", "Umschlag")
        },
        ["Schule"] = new()
        {
            new("bi-book", "Buch"),
            new("bi-journal", "Heft"),
            new("bi-pencil", "Stift"),
            new("bi-backpack", "Schulranzen"),
            new("bi-calculator", "Taschenrechner")
        },

        ["Medizin"] = new()
        {
            new("bi-heart-pulse", "Medizin"),
            new("bi-capsule", "Kapsel"),
            new("bi-bandaid", "Pflaster"),
            new("bi-thermometer", "Thermometer"),
            new("bi-eyeglasses", "Brille")
        },

        ["Spielzeug"] = new()
        {
            new("bi-gift", "Geschenk"),
            new("bi-balloon", "Ballon"),
            new("bi-puzzle", "Puzzle")
        },
        ["Hobby"] = new()
        {
            new("bi-palette", "Palette"),
            new("bi-brush", "Pinsel"),
            new("bi-scissors", "Schere"),
            new("bi-music-note", "Musik")
        },

        ["Auto"] = new()
        {
            new("bi-car-front", "Auto"),
            new("bi-truck", "LKW"),
            new("bi-fuel-pump", "Tankstelle"),
            new("bi-gear", "Getriebe")
        },
        ["Fahrrad"] = new()
        {
            new("bi-bicycle", "Fahrrad")
        },

        ["Bücher"] = new()
        {
            new("bi-book", "Buch"),
            new("bi-journal", "Journal"),
            new("bi-newspaper", "Zeitung")
        },
        ["Musik"] = new()
        {
            new("bi-music-note", "Note"),
            new("bi-disc", "CD"),
            new("bi-headphones", "Kopfhörer")
        },
        ["Beleuchtung"] = new()
        {
            new("bi-lightbulb", "Glühbirne"),
            new("bi-lamp", "Lampe"),
            new("bi-brightness-high", "Hell")
        },
        ["Sicherheit"] = new()
        {
            new("bi-shield-check", "Schutz"),
            new("bi-lock", "Schloss"),
            new("bi-key", "Schlüssel"),
            new("bi-shield-lock", "Sicher")
        },
        ["Verpackung"] = new()
        {
            new("bi-box", "Karton"),
            new("bi-box-seam", "Paket"),
            new("bi-archive", "Archiv"),
            new("bi-bag", "Tasche")
        },
        ["Allgemein"] = new()
        {
            new("bi-box", "Box"),
            new("bi-grid", "Raster"),
            new("bi-list", "Liste"),
            new("bi-tag", "Tag"),
            new("bi-star", "Stern"),
            new("bi-bookmark", "Lesezeichen")
        }
    };

    public static List<CategoryIconInfo> GetAllIcons()
    {
        return IconsByCategory.Values
            .SelectMany(icons => icons)
            .DistinctBy(i => i.IconClass)
            .OrderBy(i => i.Name)
            .ToList();
    }

    public static List<CategoryIconInfo> GetIconsForCategory(string categoryName)
    {
        foreach (var kvp in IconsByCategory)
        {
            if (categoryName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }
        return IconsByCategory["Allgemein"];
    }

    public static string GetDefaultIconForCategory(string categoryName)
    {
        var icons = GetIconsForCategory(categoryName);
        return icons.FirstOrDefault()?.IconClass ?? "bi-box";
    }

    public static readonly List<CategoryIconInfo> PopularIcons = new()
    {
        new("bi-box", "Standard"),
        new("bi-tag", "Tag"),
        new("bi-star", "Favorit"),
        new("bi-heart", "Herz"),
        new("bi-cart", "Warenkorb"),
        new("bi-basket", "Korb"),
        new("bi-bag", "Tasche"),
        new("bi-book", "Buch"),
        new("bi-laptop", "Computer"),
        new("bi-phone", "Telefon"),
        new("bi-tools", "Werkzeug"),
        new("bi-house", "Haus"),
        new("bi-car-front", "Auto"),
        new("bi-bicycle", "Fahrrad"),
        new("bi-gift", "Geschenk")
    };
}

public class CategoryIconInfo
{
    public string IconClass { get; set; }
    public string Name { get; set; }

    public CategoryIconInfo(string iconClass, string name)
    {
        IconClass = iconClass;
        Name = name;
    }
}
