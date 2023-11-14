using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics;
using VRCFaceTracking;
using LibALXR;
using ALXR;
using static LibALXR.LibALXR;
using static ALXR.ModuleUtils;

namespace ALXRLocalModule
{
    public sealed class ALXRLocalModule : ExtTrackingModule
    {
        private ALXRModuleConfig config = new ALXRModuleConfig();
        private XrPosefOneEuroFilter[] poseFilters = new XrPosefOneEuroFilter[2]
        {
            new XrPosefOneEuroFilter(),
            new XrPosefOneEuroFilter(),
        };

        private ALXRClientCtx alxrCtx;

        private bool eyeActive = true;
        private bool lipActive = true;

        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            try
            {
                ModuleInformation.Name = "ALXR Local Module";

                var nativeDLLDir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "ModuleLibs");
                if (!AddDllSearchPath(nativeDLLDir))
                {
                    Logger.Log(LogLevel.Error, $"libalxr library path to search failed to be set.");
                    return (false, false);
                }

                eyeActive = eyeAvailable;
                lipActive = expressionAvailable;

                config = LoadOrNewConfig(Logger);
                Debug.Assert(config != null);
                
                var filterParams = config.EyeTrackingConfig.EyeTrackingFilterParams.FilterParams;
                Array.ForEach(poseFilters, p => p.FilterParams = filterParams);

                LoadTrackingSensitivity(config, Logger);

                alxrCtx = CreateALXRClientCtx(ref config.LocalConfig);
                Debug.Assert(alxrCtx.pathStringToHash != null);

                return (true, true);

            } catch (Exception ex) {
                Logger.Log(LogLevel.Error, ex.ToString());
                return (false, false);
            }
        }

        private void ALXRLogOuput(ALXRLogLevel level, string output, uint len)
        {
            if (string.IsNullOrEmpty(output) || len == 0 || output.Length < len)
                return;
            Debug.Assert(output.Length == len);
            switch (level)
            {
                case ALXRLogLevel.Warning:
                    Logger.LogWarning(output);
                    break;
                case ALXRLogLevel.Error:
                    Logger.LogError(output);
                    break;
                case ALXRLogLevel.Info:
                default:
                    Logger.LogInformation(output);
                    break;
            }
        }

        private void SetEnableALXRLogOuput(bool enable)
        {
            ALXRLogOutputFn logOutputFn = null;
            if (enable)
            {
                logOutputFn = ALXRLogOuput;
            }
            alxr_set_log_custom_output(ALXRLogOptions.None, logOutputFn);
        }

        private void DestroyLibALXR()
        {
            // Disabling alxr logging at this point to workaround for output page crashes for unknown reasons...
            SetEnableALXRLogOuput(false);
            Logger.LogInformation("Shutting down libalxr");
            alxr_destroy();
            Logger.LogInformation("libalxr shutdown");
        }

        public override void Teardown()
        {
            DestroyLibALXR();
        }

        private ALXRProcessFrameResult frameResult = new ALXRProcessFrameResult
        {
            exitRenderLoop = false,
            requestRestart = true,
        };
        private const int RetryTime = 2000;
        private const int MaxRetryCount = 10;
        private int retryCount = 0;
        private Stopwatch stopwatch = new Stopwatch();

        public override void Update()
        {
            if (frameResult.requestRestart)
            {
                DestroyLibALXR();
                SetEnableALXRLogOuput(true);
                var sysProperties = new ALXRSystemProperties();
                if (!alxr_init(ref alxrCtx, out sysProperties))
                {
                    bool isMaxRetryReached = retryCount >= MaxRetryCount;
                    if (isMaxRetryReached)
                    {
                        frameResult.requestRestart = false;
                        frameResult.exitRenderLoop = true;
                    }

                    var retryMsg = isMaxRetryReached ?
                        "Max retry count reached.\nTo try again, exit vrcft, ensure the correct OpenXR runtime is set and the headset is active & fully running." :
                        $"Retrying again in {RetryTime / 1000}s ({retryCount}/{MaxRetryCount})";
                    Logger.LogWarning($"libalxr failed to initialize. {retryMsg}");
                    
                    ++retryCount;
                    // wait and rety.
                    Thread.Sleep(RetryTime);
                    return;
                }
                // Disabling alxr logging at this point to workaround for vrcft output page crashes for unknown reasons...
                SetEnableALXRLogOuput(false);
                frameResult.requestRestart = false;
                frameResult.exitRenderLoop = false;
                retryCount = 0;
            }

            if (frameResult.exitRenderLoop)
            {
                Thread.Sleep(500);
                return;
            }

            frameResult.facialEyeTracking = new ALXRFacialEyePacket();
            alxr_process_frame2(ref frameResult);
            if (frameResult.exitRenderLoop)
            {
                DestroyLibALXR();
                return;
            }

            ApplyFilters(ref frameResult.facialEyeTracking);
            UpdateData(ref frameResult.facialEyeTracking, eyeActive, lipActive);

            if (!alxr_is_session_running())
            {
                // Throttle loop since xrWaitFrame won't be called.
                Thread.Sleep(250);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyEyeGazeFilters(float deltaTime, ref ALXRFacialEyePacket packet)
        {
            packet.eyeGazePose0 = poseFilters[0].Filter(deltaTime, packet.eyeGazePose0);
            packet.eyeGazePose1 = poseFilters[1].Filter(deltaTime, packet.eyeGazePose1);
        }

        private void ApplyFilters(ref ALXRFacialEyePacket newPacket)
        {
            Debug.Assert(config != null);
            if (!config.EyeTrackingConfig.EyeTrackingFilterParams.Enable)
                return;            
            var deltaTime = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            ApplyEyeGazeFilters(deltaTime, ref newPacket);
        }

        private static ALXRClientCtx CreateALXRClientCtx(ref LibALXRConfig config)
        {
            return new ALXRClientCtx
            {
                inputSend = (ref ALXRTrackingInfo data) => { },
                viewsConfigSend = (ref ALXREyeInfo eyeInfo) => { },
                pathStringToHash = (path) => { return (ulong)path.GetHashCode(); },
                timeSyncSend = (ref ALXRTimeSync data) => { },
                videoErrorReportSend = () => { },
                batterySend = (a, b, c) => { },
                setWaitingNextIDR = a => { },
                requestIDR = () => { },
                graphicsApi = config.GraphicsApi,
                decoderType = ALXRDecoderType.D311VA,
                displayColorSpace = ALXRColorSpace.Default,
                facialTracking = config.FacialTrackingExt,
                eyeTracking = config.EyeTrackingExt,
                trackingServerPortNo = ALXRClientConfig.DefaultPortNo,
                verbose = config.VerboseLogs,
                disableLinearizeSrgb = false,
                noSuggestedBindings = true,
                noServerFramerateLock = false,
                noFrameSkip = false,
                disableLocalDimming = true,
                headlessSession = config.HeadlessSession,
                simulateHeadless = config.SimulateHeadless,
                noFTServer = true,
                noPassthrough = true,
                noHandTracking = true, //!config.EnableHandleTracking, temp disabled for future OSC supprot.
                firmwareVersion = new ALXRVersion
                {
                    // only relevant for android clients.
                    major = 0,
                    minor = 0,
                    patch = 0
                }
            };
        }
    }
}
