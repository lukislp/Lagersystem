namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for lamps, bulbs & lighting
/// </summary>
public class LightingKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Beleuchtung";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Bulbs - LED
            "led", "leuchtdiode", "led-lampe", "led-birne",
            "led-spot", "led-strahler", "led-röhre",
            "e27", "e14", "gu10", "gu5.3", "mr16",
            "g4", "g9", "sockel", "fassung", "base",
            "lumen", "lm", "helligkeit", "brightness",
            "kelvin", "warmweiß", "warm", "white", "kaltweiß",
            "neutralweiß", "daylight", "tageslichtweiß",
            "dimmbar", "dimmable", "dimmer", "dimming",

            // Classic bulbs
            "glühbirne", "bulb", "birne", "lampe",
            "halogen", "halogenlampe", "halogenspot",
            "energiesparlampe", "cfl", "kompaktleuchtstoff",
            "leuchtstoffröhre", "fluorescent", "tube", "neon",

            // Smart lighting
            "smart", "smarthome", "intelligente", "beleuchtung",
            "philips", "hue", "bridge", "zigbee",
            "tradfri", "ikea", "wlan", "wifi", "bluetooth",
            "rgb", "farbig", "color", "changing",
            "alexa", "google", "home", "sprachsteuerung",
            "fernbedienung", "remote", "control", "app",

            // Lamps - indoor
            "lampe", "lamp", "leuchte", "light", "fixture",
            "deckenlampe", "ceiling", "lamp", "deckenleuchte",
            "pendelleuchte", "pendant", "light", "hängeleuchte",
            "kronleuchter", "chandelier", "lüster",
            "wandlampe", "wall", "lamp", "wandleuchte",
            "tischlampe", "table", "lamp", "tischleuchte",
            "stehlampe", "floor", "lamp", "stehleuchte",
            "nachttischlampe", "bedside", "lamp",
            "schreibtischlampe", "desk", "lamp", "arbeitsleuchte",

            // Lamps - outdoor
            "außenleuchte", "outdoor", "light", "gartenlampe",
            "wegeleuchte", "path", "light", "pollerleuchte",
            "wandaußenleuchte", "outdoor", "wall", "light",
            "solarleuchte", "solar", "light", "solarpanel",
            "bewegungsmelder", "motion", "sensor", "pir",
            "dämmerungsschalter", "dusk", "dawn", "sensor",

            // Recessed lights
            "einbaustrahler", "recessed", "light", "spot",
            "downlight", "einbauleuchte", "deckenspot",
            "panel", "led-panel", "deckenpanel",
            "einbaurahmen", "frame", "housing",

            // String lights
            "lichterkette", "string", "lights", "fairy",
            "weihnachtsbeleuchtung", "christmas", "lights",
            "außenlichterkette", "outdoor", "string",
            "lichtervorhang", "curtain", "lights", "icicle",
            "lichterschlauch", "rope", "light", "tube",

            // Spotlights & track lights
            "strahler", "spotlight", "floodlight", "scheinwerfer",
            "baustrahler", "work", "light", "arbeitsleuchte",
            "led-fluter", "floodlight", "außenstrahler",
            "track", "light", "schienenspot", "schienensystem",

            // Specialty lamps
            "nachtlicht", "night", "light", "orientierungslicht",
            "unterbauleuchte", "under", "cabinet", "light",
            "schrankleuchte", "closet", "light", "sensor",
            "vitrinenleuchte", "display", "cabinet", "light",
            "spiegelleuchte", "mirror", "light", "badezimmer",
            "klemmleuchte", "clip", "lamp", "clamp",

            // Accessories
            "fassung", "socket", "lampenfassung",
            "kabel", "cable", "textilkabel", "fabric",
            "baldachin", "canopy", "rosette", "ceiling",
            "lampenschirm", "shade", "lampshade",
            "dimmer", "dimmschalter", "dimming", "switch",
            "trafo", "transformer", "netzteil", "driver",
            "zeitschaltuhr", "timer", "switch", "zeitsteuerung",

            // Properties
            "energiesparend", "energy", "saving", "effizient",
            "langlebig", "long", "lasting", "lebensdauer",
            "stoßfest", "shockproof", "robust", "sturdy",
            "wasserdicht", "waterproof", "ip44", "ip65",
            "spritzwassergeschützt", "splashproof",

            // Light colors
            "warmweiß", "2700k", "3000k", "gemütlich",
            "neutralweiß", "4000k", "natürlich", "natural",
            "tageslichtweiß", "6000k", "6500k", "cool",
            "rgb", "farbwechsel", "multicolor", "tunable",

            // Brands
            "philips", "osram", "ledvance", "paulmann",
            "eglo", "müller-licht", "müller", "müller-licht",
            "trio", "leuchten", "wofi", "nordlux",
            "steinel", "brennenstuhl", "as", "schwabe",

            // General
            "beleuchtung", "lighting", "licht", "light",
            "leuchtmittel", "bulb", "lamp", "leuchte"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["led"] = 3.0,
    ["lampe"] = 2.5,
    ["lamp"] = 2.5,
    ["leuchte"] = 2.5,
    ["beleuchtung"] = 2.5,
    ["lighting"] = 2.5,
    ["licht"] = 2.0,
    ["light"] = 2.0,
    ["smart"] = 2.0,
    ["hue"] = 2.0
        };
    }
    }
