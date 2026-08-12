namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for electronics products
/// </summary>
public class ElectronicsKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Elektronik";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Computers & laptops
            "laptop", "notebook", "computer", "pc", "desktop", "workstation",
            "tablet", "ipad", "surface", "chromebook", "macbook",

            // Monitors & displays
            "monitor", "bildschirm", "display", "screen", "curved", "ultrawide",
            "4k", "fullhd", "wqhd", "oled", "led", "lcd",

            // Input devices
            "tastatur", "keyboard", "mechanical", "gaming", "wireless",
            "maus", "mouse", "trackball", "trackpad", "touchpad",
            "grafiktablett", "pen", "stylus", "drawing",

            // Mobile devices
            "handy", "smartphone", "iphone", "android", "galaxy",
            "pixel", "oneplus", "xiaomi", "huawei", "oppo", "realme",
            "smartwatch", "watch", "wearable", "fitbit", "garmin", "applewatch",
            "fitness", "tracker", "band", "bracelet",

            // Smart home & IoT
            "smart", "smarthome", "iot", "connected", "intelligent",
            "alexa", "echo", "dot", "show", "studio",
            "google", "home", "nest", "mini", "hub", "assistant",
            "homepod", "siri", "apple", "homekit",

            // Smart lighting
            "philips", "hue", "white", "color", "ambiance",
            "tradfri", "ikea", "fyrtur", "symfonisk",
            "yeelight", "lifx", "nanoleaf", "panels",
            "bulb", "lampe", "licht", "light", "beleuchtung",
            "strip", "led", "rgb", "rgbw", "rgbww", "farbe", "color",
            "dimmer", "schalter", "switch", "button", "remote",

            // Smart plugs & sensors
            "plug", "steckdose", "socket", "outlet", "power",
            "sensor", "motion", "bewegung", "presence", "temperature",
            "humidity", "luftfeuchtigkeit", "air", "quality",
            "door", "window", "tür", "fenster", "contact",

            // Smart security
            "doorbell", "klingel", "ring", "video", "camera", "überwachung",
            "lock", "schloss", "yale", "nuki", "august", "security",
            "detector", "rauchmelder", "smoke", "co", "carbon", "alarm", "fire",

            // Smart home appliances
            "thermostat", "heizung", "heating", "tado", "nest", "climate",
            "vacuum", "saugrobot", "robot", "roborock", "roomba", "cleaner",
            "air", "purifier", "luftreiniger", "filter", "dyson",
            "curtain", "vorhang", "blind", "rollo", "shutter", "jalousie",

            // Networking & connectivity
            "hub", "bridge", "gateway", "controller", "zentrale",
            "zigbee", "zwave", "matter", "thread", "wifi", "wlan",
            "bluetooth", "ble", "mesh", "repeater", "extender",

            // Trackers & finders
            "finder", "tracker", "locator", "sucher", "orten",
            "airtag", "tile", "chipolo", "samsung", "smarttag",

            // Audio & speakers
            "speaker", "lautsprecher", "bluetooth", "portable",
            "sonos", "bose", "jbl", "harman", "kardon", "ue",
            "soundbar", "subwoofer", "surround", "heimkino",
            "receiver", "amplifier", "verstärker", "av",

            // Headphones
            "kopfhörer", "headset", "earbuds", "headphones",
            "airpods", "pro", "max", "beats", "sony", "wh",
            "noise", "cancelling", "anc", "transparency",
            "over-ear", "on-ear", "in-ear", "true", "wireless",

            // Microphones & webcams
            "mikrofon", "microphone", "mic", "usb", "xlr",
            "webcam", "kamera", "camera", "streaming", "recording",
            "logitech", "razer", "elgato", "blue", "yeti",

            // TV & streaming
            "tv", "fernseher", "television", "smart", "4k", "8k",
            "roku", "firestick", "chromecast", "apple", "tv",
            "streaming", "stick", "dongle", "media", "player",

            // Gaming consoles
            "playstation", "ps5", "ps4", "xbox", "series",
            "nintendo", "switch", "lite", "oled",
            "console", "controller", "gamepad", "dualsense",
            "gaming", "gamer", "spiel", "videospiel",
            "steam", "deck", "valve", "portable",

            // VR & AR
            "vr", "virtual", "reality", "headset",
            "oculus", "quest", "meta", "rift",
            "playstation", "psvr", "valve", "index",
            "htc", "vive", "pico", "ar", "augmented",

            // Storage & media
            "festplatte", "ssd", "nvme", "m.2", "sata",
            "hdd", "hard", "drive", "disk", "storage",
            "external", "extern", "portable", "backup",
            "nas", "network", "attached", "synology", "qnap",
            "usb", "stick", "flash", "pendrive", "thumb",
            "memory", "card", "sd", "microsd", "cf",

            // Cables & adapters
            "kabel", "cable", "wire", "cord", "lead",
            "hdmi", "displayport", "usb-c", "lightning", "thunderbolt",
            "vga", "dvi", "ethernet", "lan", "cat6", "cat7",
            "adapter", "converter", "dongle", "hub", "splitter",

            // Chargers & power
            "ladegerät", "charger", "charging", "laden",
            "netzteil", "power", "supply", "adapter", "brick",
            "powerbank", "portable", "battery", "akku",
            "ladekabel", "cable", "usb", "wireless", "qi",
            "fast", "quick", "charge", "pd", "gan",

            // Networking
            "router", "wlan", "wifi", "mesh", "tri-band",
            "repeater", "extender", "range", "booster",
            "access", "point", "ap", "hotspot",
            "switch", "gigabit", "poe", "managed",
            "ethernet", "lan", "netzwerk", "network",
            "modem", "dsl", "kabel", "glasfaser",
            "fritzbox", "tp-link", "asus", "netgear", "linksys",

            // E-readers & digital paper
            "kindle", "ereader", "ebook", "reader", "paperwhite",
            "tolino", "kobo", "pocketbook", "onyx", "boox",
            "remarkable", "supernote", "digital", "paper",

            // Drones & action cams
            "drohne", "drone", "quadcopter", "quadrocopter",
            "dji", "mavic", "mini", "air", "phantom",
            "gopro", "hero", "action", "cam", "kamera",
            "insta360", "osmo", "pocket", "gimbal",

            // Batteries (general)
            "batterie", "battery", "akku", "rechargeable",
            "aa", "aaa", "9v", "cr2032", "lithium",
            "alkaline", "nimh", "liion", "lipo",

            // Accessories
            "halter", "halterung", "mount", "bracket", "ständer",
            "tasche", "case", "bag", "sleeve", "hülle",
            "schutzfolie", "screen", "protector", "panzerglas",
            "reinigung", "cleaning", "kit", "spray", "tuch"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            // Main keywords with higher weighting
            ["smart"] = 2.0,
            ["alexa"] = 2.0,
            ["google"] = 2.0,
            ["apple"] = 2.0,
            ["samsung"] = 2.0,
            ["philips"] = 2.0,
            ["hue"] = 2.0,
            ["bluetooth"] = 1.5,
            ["wifi"] = 1.5,
            ["wireless"] = 1.5,
            ["iot"] = 2.0
        };
    }
}
