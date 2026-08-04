namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for batteries
/// </summary>
public class BatteriesKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Batterien";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // General - HIGHEST PRIORITY
            "batterie", "batterien", "battery", "batteries",
            "akku", "akkus", "rechargeable", "wiederaufladbar",
            "einweg", "disposable", "primär", "sekundär",
            "stromversorgung", "energiezelle", "powercell",

            // Standard sizes - SPECIFIC!
            "aa-batterie", "aaa-batterie", "aa batterie", "aaa batterie",
            "aa", "aaa", // Short variants (scored lower by match quality)
            "mignon", "micro", "baby-batterie", "mono-batterie",
            "c-batterie", "d-batterie", "9v-batterie", "9v batterie", "9v",
            "blockbatterie", "e-block",
            "n-batterie", "lady-batterie", "aaaa-batterie",
            "lr1", "lr6", "lr03", "lr14", "lr20",

            // Coin cells - VERY SPECIFIC
            "knopfzelle", "knopfzellen", "coin-cell", "button-cell",
            "cr2032", "cr2025", "cr2016", "cr1220", "cr1616",
            "lr44", "lr41", "lr1130", "ag13", "ag10",
            "sr44", "sr41", "sr920", "sr626",

            // Specialty batteries
            "photobatterie", "lithium-batterie", "cr123a", "cr2",
            "2cr5", "6v-batterie", "4lr44", "px28", "a23", "23a",
            "12v-batterie", "27a", "a27", "mn27",

            // Chemistry types
            "alkaline", "alkali-batterie", "alkali-mangan",
            "lithium-ion", "li-ion", "liion", "li-po", "lipo",
            "nimh", "ni-mh", "nickel-metallhydrid",
            "nicd", "ni-cd", "nickel-cadmium",
            "zink-luft", "zinc-air", "hörgerätebatterie",

            // Battery types
            "akku-batterie", "wiederaufladbarer", "wiederaufladbar",
            "eneloop", "ready-to-use", "rtu",
            "low-self-discharge", "lsd",
            "precharged", "vorgeladen", "sofort-einsetzbar",

            // Capacity & voltage
            "mah", "milliamperestunden", "wh", "wattstunde",
            "volt", "spannung", "voltage",
            "1.2v", "1.5v", "3v", "3.7v", "12v",
            "kapazität", "capacity", "leistung",

            // Brands - BATTERY-SPECIFIC
            "varta-batterie", "duracell", "energizer",
            "panasonic-batterie", "eneloop", "eneloop-pro",
            "ikea-ladda", "ladda", "sony-batterie",
            "gp-batterie", "ansmann", "camelion",
            "philips-batterie", "powerex", "tenergy",

            // Chargers - FOR BATTERIES
            "batterieladegerät", "akkuladegerät", "ladegerät",
            "charger", "charging-station",
            "ladestation", "universal-ladegerät",
            "smart-charger", "intelligent-charger",
            "usb-ladegerät", "auto-ladegerät", "kfz-ladegerät",
            "schnelllader", "fast-charger", "quick-charge",
            "erhaltungsladung", "trickle-charge",

            // Accessories
            "batteriebox", "aufbewahrungsbox", "storage-box",
            "batterietester", "battery-tester", "prüfgerät",
            "batterieadapter", "adapter-spacer", "dummy-batterie",
            "batteriekontakt", "kontaktfeder",

            // Applications
            "fernbedienungsbatterie", "remote-batterie",
            "taschenlampen-batterie", "flashlight-battery",
            "uhrenbatterie", "clock-battery", "weckerbatterie",
            "spielzeugbatterie", "toy-battery",
            "kamerabatterie", "camera-battery",
            "tastatur-batterie", "keyboard-battery",
            "maus-batterie", "mouse-battery",
            "thermometer-batterie", "medical-battery",

            // Properties
            "langlebig", "long-lasting", "endurance",
            "zuverlössig", "reliable", "premium-quality",
            "umweltfreundlich", "eco-friendly",
            "quecksilberfrei", "mercury-free", "cadmiumfrei",

            // Solar & alternative
            "solarbatterie", "solar-battery",
            "wiederaufladbare-batterie", "aufladbar",

            // Specialty
            "hörgerätebatterie", "hearing-aid-battery",
            "uhrenbatterie", "watch-battery", "renata", "maxell",
            "autoschlösselbatterie", "car-key-battery",
            "rauchmelder-batterie", "smoke-detector-battery"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            // HIGHEST PRIORITY - main keywords
            ["batterie"] = 3.0,
            ["batterien"] = 3.0,
            ["akku"] = 3.0,
            ["battery"] = 3.0,
            ["batteries"] = 3.0,

            // VERY IMPORTANT - specific sizes (longer variants preferred!)
            ["aa-batterie"] = 2.5,
            ["aaa-batterie"] = 2.5,
            ["aa batterie"] = 2.5,
            ["aaa batterie"] = 2.5,
            ["9v-batterie"] = 2.5,
            ["9v batterie"] = 2.5,
            ["mignon"] = 2.0,
            ["micro"] = 2.0,

            // IMPORTANT BUT SHORT - automatically scored lower by match quality
            // These matter for bare "AA", but quality 0.1 (2 chars)
            ["aa"] = 1.8,  // Important but short - quality lowers the score automatically
            ["aaa"] = 1.8,
            ["9v"] = 1.5,

            // IMPORTANT - types & brands
            ["alkaline"] = 2.0,
            ["lithium"] = 2.0,
            ["rechargeable"] = 2.0,
            ["duracell"] = 2.0,
            ["varta"] = 2.0,
            ["energizer"] = 2.0,
            ["eneloop"] = 2.0,

            // MEDIUM - chargers
            ["ladegerät"] = 1.5,
            ["charger"] = 1.5
        };
    }
}
