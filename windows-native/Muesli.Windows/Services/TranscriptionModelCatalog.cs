using System.IO;

namespace Muesli.Windows.Services;

public enum NativeAsrModelKind
{
    Parakeet,
    Whisper,
    SenseVoice,
    Qwen3Asr,
    CohereTranscribe
}

public sealed record TranscriptionModelDefinition(
    string Id,
    string DisplayName,
    string Summary,
    string Languages,
    string SizeLabel,
    NativeAsrModelKind Kind,
    string DirectoryName,
    string ArchiveUrl,
    string ArchiveSha256,
    IReadOnlyDictionary<string, string> RequiredFileSha256,
    string Language = "auto")
{
    public IReadOnlyList<string> RequiredFiles => RequiredFileSha256.Keys.ToArray();
    public string PickerLabel => $"{DisplayName} · {SizeLabel}";
    public string ModelPath => Kind == NativeAsrModelKind.Parakeet
        ? NativeParakeetClient.ModelPath
        : Path.Combine(TranscriptionModelCatalog.ModelCacheDirectory, DirectoryName);
    public bool IsCached => RequiredFiles.All(relativePath => File.Exists(Path.Combine(ModelPath, relativePath)));
    public override string ToString() => PickerLabel;
}

public static class TranscriptionModelCatalog
{
    public const string DefaultModelId = "parakeet-v3";

    public static string ModelCacheDirectory =>
        Environment.GetEnvironmentVariable("MUESLI_NATIVE_ASR_CACHE") ??
        Path.Combine(MuesliPathService.UserProfileDirectory, ".cache", "muesli", "native-asr");

