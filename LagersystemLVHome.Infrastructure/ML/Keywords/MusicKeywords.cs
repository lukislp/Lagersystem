namespace LagersystemLVHome.Infrastructure.ML.Keywords;

public class MusicKeywords : ICategoryKeywordProvider
{
    public string CategoryName => "Musikinstrumente";

    public List<string> GetKeywords()
    {
        return new List<string>
        {
            "musik", "music", "instrument", "musikinstrument",
            "gitarre", "guitar", "e-gitarre", "electric", "akustikgitarre",
            "bass", "bassgitarre", "saite", "string", "plektrum", "pick",
            "klavier", "piano", "keyboard", "synthesizer", "synth",
            "flügel", "grand", "digital", "stage",
            "schlagzeug", "drums", "drumset", "trommel",
            "becken", "cymbal", "snare", "hihat", "tom",
            "geige", "violin", "violine", "bratsche", "viola",
            "cello", "kontrabass", "double", "bass",
            "saxophon", "saxophone", "klarinette", "clarinet",
            "trompete", "trumpet", "posaune", "trombone",
            "querflöte", "flute", "blockflöte", "recorder",
            "mundharmonika", "harmonica", "akkordeon", "accordion",
            "dj", "equipment", "turntable", "plattenspieler",
            "mischpult", "mixer", "controller", "cdj",
            "mikrofon", "microphone", "mic", "vocal",
            "verstärker", "amplifier", "amp", "combo",
            "lautsprecher", "speaker", "monitor", "pa",
            "kabel", "cable", "jack", "xlr", "klinke",
            "effektgerät", "effects", "pedal", "delay", "reverb",
            "metronom", "metronome", "stimmgerät", "tuner",
            "notenständer", "music", "stand", "noten", "sheet"
        };
    }

    public Dictionary<string, double> GetWeightedKeywords()
        {
        return new Dictionary<string, double>
        {
    ["musik"] = 2.0,
    ["music"] = 2.0,
    ["instrument"] = 2.0
        };
    }
    }
