namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for photo & video
/// </summary>
public class PhotographyKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Foto & Video";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Cameras
            "kamera", "camera", "fotokamera", "spiegelreflex", "dslr",
            "systemkamera", "mirrorless", "vollformat", "fullframe",
            "crop", "sensor", "aps-c", "mft", "micro", "four", "thirds",
            "canon", "nikon", "sony", "fujifilm", "olympus", "panasonic",
            "pentax", "leica", "hasselblad", "phase", "one",

            // Lenses
            "objektiv", "lens", "linse", "zoom", "festbrennweite", "prime",
            "weitwinkel", "wide", "angle", "teleobjektiv", "tele",
            "makro", "macro", "fisheye", "fischauge",
            "bildstabilisator", "is", "vr", "ois", "stabilization",
            "blende", "aperture", "bokeh", "schärfentiefe",

            // Video cameras
            "videokamera", "camcorder", "video", "cinema",
            "actioncam", "action", "gopro", "osmo", "insta360",
            "drohne", "drone", "quadcopter", "dji", "mavic",

            // Accessories
            "stativ", "tripod", "einbeinstativ", "monopod",
            "gimbal", "stabilizer", "schwebestativ", "steadicam",
            "blitz", "flash", "blitzgerät", "speedlight", "studioblitz",
            "softbox", "reflektor", "reflector", "diffusor",
            "fernauslöser", "remote", "trigger", "cable", "release",

            // Storage & batteries
            "speicherkarte", "memory", "card", "sd", "cf", "xqd",
            "akku", "battery", "ladegerät", "charger", "grip",

            // Filters
            "filter", "uv", "polfilter", "nd", "neutraldichtefilter",
            "graufilter", "verlaufsfilter", "gradient",

            // Bags & transport
            "fototasche", "camera", "bag", "rucksack", "backpack",
            "koffer", "case", "hardcase", "pelican",

            // Studio & lighting
            "studiolicht", "studio", "light", "led", "panel",
            "ringlicht", "ring", "light", "dauerlicht", "continuous",
            "lichtformer", "modifier", "beauty", "dish",
            "hintergrund", "backdrop", "greenscreen", "chroma",

            // Processing
            "monitor", "farbkalibrierung", "calibration", "colorimeter",
            "grafiktablett", "pen", "tablet", "wacom", "huion",

            // Miscellaneous
            "fotografie", "photography", "video", "videografie",
            "filmen", "filming", "shooting", "foto", "picture"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
    {
        return new Dictionary<string, double>
        {
            ["kamera"] = 2.0,
            ["camera"] = 2.0,
            ["objektiv"] = 2.0,
            ["lens"] = 2.0,
            ["fotografie"] = 1.5,
            ["video"] = 1.5
        };
    }
}
