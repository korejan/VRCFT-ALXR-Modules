using System.IO;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Runtime.Serialization;
using LibALXR;
using static LibALXR.LibALXR;

namespace ALXR
{
    public struct ALXRClientConfig
    {
        public static readonly IPAddress LocalHost = new IPAddress(new byte[] { 127, 0, 0, 1 });
        public const ushort DefaultPortNo = TrackingServerDefaultPortNo;

        [JsonConverter(typeof(IPAddressJsonConverter))]
        public IPAddress ClientIpAddress;

        [JsonInclude]
        public ushort PortNo;

        public static ALXRClientConfig Default
        {
            get => new ALXRClientConfig()
            {
                ClientIpAddress = LocalHost,
                PortNo = DefaultPortNo,
            };
        }
    }

    public struct LibALXRConfig
    {
        [JsonInclude]
        public bool VerboseLogs;
        [JsonInclude]
        public bool EnableHandTracking;
        [JsonInclude]
        public bool HeadlessSession;
        // Enables a headless OpenXR session if supported by the runtime (same as `HeadlessSession`).
        // In the absence of native support, will attempt to simulate a headless session.
        // Caution: May not be compatible with all runtimes and could lead to unexpected behavior.
        [JsonInclude]
        public bool SimulateHeadless;
        [JsonInclude]
        public ALXRGraphicsApi GraphicsApi;
        [JsonInclude]
        public ALXREyeTrackingType EyeTrackingExt;
        [JsonInclude]
        public ALXRFacialExpressionType FacialTrackingExt;
        [JsonInclude]
        public List<ALXRFaceTrackingDataSource> FaceTrackingDataSources;

        public static LibALXRConfig Default
        {
            get => new LibALXRConfig()
            {
                VerboseLogs = false,
                EnableHandTracking = false,
                HeadlessSession = true,
                SimulateHeadless = true,
                GraphicsApi = ALXRGraphicsApi.Auto,
                EyeTrackingExt = ALXREyeTrackingType.Auto,
                FacialTrackingExt = ALXRFacialExpressionType.Auto,
                FaceTrackingDataSources = new List<ALXRFaceTrackingDataSource>()
                {
                    ALXRFaceTrackingDataSource.VisualSource,
                },
            };
        }
    }

    public struct TrackingSensitivityConfig
    {
        [JsonInclude]
        public bool Enable;
        [JsonInclude]
        public string ProfileFilename;

        public static TrackingSensitivityConfig Default
        {
            get {
                return new TrackingSensitivityConfig()
                {
                    Enable = false,
                    ProfileFilename = "AdjerryV4DefaultMultipliers.json"
                };
            }
        }
    }

    /// <summary>
    /// Enum for modes of adjusting eye openness for `XR_FB_face_tracking`
    /// </summary>
    public enum FBEyeOpennessMode
    {
        /// <summary>
        /// Adjusts eye openness in a simple, linear manner considering the effect of lid tightening.
        /// </summary>
        [EnumMember(Value = "LinearLidTightening")]
        LinearLidTightening,

        /// <summary>
        /// Adjusts eye openness using a non-linear function of the lid tightening effect.
        /// </summary>
        [EnumMember(Value = "NonLinearLidTightening")]
        NonLinearLidTightening,

        /// <summary>
        /// Provides a smooth transition between eye-closed and eye-open states for a natural-looking effect.
        /// </summary>
        [EnumMember(Value = "SmoothTransition")]
        SmoothTransition,

        /// <summary>
        /// Adjusts eye openness by considering multiple facial expressions, including looking down, lid tightening, and upper lid raising.
        /// </summary>
        [EnumMember(Value = "MultiExpression")]
        MultiExpression,
    }

    public struct EyeTrackingFilterParams
    {
        [JsonInclude]
        public bool Enable;

        [JsonInclude]
        public OneEuroFilterParams Rot1EuroFilterParams;

        [JsonInclude]
        public OneEuroFilterParams Pos1EuroFilterParams;

        [JsonIgnore]
        public XrPosef1EuroFilterParams FilterParams => new XrPosef1EuroFilterParams()
        {
            RotParams = Rot1EuroFilterParams,
            PosParams = Pos1EuroFilterParams,
        };

        public static EyeTrackingFilterParams Default
        {
            get => new EyeTrackingFilterParams()
            {
                Enable = false,
                Rot1EuroFilterParams = OneEuroFilterParams.Default,
                Pos1EuroFilterParams = OneEuroFilterParams.Default,
            };
        }
    }

    public struct EyeTrackingConfig
    {
        [JsonInclude]
        public FBEyeOpennessMode FBEyeOpennessMode;

        [JsonInclude]
        public bool UseEyeExpressionForGazePose;

        [JsonInclude]
        public EyeTrackingFilterParams EyeTrackingFilterParams;

        public static EyeTrackingConfig Default
        {
            get => new EyeTrackingConfig()
            {
                FBEyeOpennessMode = FBEyeOpennessMode.LinearLidTightening,
                UseEyeExpressionForGazePose = false,
                EyeTrackingFilterParams = EyeTrackingFilterParams.Default
            };
        }
    }

    public sealed class ALXRModuleConfig
    {
        [JsonInclude]
        public ALXRClientConfig RemoteConfig = ALXRClientConfig.Default;

        [JsonInclude]
        public LibALXRConfig LocalConfig = LibALXRConfig.Default;

        [JsonInclude]
        public EyeTrackingConfig EyeTrackingConfig = EyeTrackingConfig.Default;

        [JsonInclude]
        public TrackingSensitivityConfig TrackingSensitivityConfig = TrackingSensitivityConfig.Default;

        private static void AddJsonConverters(JsonSerializerOptions jsonOptions)
        {
            jsonOptions.Converters.Add(new EnumStringConverter<ALXRGraphicsApi>());
            jsonOptions.Converters.Add(new EnumStringConverter<ALXREyeTrackingType>());
            jsonOptions.Converters.Add(new EnumStringConverter<ALXRFacialExpressionType>());
            jsonOptions.Converters.Add(new EnumStringConverter<ALXRFaceTrackingDataSource>());
            jsonOptions.Converters.Add(new EnumStringConverter<FBEyeOpennessMode>());
        }

        public string ToJsonString()
        {
            var jsonOptions = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true
            };
            AddJsonConverters(jsonOptions);
            return JsonSerializer.Serialize(this, jsonOptions);
        }

        public void WriteJsonFile(string filename) =>
            WriteJsonFile(this, filename);

        public static void WriteJsonFile(ALXRModuleConfig config, string filename)
        {
            if (config == null)
                return;
            var jsonStr = config.ToJsonString();
            File.WriteAllText(filename, jsonStr);
        }

        public static ALXRModuleConfig ReadJsonFile(string filename)
        {
            var jsonStr = File.ReadAllText(filename);
            var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
            AddJsonConverters(jsonOptions);
            return JsonSerializer.Deserialize<ALXRModuleConfig>(jsonStr, jsonOptions);
        }
    }
}