namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for computers & software
/// </summary>
public class ComputerSoftwareKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Computer & Software";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Hardware components
            "prozessor", "cpu", "processor", "intel", "amd", "ryzen",
            "grafikkarte", "gpu", "graphics", "nvidia", "geforce", "radeon",
            "mainboard", "motherboard", "platine", "board", "chipset",
            "arbeitsspeicher", "ram", "memory", "ddr4", "ddr5", "dimm",
            "netzteil", "psu", "power", "supply", "watt", "modular",
            "gehäuse", "case", "tower", "midi", "big", "mini",
            "kühler", "cooler", "lüfter", "fan", "wasserkühlung", "aio",

            // Storage
            "ssd", "nvme", "m.2", "sata", "festplatte", "hdd",
            "externe", "external", "backup", "nas", "raid",

            // Peripherals
            "maus", "mouse", "gaming", "wireless", "kabellos",
            "tastatur", "keyboard", "mechanical", "mechanisch", "rgb",
            "headset", "kopfhörer", "mikrofon", "webcam",
            "monitor", "bildschirm", "display", "4k", "144hz", "curved",

            // Networking
            "netzwerkkarte", "lan", "ethernet", "wifi", "wlan",
            "router", "switch", "netzwerk", "network",

            // Software & licenses
            "software", "programm", "program", "app", "application",
            "lizenz", "license", "key", "activation", "serial",
            "windows", "microsoft", "office", "365", "subscription",
            "antivirus", "virenschutz", "security", "firewall",
            "adobe", "photoshop", "premiere", "creative", "cloud",

            // Gaming
            "gaming", "gamer", "esports", "streaming", "twitch",
            "controller", "gamepad", "joystick", "racing", "wheel",

            // Accessories
            "usb", "hub", "adapter", "kabel", "cable", "hdmi",
            "dockingstation", "dock", "port", "replicator",
            "mauspad", "mousepad", "extended", "rgb",

            // Miscellaneous
            "pc", "computer", "desktop", "workstation", "server",
            "notebook", "laptop", "ultrabook", "macbook",
            "komponente", "component", "hardware", "aufrästung", "upgrade"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["computer"] = 2.0,
    ["pc"] = 2.0,
    ["software"] = 2.0,
    ["gaming"] = 1.5,
    ["prozessor"] = 1.5,
    ["grafikkarte"] = 1.5
        };
    }
    }