    public static IReadOnlyList<TranscriptionModelDefinition> Models { get; } =
    [
        new(
            DefaultModelId,
            "Parakeet v3",
            "Fast multilingual transcription with token timestamps. Recommended for everyday dictation and meetings.",
            "25 European languages · automatic",
            "~600 MB",
            NativeAsrModelKind.Parakeet,
            "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
            NativeParakeetClient.ModelArchiveSha256,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["encoder.int8.onnx"] = "ACFC2B4456377E15D04F0243AF540B7FE7C992F8D898D751CF134C3A55FD2247",
                ["decoder.int8.onnx"] = "179E50C43D1A9DE79C8A24149A2F9BAC6EB5981823F2A2ED88D655B24248DB4E",
                ["joiner.int8.onnx"] = "3164C13FC2821009440D20FCB5FDC78BFF28B4DB2F8D0F0B329101719C0948B3",
                ["tokens.txt"] = "D58544679EA4BC6AC563D1F545EB7D474BD6CFA467F0A6E2C1DC1C7D37E3C35D"
            }),
        new(
            "whisper-tiny-en",
            "Whisper Tiny English",
            "Smallest download and lowest memory use. Best for quick English dictation on slower PCs.",
            "English",
            "113 MB download · ~100 MB installed",
            NativeAsrModelKind.Whisper,
            "sherpa-onnx-whisper-tiny.en",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-whisper-tiny.en.tar.bz2",
            "2BD6CF965C8BB3E068EF9FA2191387EE63A9DFA2A4E37582A8109641C20005DD",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tiny.en-encoder.int8.onnx"] = "0CE578B827C94A961AACB8FA14B02F096504B337E5C94BE37C36238CBE3E8BC6",
                ["tiny.en-decoder.int8.onnx"] = "06C0E6FF6348D427E51839219D1C886C18CFDF411E629E33F5E1679BFF9C1527",
                ["tiny.en-tokens.txt"] = "306CD27F03C1A714ECA7108E03D66B7DC042ABE8C258B44C199A7ED9838DD930"
            },
            "en"),
        new(
            "whisper-small-en",
            "Whisper Small English",
            "Balanced English Whisper model with stronger accuracy than Tiny.",
            "English",
            "606 MB download · ~360 MB installed",
            NativeAsrModelKind.Whisper,
            "sherpa-onnx-whisper-small.en",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-whisper-small.en.tar.bz2",
            "0CDBA2B8AAAB69E04847F3427CC9709574112E67913A1A84B7FEC3A8729FAA9A",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["small.en-encoder.int8.onnx"] = "8BDAC288F369AA94EE2194059238C465ED82EA9D47EE8FA4A8C0A891873E462F",
                ["small.en-decoder.int8.onnx"] = "710CCF890E10F3FAA15F51EC346081A2723C9F3ADB6E4DA81C6573A5A6F877FB",
                ["small.en-tokens.txt"] = "306CD27F03C1A714ECA7108E03D66B7DC042ABE8C258B44C199A7ED9838DD930"
            },
            "en"),
        new(
            "whisper-medium-en",
            "Whisper Medium English",
            "Higher-accuracy English Whisper model for imported recordings and difficult audio.",
            "English",
            "1.8 GB download · ~905 MB installed",
            NativeAsrModelKind.Whisper,
            "sherpa-onnx-whisper-medium.en",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-whisper-medium.en.tar.bz2",
            "73D95C169A410B5F23A79F8901374B26E0A16A09EA7F02B5E1DB983F4CDFDD67",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["medium.en-encoder.int8.onnx"] = "5A8E3A36619E0B67DB9320EEF3152DB59D4B440F5CE0212D2C162A61B750BF80",
                ["medium.en-decoder.int8.onnx"] = "7303BE339ED4E51F4FFB7AE84F3803B10CF8E67E1DCF8A98CB4D843F0DEA0141",
                ["medium.en-tokens.txt"] = "306CD27F03C1A714ECA7108E03D66B7DC042ABE8C258B44C199A7ED9838DD930"
            },
            "en"),
        new(
            "sensevoice-small-int8",
            "SenseVoice Small INT8",
            "Fast multilingual speech recognition with punctuation and inverse text normalization.",
            "Chinese, English, Japanese, Korean, Cantonese · automatic",
            "156 MB download · ~230 MB installed",
            NativeAsrModelKind.SenseVoice,
            "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17.tar.bz2",
            "7D1EFA2138A65B0B488DF37F8B89E3D91A60676E416F515B952358D83DFD347E",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["model.int8.onnx"] = "C71F0CE00BEC95B07744E116345E33D8CBBE08CEF896382CF907BF4B51A2CD51",
                ["tokens.txt"] = "F449EB28DC567533D7FA59BE34E2ABCA8784F771850C78A47FB731A31429A1DC"
            }),
        new(
            "qwen3-asr-0.6b-int8",
            "Qwen3-ASR 0.6B INT8",
            "Modern multilingual recognizer with broad language and accent coverage.",
            "30 languages plus Chinese dialects · automatic",
            "838 MB download · ~954 MB installed",
            NativeAsrModelKind.Qwen3Asr,
            "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2",
            "393F8A14E2F5FB96746AAAB342997A40641001FBD5BF9592A080A8329178EE96",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["conv_frontend.onnx"] = "D22DC4423E0940E49884E903D2EA2F7E5567C14FC1AED97E4E26D6B8F208EF9E",
                ["encoder.int8.onnx"] = "60748D3E6744A57C9C91E1B17424A6C2990567E8ADCEB0783940C03ED98FA9D9",
                ["decoder.int8.onnx"] = "4F6885BE5959AE26AF3089D38EE7972C5FAFBEEB1CF8D5E76EAB6D8B61CA5771",
                ["tokenizer/merges.txt"] = "8831E4F1A044471340F7C0A83D7BD71306A5B867E95FD870F74D0C5308A904D5",
                ["tokenizer/vocab.json"] = "CA10D7E9FB3ED18575DD1E277A2579C16D108E32F27439684AFA0E10B1440910"
            }),
        new(
            "cohere-transcribe-int8-en",
            "Cohere Transcribe INT8",
            "Large multilingual model configured for punctuated English transcription.",
            "English selected (model supports 14 languages)",
            "1.6 GB download · ~2.7 GB installed",
            NativeAsrModelKind.CohereTranscribe,
            "sherpa-onnx-cohere-transcribe-14-lang-int8-2026-04-01",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-cohere-transcribe-14-lang-int8-2026-04-01.tar.bz2",
            "BD582588D50685A795DCD2807AB77E11361B8312D96C53884682DEF45AB4206D",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["encoder.int8.onnx"] = "CF704F8CFA90E3F0A76F9FFC05998BDF00BA9AE983192C14A85A3A5EB008B367",
                ["encoder.int8.onnx.data"] = "BCF1B7148C8518AE52DF1AD2D2FC2B4E89261EA23E6C874EEF1D9F55BCBAA4A3",
                ["decoder.int8.onnx"] = "8372CA6C8FF4DB8B916CA3592F5C757A715E691B9EDEC751BA19B29FC854BAF9",
                ["tokens.txt"] = "013EDE043AE2480E3A9205CC34550D9686100CC682BACC90F702FACDFBB93035"
            },
            "en")
    ];

    public static TranscriptionModelDefinition Get(string? modelId)
    {
        return Models.FirstOrDefault(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase)) ??
               Models[0];
    }

    public static bool TryGet(string? modelId, out TranscriptionModelDefinition model)
    {
        model = Models.FirstOrDefault(candidate =>
            candidate.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))!;
        return model is not null;
    }

    public static TranscriptionModelDefinition GetRequired(string? modelId)
    {
        return TryGet(modelId, out var model)
            ? model
            : throw new ArgumentOutOfRangeException(nameof(modelId), modelId, "Unknown Windows transcription model ID.");
    }

    public static string NormalizeId(string? modelId) => Get(modelId).Id;

    public static long CacheSizeBytes()
    {
        var genericBytes = Directory.Exists(ModelCacheDirectory)
            ? Directory.EnumerateFiles(ModelCacheDirectory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : 0;
        return genericBytes + NativeParakeetClient.ModelCacheSizeBytes();
    }

    public static void OpenCacheDirectory()
    {
        Directory.CreateDirectory(ModelCacheDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ModelCacheDirectory,
            UseShellExecute = true
        });
    }
}
