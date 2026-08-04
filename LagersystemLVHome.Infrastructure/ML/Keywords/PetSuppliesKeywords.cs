namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Keywords for pet supplies (extended / specialized)
/// </summary>
public class PetSuppliesKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Tiermedizin & Pflege";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            // Medicine & health
            "tiermedizin", "veterinary", "vet", "tierarzt",
            "medikament", "medicine", "arzneimittel",
            "floh", "flea", "zecke", "tick", "parasit",
            "wurmkur", "deworming", "entwurmung",
            "antibiotikum", "antibiotic", "schmerzmittel",
            "augentropfen", "eye", "drops", "ohrentropfen",

            // Care & hygiene
            "pflege", "grooming", "care", "hygiene",
            "shampoo", "hundeshampoo", "katzenshampoo",
            "fellpflege", "coat", "care", "bürste", "brush",
            "kamm", "comb", "entfilzer", "dematting",
            "krallenschere", "nail", "clipper", "trimmer",
            "zahnpflege", "dental", "care", "zahnbürste",
            "ohrenreiniger", "ear", "cleaner",

            // Supplements - dog
            "nahrungsergänzung", "supplement", "vitamine",
            "gelenktabletten", "joint", "support", "glucosamin",
            "omega", "lachsöl", "salmon", "oil",
            "bierhefe", "yeast", "fell", "haut",
            "calcium", "mineralstoffe", "minerals",

            // Supplements - cat
            "katzengras", "cat", "grass", "malzpaste",
            "taurin", "aminosäure", "amino", "acid",

            // First aid
            "verbandsmaterial", "bandage", "verband",
            "wundspray", "wound", "spray", "desinfektionsmittel",
            "zeckenzange", "tick", "remover", "pinzette",
            "erste", "hilfe", "set", "first", "aid",

            // Specialty - dog
            "hundebekleidung", "dog", "clothes", "mantel",
            "regenmantel", "rain", "coat", "wintermantel",
            "leuchtgeschirr", "reflective", "harness",
            "maulkorb", "muzzle", "pfotenschutz", "boots",
            "windeln", "diapers", "läufigkeitshose",

            // Specialty - cat
            "katzenklappe", "cat", "flap", "door",
            "kratzschutz", "scratch", "protection",
            "pheromon", "feliway", "beruhigung", "calming",

            // Specialty - bird
            "vogelsand", "bird", "sand", "grit",
            "sepiaschale", "cuttlebone", "mineral", "block",
            "vitakalk", "calcium", "supplement",
            "vogelbad", "bird", "bath", "badewanne",

            // Specialty - rodents
            "nagerzahn", "gnawing", "wood", "knabberstange",
            "salzleckstein", "salt", "lick", "mineral",
            "nistmaterial", "nesting", "material",
            "hamsterwatte", "hamster", "bedding",

            // Specialty - aquarium
            "wasseraufbereiter", "water", "conditioner",
            "wassertestset", "test", "kit", "ph", "nitrit",
            "aquariumfilter", "filter", "filtermaterial",
            "heizstab", "heater", "thermometer",
            "beleuchtung", "lighting", "led", "aquarium",
            "co2", "anlage", "düngung", "fertilizer",

            // Specialty - terrarium
            "terrariumheizung", "terrarium", "heating",
            "uv", "lampe", "uvb", "reptil",
            "bodengrund", "substrate", "rinde", "bark",
            "höhle", "cave", "versteck", "hide",
            "sprühflasche", "spray", "bottle", "misting",

            // Transport & safety
            "transportbox", "carrier", "transport", "box",
            "autogeschirr", "car", "harness", "safety",
            "autositz", "car", "seat", "hundesitz",
            "gitter", "barrier", "trenngitter", "guard",

            // Training & education
            "clicker", "training", "erziehung",
            "belohnung", "treat", "leckerli", "snack",
            "hundepfeife", "dog", "whistle", "training",
            "apportierdummy", "dummy", "retrieval",

            // Odor neutralization
            "geruchsneutralisierer", "odor", "eliminator",
            "urinentferner", "urine", "cleaner",
            "enzymreiniger", "enzyme", "cleaner",

            // Brands
            "frontline", "advantage", "seresto", "bravecto",
            "royal", "canin", "hills", "eukanuba",
            "animonda", "whiskas", "felix", "sheba",
            "pedigree", "frolic", "chappi",
            "vitakraft", "versele-laga", "sera", "jbl",

            // General
            "tierbedarf", "pet", "supplies", "tiergesundheit",
            "tierpflege", "pet", "care", "haustierbedarf"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["tiermedizin"] = 2.5,
    ["pflege"] = 2.0,
    ["supplement"] = 2.0,
    ["floh"] = 2.0,
    ["zecke"] = 2.0,
    ["wurmkur"] = 2.0,
    ["nahrungsergänzung"] = 1.5
        };
    }
    }
