using System.Diagnostics;
using System;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking;
using VRCFaceTracking.Core.Types;
using System.Reflection;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using LibALXR;

namespace ALXR
{
    public static class ModuleUtils
    {
        #region Tracking Sensitivity (File) Functions
        public static FBTrackingSensitivity TrackingSensitivity { get; private set; } = new FBTrackingSensitivity();

        public static bool LoadTrackingSensitivity(ALXRModuleConfig config, ILogger logger)
        {
            if (config == null || logger == null)
                return false;
            logger.LogInformation("Tracking sensitivity multipliers are {0}", config.TrackingSensitivityConfig.Enable ? "enabled" : "disabled");
            if (!config.TrackingSensitivityConfig.Enable) {
                return false;
            }
            var newMultipliers = FBTrackingSensitivity.LoadAndMonitor(logger, config.TrackingSensitivityConfig.ProfileFilename);
            if (newMultipliers != null) {
                TrackingSensitivity = newMultipliers;
            }
            Debug.Assert(TrackingSensitivity != null);
            return true;
        }
        #endregion

        #region Module Config (File) Functions
        public static class ModuleConfigPath
        {
            public const string Filename = "ALXRModuleConfig.json";
            public static string Directory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            public static string FullPath => Path.Combine(ModuleConfigPath.Directory, ModuleConfigPath.Filename);
        }

        public static string ConfigFilename => ModuleConfigPath.FullPath;

        public static FBEyeOpennessMode FBEyeOpennessMode {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            private set;
        } = FBEyeOpennessMode.LinearLidTightening;

        public static bool UseEyeExpressionForGazePose
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            private set;
        } = false;

        public static bool SaveConfig(ALXRModuleConfig ALXRModuleConfig, ILogger logger) =>
            SaveConfig(ALXRModuleConfig, logger, ConfigFilename);

        public static bool SaveConfig(ALXRModuleConfig ALXRModuleConfig, ILogger logger, string configFile)
        {
            try
            {
                if (ALXRModuleConfig == null || logger == null)
                {
                    return false;
                }

                logger.LogInformation($"Writing alxr-config: {configFile}");

                ALXRModuleConfig.WriteJsonFile(configFile);

                logger.LogInformation($"alxr-config successfully written.");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to write alxr-config, reason: {ex.Message}");
                return false;
            }
        }

        public static bool SaveConfigShortcut(string filename, ILogger logger)
        {
            try
            {
                if (string.IsNullOrEmpty(filename))
                    throw new ArgumentException("filename is null/empty");
                if (!Path.Exists(filename))
                    throw new ArgumentException($"file path: {filename} does not exist");

                var lnkFilename = $"{Path.GetFileNameWithoutExtension(filename)}.lnk";
                var shortcutLocation = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    lnkFilename
                );
                if (Path.Exists(shortcutLocation))
                {
                    logger.LogInformation($"Shortcut to {filename} already exists, skipping shortcut creation.");
                    return false;
                }
                var shell = new IWshRuntimeLibrary.WshShell();
                var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutLocation);
                shortcut.Description = $"Shortcut to {Path.GetFileName(filename)}";   // The description of the shortcut
                shortcut.TargetPath = filename;                 // The path of the file that will launch when the shortcut is run
                shortcut.Save();
                logger.LogInformation($"Successfully created shortcut for {Path.GetFileName(filename)}");
                return true;
            } catch (Exception ex) {
                logger.LogError($"Failed to create shortcut for file: {filename}, reason: {ex.Message}");
                return false;
            }
        }

        public static ALXRModuleConfig LoadOrNewConfig(ILogger logger) =>
            LoadOrNewConfig(logger, ConfigFilename);

        public static ALXRModuleConfig LoadOrNewConfig(ILogger logger, string filename)
        {
            var moduleConfig = LoadConfig(logger, filename);
            if (moduleConfig == null)
            {
                moduleConfig = new ALXRModuleConfig();
                SaveConfig(moduleConfig, logger, filename);
            }
            
            SaveConfigShortcut(filename, logger);

            Debug.Assert(moduleConfig != null);
            var eyeTrackingConfig = moduleConfig.EyeTrackingConfig;
            UseEyeExpressionForGazePose = eyeTrackingConfig.UseEyeExpressionForGazePose;
            FBEyeOpennessMode = eyeTrackingConfig.FBEyeOpennessMode;
            logger.LogInformation($"Selected FB eye openness mode: {FBEyeOpennessMode}");
            if (UseEyeExpressionForGazePose)
                logger.LogInformation("Using eye expressions for eye gaze pose enabled.");
            return moduleConfig;
        }

        public static ALXRModuleConfig LoadConfig(ILogger logger) =>
            LoadConfig(logger, ConfigFilename);

        public static ALXRModuleConfig LoadConfig(ILogger logger, string configFile)
        {
            try
            {
                if (logger == null || string.IsNullOrEmpty(configFile))
                    return null;
                logger.LogInformation($"Loading module config file: {configFile}");
                if (!File.Exists(configFile))
                {
                    logger.LogWarning($"Failed to find alxr-config json, file doest not exist.");
                    return null;
                }
                var result = ALXRModuleConfig.ReadJsonFile(configFile);
                if (result == null)
                    throw new Exception($"Failed read json file {configFile}");
                logger.LogInformation("Successfully loaded module config.");
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to read alxr-config, reason: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region All Update Data Functions

        #region Top-level Update Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateData(ref ALXRFacialEyePacket newPacket, bool eyeActive, bool lipActive)
        {
            if (eyeActive)
            {
                UpdateEyeData(ref newPacket);
                UpdateEyeExpressions(ref newPacket);
            }
            if (lipActive)
                UpdateMouthExpressions(ref newPacket);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeData(ref ALXRFacialEyePacket packet)
        {
            switch (packet.eyeTrackerType)
            {
                case ALXREyeTrackingType.FBEyeTrackingSocial:
                    UpdateEyeDataFB(ref UnifiedTracking.Data.Eye, ref packet);
                    break;
                case ALXREyeTrackingType.ExtEyeGazeInteraction:
                    UpdateEyeDataEyeGazeEXT(ref UnifiedTracking.Data.Eye, ref packet);
                    break;
                case ALXREyeTrackingType.AndroidAvatarEyes:
                    UpdateEyeDataANDROID(ref UnifiedTracking.Data.Eye, ref packet);
                    break;
                default:
                    UpdateEyeDataNone(ref UnifiedTracking.Data.Eye, ref packet);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeExpressions(ref ALXRFacialEyePacket packet)
        {
            var expressionWeights = packet.ExpressionWeightSpan;
            switch (packet.expressionType)
            {
                case ALXRFacialExpressionType.FB_V2:
                    UpdateEyeOpenessFBV2(ref UnifiedTracking.Data.Eye, expressionWeights);
                    UpdateEyeExpressionsFBV2(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                    break;
                case ALXRFacialExpressionType.FB:
                    UpdateEyeOpenessFB(ref UnifiedTracking.Data.Eye, expressionWeights);
                    UpdateEyeExpressionsFB(ref UnifiedTracking.Data.Shapes, expressionWeights);
                    break;
                case ALXRFacialExpressionType.HTC:
                    UpdateEyeOpenessHTC(ref UnifiedTracking.Data.Eye, expressionWeights);
                    UpdateEyeExpressionsHTC(ref UnifiedTracking.Data.Shapes, expressionWeights);
                    break;
                case ALXRFacialExpressionType.Android:
                    UpdateEyeOpenessANDROID(ref UnifiedTracking.Data.Eye, expressionWeights);
                    UpdateEyeExpressionsANDROID(ref UnifiedTracking.Data.Shapes, expressionWeights);
                    break;
                default:
                    UnifiedTracking.Data.Eye.Left.Openness  = 1.0f;
                    UnifiedTracking.Data.Eye.Right.Openness = 1.0f;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateMouthExpressions(ref ALXRFacialEyePacket packet)
        {
            switch (packet.expressionType)
            {
                case ALXRFacialExpressionType.FB_V2:
                    UpdateMouthExpressionsFBV2(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                    break;
                case ALXRFacialExpressionType.FB:
                    UpdateMouthExpressionsFB(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                    break;
                case ALXRFacialExpressionType.HTC:
                    UpdateMouthExpressionsHTC(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan.Slice(14));
                    break;
                case ALXRFacialExpressionType.Android:
                    UpdateMouthExpressionsANDROID(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                    break;
            }
        }
        #endregion

        #region UpdateEyeGaze Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 NormalizedGaze(ref ALXRQuaternionf q)
        {
            float magnitude = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            float xm = q.x / magnitude;
            float ym = q.y / magnitude;
            float zm = q.z / magnitude;
            float wm = q.w / magnitude;

            float pitch = (float)Math.Asin(2*(xm*zm - wm*ym));
            float yaw = (float)Math.Atan2(2.0*(ym*zm + wm*xm), wm*wm - xm*xm - ym*ym + zm*zm);

            return new Vector2(pitch, yaw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateEyeGaze(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            eye.Right.Gaze = NormalizedGaze(ref packet.eyeGazePose1.orientation);
            eye.Left.Gaze = NormalizedGaze(ref packet.eyeGazePose0.orientation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeDataNone(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            // Eye dilation code, automated process maybe?
            eye.Left.PupilDiameter_MM  = 5f;
            eye.Right.PupilDiameter_MM = 5f;

            // Force the normalization values of Dilation to fit avg. pupil values.
            eye._minDilation = 0;
            eye._maxDilation = 10;

            eye.Left.Gaze  = new Vector2(0, 0);
            eye.Right.Gaze = new Vector2(0, 0);
        }

        #endregion

        #region XR_EXT_eye_gaze_interaction Update Function

        public static void UpdateEyeDataEyeGazeEXT(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            UpdateEyeGaze(ref eye, ref packet);

            // Eye dilation code, automated process maybe?
            eye.Left.PupilDiameter_MM  = 5f;
            eye.Right.PupilDiameter_MM = 5f;

            // Force the normalization values of Dilation to fit avg. pupil values.
            eye._minDilation = 0;
            eye._maxDilation = 10;
        }
        #endregion

        #region HTC Facial Update Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateEyeOpenessHTC(ref UnifiedEyeData eye, ReadOnlySpan<float> expressionWeights)
        {
            eye.Left.Openness  = 1.0f - expressionWeights[(int)XrEyeExpressionHTC.LeftBlink];
            eye.Right.Openness = 1.0f - expressionWeights[(int)XrEyeExpressionHTC.RightBlink];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeExpressionsHTC(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressionWeights)
        {
            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftWide];
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightWide]; ;

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftSqueeze]; ;
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightSqueeze]; ;

            // Emulator expressions for Unified Expressions. These are essentially already baked into Legacy eye expressions (SRanipal)
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftWide];
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftWide];

            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightWide]; ;
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightWide]; ;

            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftSqueeze]; ;
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftSqueeze]; ;

            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightSqueeze]; ;
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightSqueeze]; ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateMouthExpressionsHTC(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressionWeights)
        {
            #region Direct Jaw

            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight = expressionWeights[(int)XrLipExpressionHTC.JawOpen] + expressionWeights[(int)XrLipExpressionHTC.MouthApeShape];
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.JawLeft];
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight = expressionWeights[(int)XrLipExpressionHTC.JawRight];
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = expressionWeights[(int)XrLipExpressionHTC.JawForward];
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthApeShape];

            #endregion

            #region Direct Mouth and Lip

            // These shapes have overturns subtracting from them, as we are expecting the new standard to have Upper Up / Lower Down baked into the funneller shapes below these.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpRight] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpRight] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpLeft] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpLeft] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerDownLeft] - expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerDownRight] - expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverturn];

            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];

            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperInside];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperInside];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerInside];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerInside];

            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverlay] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperInside];
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverlay];

            #endregion

            #region Direct Cheek

            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekPuffLeft];
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekPuffRight];

            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekSuck];
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekSuck];

            #endregion

            #region Direct Tongue

            unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = (expressionWeights[(int)XrLipExpressionHTC.TongueLongStep1] + expressionWeights[(int)XrLipExpressionHTC.TongueLongStep2]) * 0.5f;
            unifiedExpressions[(int)UnifiedExpressions.TongueUp].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueUp];
            unifiedExpressions[(int)UnifiedExpressions.TongueDown].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueDown];
            unifiedExpressions[(int)UnifiedExpressions.TongueLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueLeft];
            unifiedExpressions[(int)UnifiedExpressions.TongueRight].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueRight];
            unifiedExpressions[(int)UnifiedExpressions.TongueRoll].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueRoll];

            #endregion

            // These shapes are not tracked at all by SRanipal, but instead are being treated as enhancements to driving the shapes above.

            #region Emulated Unified Mapping

            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadRight];

            #endregion
        }
        #endregion

        #region FB V2 Eye & Facial Update Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeDataFBV2(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            if (UseEyeExpressionForGazePose && packet.expressionType == ALXRFacialExpressionType.FB_V2)
            {
                var expressions = packet.ExpressionWeightSpan;
                eye.Left.Gaze = MakeEye(
                    expressions[(int)FBExpression2.Eyes_Look_Left_L],
                    expressions[(int)FBExpression2.Eyes_Look_Right_L],
                    expressions[(int)FBExpression2.Eyes_Look_up_L],
                    expressions[(int)FBExpression2.Eyes_Look_Down_L]
                );
                eye.Right.Gaze = MakeEye(
                    expressions[(int)FBExpression2.Eyes_Look_Left_R],
                    expressions[(int)FBExpression2.Eyes_Look_Right_R],
                    expressions[(int)FBExpression2.Eyes_Look_up_R],
                    expressions[(int)FBExpression2.Eyes_Look_Down_R]
                );
            }
            else // Default use eye gaze pose.
            {
                UpdateEyeGaze(ref eye, ref packet);
            }

            // Eye dilation code, automated process maybe?
            eye.Left.PupilDiameter_MM = 5f;
            eye.Right.PupilDiameter_MM = 5f;

            // Force the normalization values of Dilation to fit avg. pupil values.
            eye._minDilation = 0;
            eye._maxDilation = 10;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateEyeOpenessFBV2(ref UnifiedEyeData eye, ReadOnlySpan<float> expressionWeights)
        {
            switch (FBEyeOpennessMode)
            {
                case FBEyeOpennessMode.LinearLidTightening:
                {
                    eye.Left.Openness = 1f - Math.Clamp
                    (
                        expressionWeights[(int)FBExpression2.Eyes_Closed_L] + expressionWeights[(int)FBExpression2.Eyes_Closed_L] * expressionWeights[(int)FBExpression2.Lid_Tightener_L],
                        0f, 1f
                    );
                    eye.Right.Openness = 1f - Math.Clamp
                    (
                        expressionWeights[(int)FBExpression2.Eyes_Closed_R] + expressionWeights[(int)FBExpression2.Eyes_Closed_R] * expressionWeights[(int)FBExpression2.Lid_Tightener_R],
                        0f, 1f
                    );
                }   break;

                case FBEyeOpennessMode.NonLinearLidTightening:
                {
                    eye.Left.Openness = 1f - (float)Math.Clamp
                    (
                        expressionWeights[(int)FBExpression2.Eyes_Closed_L] + // Use eye closed full range
                        expressionWeights[(int)FBExpression2.Eyes_Closed_L] * (2f * expressionWeights[(int)FBExpression2.Lid_Tightener_L] / Math.Pow(2f, 2f * expressionWeights[(int)FBExpression2.Lid_Tightener_L])), // Add lid tighener as the eye closes to help winking.
                        0f, 1f
                    );

                    eye.Right.Openness = 1f - (float)Math.Clamp
                    (
                        expressionWeights[(int)FBExpression2.Eyes_Closed_R] + // Use eye closed full range
                        expressionWeights[(int)FBExpression2.Eyes_Closed_R] * (2f * expressionWeights[(int)FBExpression2.Lid_Tightener_R] / Math.Pow(2f, 2f * expressionWeights[(int)FBExpression2.Lid_Tightener_R])), // Add lid tighener as the eye closes to help winking.
                        0f, 1f
                    );
                }   break;

                case FBEyeOpennessMode.SmoothTransition:
                {
                    // Compute eye closed values with Lid Tightener factored in
                    float eyeClosedL = Math.Min(1f, expressionWeights[(int)FBExpression2.Eyes_Closed_L] + expressionWeights[(int)FBExpression2.Lid_Tightener_L] * 0.5f);
                    float eyeClosedR = Math.Min(1f, expressionWeights[(int)FBExpression2.Eyes_Closed_R] + expressionWeights[(int)FBExpression2.Lid_Tightener_R] * 0.5f);

                    // Convert from Eye Closed to Eye Openness using a sigmoid function for a smooth transition
                    float openessL = (float)(1.0 / (1.0 + Math.Exp(-5.0 * ((1 - eyeClosedL) - 0.5))));
                    float openessR = (float)(1.0 / (1.0 + Math.Exp(-5.0 * ((1 - eyeClosedR) - 0.5))));

                    eye.Left.Openness = Math.Min(1.0f, openessL);
                    eye.Right.Openness = Math.Min(1.0f, openessR);

                }   break;

                case FBEyeOpennessMode.MultiExpression:
                {
                    // Recover true eye closed values; as you look down the eye closes.
                    // from FaceTrackingSystem.CS from Movement Aura Scene in https://github.com/oculus-samples/Unity-Movement
                    float eyeClosedL = Math.Min(1f, expressionWeights[(int)FBExpression2.Eyes_Closed_L] + expressionWeights[(int)FBExpression2.Eyes_Look_Down_L] * 0.5f);
                    float eyeClosedR = Math.Min(1f, expressionWeights[(int)FBExpression2.Eyes_Closed_R] + expressionWeights[(int)FBExpression2.Eyes_Look_Down_R] * 0.5f);

                    // Add Lid tightener to eye lid close to help get value closed
                    eyeClosedL = Math.Min(1f, eyeClosedL + expressionWeights[(int)FBExpression2.Lid_Tightener_L] * 0.5f);
                    eyeClosedR = Math.Min(1f, eyeClosedR + expressionWeights[(int)FBExpression2.Lid_Tightener_R] * 0.5f);

                    // Convert from Eye Closed to Eye Openness and limit from going negative. Set the max higher than normal to offset the eye lid to help keep eye lid open.
                    float openessL = Math.Clamp(1.1f - eyeClosedL * TrackingSensitivity.EyeLid, 0f, 1f);
                    float openessR = Math.Clamp(1.1f - eyeClosedR * TrackingSensitivity.EyeLid, 0f, 1f);

                    // As eye opens there is an issue flickering between eye wide and eye not fully open with the combined eye lid parameters. Need to reduce the eye widen value until openess is closer to value of 1. When not fully open will do constant value to reduce the eye widen.
                    float eyeWidenL = Math.Max(0f, expressionWeights[(int)FBExpression2.Upper_Lid_Raiser_L] * TrackingSensitivity.EyeWiden - 3.0f * (1f - openessL));
                    float eyeWidenR = Math.Max(0f, expressionWeights[(int)FBExpression2.Upper_Lid_Raiser_R] * TrackingSensitivity.EyeWiden - 3.0f * (1f - openessR));

                    // Feedback eye widen to openess, this will help drive the openness value higher from eye widen values
                    openessL += eyeWidenL;
                    openessR += eyeWidenR;

                    eye.Left.Openness = Math.Min(1f, openessL);
                    eye.Right.Openness = Math.Min(1f, openessR);
                }   break;

                default:
                {
                    eye.Left.Openness = 1.0f;
                    eye.Right.Openness = 1.0f;
                }   break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeExpressionsFBV2(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region V2 Eye Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Upper_Lid_Raiser_L] * TrackingSensitivity.EyeWiden);
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Upper_Lid_Raiser_R] * TrackingSensitivity.EyeWiden);

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lid_Tightener_L] * TrackingSensitivity.EyeSquint);
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lid_Tightener_R] * TrackingSensitivity.EyeSquint);

            #endregion

            #region V2 Brow Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Inner_Brow_Raiser_L] * TrackingSensitivity.BrowInnerUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Inner_Brow_Raiser_R] * TrackingSensitivity.BrowInnerUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Outer_Brow_Raiser_L] * TrackingSensitivity.BrowOuterUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Outer_Brow_Raiser_R] * TrackingSensitivity.BrowOuterUp);

            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Brow_Lowerer_L] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Brow_Lowerer_L] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Brow_Lowerer_R] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Brow_Lowerer_R] * TrackingSensitivity.BrowDown);

            #endregion
        }

        // Thank you @adjerry on the VRCFT discord for these conversions! https://docs.google.com/spreadsheets/d/118jo960co3Mgw8eREFVBsaJ7z0GtKNr52IB4Bz99VTA/edit#gid=0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateMouthExpressionsFBV2(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region V2 Jaw Expression Set                        
            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Jaw_Drop] * TrackingSensitivity.JawOpen);
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Jaw_Sideways_Left] * TrackingSensitivity.JawX);
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Jaw_Sideways_Right] * TrackingSensitivity.JawX);
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Jaw_Thrust] * TrackingSensitivity.JawForward);
            #endregion

            #region V2 Mouth Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = expressions[(int)FBExpression2.Lips_Toward];

            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Mouth_Left] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Mouth_Left] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Mouth_Right] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Mouth_Right] * TrackingSensitivity.MouthX);

            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Corner_Puller_L] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Corner_Puller_L] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Corner_Puller_R] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Corner_Puller_R] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Corner_Depressor_L] * TrackingSensitivity.MouthFrown);
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Corner_Depressor_R] * TrackingSensitivity.MouthFrown);

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression2.Lower_Lip_Depressor_L] * TrackingSensitivity.MouthLowerDown);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression2.Lower_Lip_Depressor_R] * TrackingSensitivity.MouthLowerDown);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight      = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression2.Upper_Lip_Raiser_L], expressions[(int)FBExpression2.Nose_Wrinkler_L])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight  = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression2.Upper_Lip_Raiser_L], expressions[(int)FBExpression2.Nose_Wrinkler_L])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight     = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression2.Upper_Lip_Raiser_R], expressions[(int)FBExpression2.Nose_Wrinkler_R])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression2.Upper_Lip_Raiser_R], expressions[(int)FBExpression2.Nose_Wrinkler_R])); // Workaround for wierd tracking quirk.

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Chin_Raiser_T] * TrackingSensitivity.ChinRaiserTop);
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Chin_Raiser_B] * TrackingSensitivity.ChinRaiserBottom);

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Dimpler_L] * TrackingSensitivity.MouthDimpler);
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Dimpler_R] * TrackingSensitivity.MouthDimpler);

            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Tightener_L] * TrackingSensitivity.MouthTightener);
            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Tightener_R] * TrackingSensitivity.MouthTightener);

            unifiedExpressions[(int)UnifiedExpressions.MouthPressLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Pressor_L] * TrackingSensitivity.MouthPress);
            unifiedExpressions[(int)UnifiedExpressions.MouthPressRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Pressor_R] * TrackingSensitivity.MouthPress);

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Stretcher_L] * TrackingSensitivity.MouthStretch);
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Stretcher_R] * TrackingSensitivity.MouthStretch);
            #endregion

            #region V2 Lip Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Pucker_R] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Pucker_R] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Pucker_L] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Pucker_L] * TrackingSensitivity.LipPucker);

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Funneler_LT] * TrackingSensitivity.LipFunnelTop);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Funneler_RT] * TrackingSensitivity.LipFunnelTop);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Funneler_LB] * TrackingSensitivity.LipFunnelBottom);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Lip_Funneler_RB] * TrackingSensitivity.LipFunnelBottom);

            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckTop * Math.Min(1f - (float)Math.Pow(expressions[(int)FBExpression2.Upper_Lip_Raiser_L], 1f / 6f), expressions[(int)FBExpression2.Lip_Suck_LT]));
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckTop * Math.Min(1f - (float)Math.Pow(expressions[(int)FBExpression2.Upper_Lip_Raiser_R], 1f / 6f), expressions[(int)FBExpression2.Lip_Suck_RT]));
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckBottom * expressions[(int)FBExpression2.Lip_Suck_LB]);
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckBottom * expressions[(int)FBExpression2.Lip_Suck_RB]);
            #endregion

            #region V2 Cheek Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Cheek_Puff_L] * TrackingSensitivity.CheekPuff);
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Cheek_Puff_R] * TrackingSensitivity.CheekPuff);
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Cheek_Suck_L] * TrackingSensitivity.CheekSuck);
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Cheek_Suck_R] * TrackingSensitivity.CheekSuck);
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Cheek_Raiser_L] * TrackingSensitivity.CheekRaiser);
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Cheek_Raiser_R] * TrackingSensitivity.CheekRaiser);
            #endregion

            #region V2 Nose Expression Set             
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerLeft].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Nose_Wrinkler_L] * TrackingSensitivity.NoseSneer);
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression2.Nose_Wrinkler_R] * TrackingSensitivity.NoseSneer);
            #endregion

            #region V2 Tongue Expression Set
            unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = expressions[(int)FBExpression2.Tongue_Out];
            unifiedExpressions[(int)UnifiedExpressions.TongueCurlUp].Weight = expressions[(int)FBExpression2.Tongue_Tip_alveolar];
            // no current mappings
            // unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = expressions[(int)FBExpression2.Tongue_Tip_Interdental];
            // unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = expressions[(int)FBExpression2.Tongue_Front_Dorsal_Palate];
            // unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = expressions[(int)FBExpression2.Tongue_Mid_Dorsal_Palate];
            // unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = expressions[(int)FBExpression2.Tongue_Back_Dorsal_velar];
            // unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = expressions[(int)FBExpression2.Tongue_Retreat];
            #endregion
        }
        #endregion

        #region FB Eye & Facial Update Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeDataFB(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            if (UseEyeExpressionForGazePose && packet.expressionType == ALXRFacialExpressionType.FB)
            {
                var expressions = packet.ExpressionWeightSpan;
                eye.Left.Gaze = MakeEye(
                    expressions[(int)FBExpression.Eyes_Look_Left_L],
                    expressions[(int)FBExpression.Eyes_Look_Right_L],
                    expressions[(int)FBExpression.Eyes_Look_Up_L],
                    expressions[(int)FBExpression.Eyes_Look_Down_L]
                );
                eye.Right.Gaze = MakeEye(
                    expressions[(int)FBExpression.Eyes_Look_Left_R],
                    expressions[(int)FBExpression.Eyes_Look_Right_R],
                    expressions[(int)FBExpression.Eyes_Look_Up_R],
                    expressions[(int)FBExpression.Eyes_Look_Down_R]
                );
            }
            else // Default use eye gaze pose.
            {
                UpdateEyeGaze(ref eye, ref packet);
            }

            // Eye dilation code, automated process maybe?
            eye.Left.PupilDiameter_MM  = 5f;
            eye.Right.PupilDiameter_MM = 5f;

            // Force the normalization values of Dilation to fit avg. pupil values.
            eye._minDilation = 0;
            eye._maxDilation = 10;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateEyeOpenessFB(ref UnifiedEyeData eye, ReadOnlySpan<float> expressionWeights)
        { 
            switch (FBEyeOpennessMode)
            {
                case FBEyeOpennessMode.LinearLidTightening:
                { 
                    eye.Left.Openness = 1f - Math.Clamp
                    (
                        expressionWeights[(int)FBExpression.Eyes_Closed_L] + expressionWeights[(int)FBExpression.Eyes_Closed_L] * expressionWeights[(int)FBExpression.Lid_Tightener_L],
                        0f, 1f
                    );
                    eye.Right.Openness = 1f - Math.Clamp
                    (
                        expressionWeights[(int)FBExpression.Eyes_Closed_R] + expressionWeights[(int)FBExpression.Eyes_Closed_R] * expressionWeights[(int)FBExpression.Lid_Tightener_R],
                        0f, 1f
                    );
                }   break;

                case FBEyeOpennessMode.NonLinearLidTightening:
                { 
                    eye.Left.Openness = 1f - (float)Math.Clamp
                    (
                        expressionWeights[(int)FBExpression.Eyes_Closed_L] + // Use eye closed full range
                        expressionWeights[(int)FBExpression.Eyes_Closed_L] * (2f * expressionWeights[(int)FBExpression.Lid_Tightener_L] / Math.Pow(2f, 2f * expressionWeights[(int)FBExpression.Lid_Tightener_L])), // Add lid tighener as the eye closes to help winking.
                        0f, 1f
                    );

                    eye.Right.Openness = 1f - (float)Math.Clamp
                    (
                        expressionWeights[(int)FBExpression.Eyes_Closed_R] + // Use eye closed full range
                        expressionWeights[(int)FBExpression.Eyes_Closed_R] * (2f * expressionWeights[(int)FBExpression.Lid_Tightener_R] / Math.Pow(2f, 2f * expressionWeights[(int)FBExpression.Lid_Tightener_R])), // Add lid tighener as the eye closes to help winking.
                        0f, 1f
                    );                    
                }   break;

                case FBEyeOpennessMode.SmoothTransition:
                {
                    // Compute eye closed values with Lid Tightener factored in
                    float eyeClosedL = Math.Min(1f, expressionWeights[(int)FBExpression.Eyes_Closed_L] + expressionWeights[(int)FBExpression.Lid_Tightener_L] * 0.5f);
                    float eyeClosedR = Math.Min(1f, expressionWeights[(int)FBExpression.Eyes_Closed_R] + expressionWeights[(int)FBExpression.Lid_Tightener_R] * 0.5f);

                    // Convert from Eye Closed to Eye Openness using a sigmoid function for a smooth transition
                    float openessL = (float)(1.0 / (1.0 + Math.Exp(-5.0 * ((1 - eyeClosedL) - 0.5))));
                    float openessR = (float)(1.0 / (1.0 + Math.Exp(-5.0 * ((1 - eyeClosedR) - 0.5))));

                    eye.Left.Openness = Math.Min(1.0f, openessL);
                    eye.Right.Openness = Math.Min(1.0f, openessR);

                }   break;

                case FBEyeOpennessMode.MultiExpression:
                {
                    // Recover true eye closed values; as you look down the eye closes.
                    // from FaceTrackingSystem.CS from Movement Aura Scene in https://github.com/oculus-samples/Unity-Movement
                    float eyeClosedL = Math.Min(1f, expressionWeights[(int)FBExpression.Eyes_Closed_L] + expressionWeights[(int)FBExpression.Eyes_Look_Down_L] * 0.5f);
                    float eyeClosedR = Math.Min(1f, expressionWeights[(int)FBExpression.Eyes_Closed_R] + expressionWeights[(int)FBExpression.Eyes_Look_Down_R] * 0.5f);
                    
                    // Add Lid tightener to eye lid close to help get value closed
                    eyeClosedL = Math.Min(1f, eyeClosedL + expressionWeights[(int)FBExpression.Lid_Tightener_L] * 0.5f);
                    eyeClosedR = Math.Min(1f, eyeClosedR + expressionWeights[(int)FBExpression.Lid_Tightener_R] * 0.5f);
                    
                    // Convert from Eye Closed to Eye Openness and limit from going negative. Set the max higher than normal to offset the eye lid to help keep eye lid open.
                    float openessL = Math.Clamp(1.1f - eyeClosedL * TrackingSensitivity.EyeLid, 0f, 1f);
                    float openessR = Math.Clamp(1.1f - eyeClosedR * TrackingSensitivity.EyeLid, 0f, 1f);
                    
                    // As eye opens there is an issue flickering between eye wide and eye not fully open with the combined eye lid parameters. Need to reduce the eye widen value until openess is closer to value of 1. When not fully open will do constant value to reduce the eye widen.
                    float eyeWidenL = Math.Max(0f, expressionWeights[(int)FBExpression.Upper_Lid_Raiser_L] * TrackingSensitivity.EyeWiden - 3.0f * (1f - openessL));
                    float eyeWidenR = Math.Max(0f, expressionWeights[(int)FBExpression.Upper_Lid_Raiser_R] * TrackingSensitivity.EyeWiden - 3.0f * (1f - openessR));
                    
                    // Feedback eye widen to openess, this will help drive the openness value higher from eye widen values
                    openessL += eyeWidenL;
                    openessR += eyeWidenR;

                    eye.Left.Openness  = Math.Min(1f, openessL);
                    eye.Right.Openness = Math.Min(1f, openessR);
                }   break;

                default:
                { 
                    eye.Left.Openness = 1.0f;
                    eye.Right.Openness = 1.0f;
                }   break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeExpressionsFB(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region Eye Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Upper_Lid_Raiser_L] * TrackingSensitivity.EyeWiden);
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Upper_Lid_Raiser_R] * TrackingSensitivity.EyeWiden);

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lid_Tightener_L] * TrackingSensitivity.EyeSquint);
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lid_Tightener_R] * TrackingSensitivity.EyeSquint);

            #endregion

            #region Brow Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Inner_Brow_Raiser_L] * TrackingSensitivity.BrowInnerUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Inner_Brow_Raiser_R] * TrackingSensitivity.BrowInnerUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Outer_Brow_Raiser_L] * TrackingSensitivity.BrowOuterUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Outer_Brow_Raiser_R] * TrackingSensitivity.BrowOuterUp);

            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Brow_Lowerer_L] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Brow_Lowerer_L] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Brow_Lowerer_R] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Brow_Lowerer_R] * TrackingSensitivity.BrowDown);

            #endregion
        }

        // Thank you @adjerry on the VRCFT discord for these conversions! https://docs.google.com/spreadsheets/d/118jo960co3Mgw8eREFVBsaJ7z0GtKNr52IB4Bz99VTA/edit#gid=0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateMouthExpressionsFB(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region Jaw Expression Set                        
            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Jaw_Drop] * TrackingSensitivity.JawOpen);
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Jaw_Sideways_Left] * TrackingSensitivity.JawX);
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Jaw_Sideways_Right] * TrackingSensitivity.JawX);
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Jaw_Thrust] * TrackingSensitivity.JawForward);
            #endregion

            #region Mouth Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = expressions[(int)FBExpression.Lips_Toward];

            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Mouth_Left] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Mouth_Left] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Mouth_Right] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Mouth_Right] * TrackingSensitivity.MouthX);

            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Corner_Puller_L] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Corner_Puller_L] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Corner_Puller_R] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Corner_Puller_R] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Corner_Depressor_L] * TrackingSensitivity.MouthFrown);
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Corner_Depressor_R] * TrackingSensitivity.MouthFrown);

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Lower_Lip_Depressor_L] * TrackingSensitivity.MouthLowerDown);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Lower_Lip_Depressor_R] * TrackingSensitivity.MouthLowerDown);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight      = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression.Upper_Lip_Raiser_L], expressions[(int)FBExpression.Nose_Wrinkler_L])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight  = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression.Upper_Lip_Raiser_L], expressions[(int)FBExpression.Nose_Wrinkler_L])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight     = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression.Upper_Lip_Raiser_R], expressions[(int)FBExpression.Nose_Wrinkler_R])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)FBExpression.Upper_Lip_Raiser_R], expressions[(int)FBExpression.Nose_Wrinkler_R])); // Workaround for wierd tracking quirk.

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Chin_Raiser_T] * TrackingSensitivity.ChinRaiserTop);
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Chin_Raiser_B] * TrackingSensitivity.ChinRaiserBottom);

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Dimpler_L] * TrackingSensitivity.MouthDimpler);
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Dimpler_R] * TrackingSensitivity.MouthDimpler);

            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Tightener_L] * TrackingSensitivity.MouthTightener);
            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Tightener_R] * TrackingSensitivity.MouthTightener);

            unifiedExpressions[(int)UnifiedExpressions.MouthPressLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Pressor_L] * TrackingSensitivity.MouthPress);
            unifiedExpressions[(int)UnifiedExpressions.MouthPressRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Pressor_R] * TrackingSensitivity.MouthPress);

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Stretcher_L] * TrackingSensitivity.MouthStretch);
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Stretcher_R] * TrackingSensitivity.MouthStretch);
            #endregion

            #region Lip Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Pucker_R] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Pucker_R] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Pucker_L] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Pucker_L] * TrackingSensitivity.LipPucker);

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Funneler_LT] * TrackingSensitivity.LipFunnelTop);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Funneler_RT] * TrackingSensitivity.LipFunnelTop);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Funneler_LB] * TrackingSensitivity.LipFunnelBottom);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Lip_Funneler_RB] * TrackingSensitivity.LipFunnelBottom);

            //unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = expressions[(int)FBExpression.Lip_Suck_LT];
            //unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = expressions[(int)FBExpression.Lip_Suck_RT];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight  = Math.Min(1.0f, TrackingSensitivity.LipSuckTop * Math.Min(1f - (float)Math.Pow(expressions[(int)FBExpression.Upper_Lip_Raiser_L], 1f / 6f), expressions[(int)FBExpression.Lip_Suck_LT]));
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckTop * Math.Min(1f - (float)Math.Pow(expressions[(int)FBExpression.Upper_Lip_Raiser_R], 1f / 6f), expressions[(int)FBExpression.Lip_Suck_RT]));
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight  = Math.Min(1.0f, TrackingSensitivity.LipSuckBottom * expressions[(int)FBExpression.Lip_Suck_LB]);
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckBottom * expressions[(int)FBExpression.Lip_Suck_RB]);
            #endregion

            #region Cheek Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Cheek_Puff_L] * TrackingSensitivity.CheekPuff);
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Cheek_Puff_R] * TrackingSensitivity.CheekPuff);
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight    = Math.Min(1.0f, expressions[(int)FBExpression.Cheek_Suck_L] * TrackingSensitivity.CheekSuck);
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight   = Math.Min(1.0f, expressions[(int)FBExpression.Cheek_Suck_R] * TrackingSensitivity.CheekSuck);
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Cheek_Raiser_L] * TrackingSensitivity.CheekRaiser);
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Cheek_Raiser_R] * TrackingSensitivity.CheekRaiser);
            #endregion

            #region Nose Expression Set             
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerLeft].Weight  = Math.Min(1.0f, expressions[(int)FBExpression.Nose_Wrinkler_L] * TrackingSensitivity.NoseSneer);
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerRight].Weight = Math.Min(1.0f, expressions[(int)FBExpression.Nose_Wrinkler_R] * TrackingSensitivity.NoseSneer);
            #endregion

            #region Tongue Expression Set   
            //Future placeholder
            unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = 0f;
            #endregion
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 MakeEye(float LookLeft, float LookRight, float LookUp, float LookDown) =>
            new Vector2(LookRight - LookLeft, LookUp - LookDown) * 0.5f;
        #endregion

        #region Android Avatar Eyes & Facial Update Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeDataANDROID(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            if (UseEyeExpressionForGazePose && packet.expressionType == ALXRFacialExpressionType.Android)
            {
                var expressions = packet.ExpressionWeightSpan;
                eye.Left.Gaze = MakeEye(
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Left_L],
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Right_L],
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Up_L],
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Down_L]
                );
                eye.Right.Gaze = MakeEye(
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Left_R],
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Right_R],
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Up_R],
                    expressions[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Down_R]
                );
            }
            else // Default use eye gaze pose.
            {
                UpdateEyeGaze(ref eye, ref packet);
            }

            // Eye dilation code, automated process maybe?
            eye.Left.PupilDiameter_MM = 5f;
            eye.Right.PupilDiameter_MM = 5f;

            // Force the normalization values of Dilation to fit avg. pupil values.
            eye._minDilation = 0;
            eye._maxDilation = 10;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateEyeOpenessANDROID(ref UnifiedEyeData eye, ReadOnlySpan<float> expressionWeights)
        {
            switch (FBEyeOpennessMode)
            {
                case FBEyeOpennessMode.LinearLidTightening:
                {
                    eye.Left.Openness = 1f - Math.Clamp
                    (
                        expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_L] + expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_L] * expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_L],
                        0f, 1f
                    );
                    eye.Right.Openness = 1f - Math.Clamp
                    (
                        expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_R] + expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_R] * expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_R],
                        0f, 1f
                    );
                }
                break;

                case FBEyeOpennessMode.NonLinearLidTightening:
                {
                    eye.Left.Openness = 1f - (float)Math.Clamp
                    (
                        expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_L] + // Use eye closed full range
                        expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_L] * (2f * expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_L] / Math.Pow(2f, 2f * expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_L])), // Add lid tighener as the eye closes to help winking.
                        0f, 1f
                    );

                    eye.Right.Openness = 1f - (float)Math.Clamp
                    (
                        expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_R] + // Use eye closed full range
                        expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_R] * (2f * expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_R] / Math.Pow(2f, 2f * expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_R])), // Add lid tighener as the eye closes to help winking.
                        0f, 1f
                    );
                }
                break;

                case FBEyeOpennessMode.SmoothTransition:
                {
                    // Compute eye closed values with Lid Tightener factored in
                    float eyeClosedL = Math.Min(1f, expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_L] + expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_L] * 0.5f);
                    float eyeClosedR = Math.Min(1f, expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_R] + expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_R] * 0.5f);

                    // Convert from Eye Closed to Eye Openness using a sigmoid function for a smooth transition
                    float openessL = (float)(1.0 / (1.0 + Math.Exp(-5.0 * ((1 - eyeClosedL) - 0.5))));
                    float openessR = (float)(1.0 / (1.0 + Math.Exp(-5.0 * ((1 - eyeClosedR) - 0.5))));

                    eye.Left.Openness = Math.Min(1.0f, openessL);
                    eye.Right.Openness = Math.Min(1.0f, openessR);

                }
                break;

                case FBEyeOpennessMode.MultiExpression:
                {
                    // Recover true eye closed values; as you look down the eye closes.
                    // from FaceTrackingSystem.CS from Movement Aura Scene in https://github.com/oculus-samples/Unity-Movement
                    float eyeClosedL = Math.Min(1f, expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_L] + expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Down_L] * 0.5f);
                    float eyeClosedR = Math.Min(1f, expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Closed_R] + expressionWeights[(int)XrFaceParameterIndicesANDROID.Eyes_Look_Down_R] * 0.5f);

                    // Add Lid tightener to eye lid close to help get value closed
                    eyeClosedL = Math.Min(1f, eyeClosedL + expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_L] * 0.5f);
                    eyeClosedR = Math.Min(1f, eyeClosedR + expressionWeights[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_R] * 0.5f);

                    // Convert from Eye Closed to Eye Openness and limit from going negative. Set the max higher than normal to offset the eye lid to help keep eye lid open.
                    float openessL = Math.Clamp(1.1f - eyeClosedL * TrackingSensitivity.EyeLid, 0f, 1f);
                    float openessR = Math.Clamp(1.1f - eyeClosedR * TrackingSensitivity.EyeLid, 0f, 1f);

                    // As eye opens there is an issue flickering between eye wide and eye not fully open with the combined eye lid parameters. Need to reduce the eye widen value until openess is closer to value of 1. When not fully open will do constant value to reduce the eye widen.
                    float eyeWidenL = Math.Max(0f, expressionWeights[(int)XrFaceParameterIndicesANDROID.Upper_Lid_Raiser_L] * TrackingSensitivity.EyeWiden - 3.0f * (1f - openessL));
                    float eyeWidenR = Math.Max(0f, expressionWeights[(int)XrFaceParameterIndicesANDROID.Upper_Lid_Raiser_R] * TrackingSensitivity.EyeWiden - 3.0f * (1f - openessR));

                    // Feedback eye widen to openess, this will help drive the openness value higher from eye widen values
                    openessL += eyeWidenL;
                    openessR += eyeWidenR;

                    eye.Left.Openness = Math.Min(1f, openessL);
                    eye.Right.Openness = Math.Min(1f, openessR);
                }
                break;

                default:
                {
                    eye.Left.Openness = 1.0f;
                    eye.Right.Openness = 1.0f;
                }
                break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateEyeExpressionsANDROID(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region Eye Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lid_Raiser_L] * TrackingSensitivity.EyeWiden);
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lid_Raiser_R] * TrackingSensitivity.EyeWiden);

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_L] * TrackingSensitivity.EyeSquint);
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lid_Tightener_R] * TrackingSensitivity.EyeSquint);

            #endregion

            #region Brow Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Inner_Brow_Raiser_L] * TrackingSensitivity.BrowInnerUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Inner_Brow_Raiser_R] * TrackingSensitivity.BrowInnerUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Outer_Brow_Raiser_L] * TrackingSensitivity.BrowOuterUp);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Outer_Brow_Raiser_R] * TrackingSensitivity.BrowOuterUp);

            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Brow_Lowerer_L] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Brow_Lowerer_L] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Brow_Lowerer_R] * TrackingSensitivity.BrowDown);
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Brow_Lowerer_R] * TrackingSensitivity.BrowDown);

            #endregion
        }

        // Thank you @adjerry on the VRCFT discord for these conversions! https://docs.google.com/spreadsheets/d/118jo960co3Mgw8eREFVBsaJ7z0GtKNr52IB4Bz99VTA/edit#gid=0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateMouthExpressionsANDROID(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region Jaw Expression Set                        
            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Jaw_Drop] * TrackingSensitivity.JawOpen);
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Jaw_Sideways_Left] * TrackingSensitivity.JawX);
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Jaw_Sideways_Right] * TrackingSensitivity.JawX);
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Jaw_Thrust] * TrackingSensitivity.JawForward);
            #endregion

            #region Mouth Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = expressions[(int)XrFaceParameterIndicesANDROID.Lips_Toward];

            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Mouth_Left] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Mouth_Left] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Mouth_Right] * TrackingSensitivity.MouthX);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Mouth_Right] * TrackingSensitivity.MouthX);

            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Corner_Puller_L] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Corner_Puller_L] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Corner_Puller_R] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Corner_Puller_R] * TrackingSensitivity.MouthSmile);
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Corner_Depressor_L] * TrackingSensitivity.MouthFrown);
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Corner_Depressor_R] * TrackingSensitivity.MouthFrown);

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lower_Lip_Depressor_L] * TrackingSensitivity.MouthLowerDown);
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lower_Lip_Depressor_R] * TrackingSensitivity.MouthLowerDown);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lip_Raiser_L], expressions[(int)XrFaceParameterIndicesANDROID.Nose_Wrinkler_L])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lip_Raiser_L], expressions[(int)XrFaceParameterIndicesANDROID.Nose_Wrinkler_L])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lip_Raiser_R], expressions[(int)XrFaceParameterIndicesANDROID.Nose_Wrinkler_R])); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = Math.Min(1.0f, TrackingSensitivity.MouthUpperUp * Math.Max(expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lip_Raiser_R], expressions[(int)XrFaceParameterIndicesANDROID.Nose_Wrinkler_R])); // Workaround for wierd tracking quirk.

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Chin_Raiser_T] * TrackingSensitivity.ChinRaiserTop);
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Chin_Raiser_B] * TrackingSensitivity.ChinRaiserBottom);

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Dimpler_L] * TrackingSensitivity.MouthDimpler);
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Dimpler_R] * TrackingSensitivity.MouthDimpler);

            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Tightener_L] * TrackingSensitivity.MouthTightener);
            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Tightener_R] * TrackingSensitivity.MouthTightener);

            unifiedExpressions[(int)UnifiedExpressions.MouthPressLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Pressor_L] * TrackingSensitivity.MouthPress);
            unifiedExpressions[(int)UnifiedExpressions.MouthPressRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Pressor_R] * TrackingSensitivity.MouthPress);

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Stretcher_L] * TrackingSensitivity.MouthStretch);
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Stretcher_R] * TrackingSensitivity.MouthStretch);
            #endregion

            #region Lip Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Pucker_R] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Pucker_R] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Pucker_L] * TrackingSensitivity.LipPucker);
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Pucker_L] * TrackingSensitivity.LipPucker);

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Funneler_LT] * TrackingSensitivity.LipFunnelTop);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Funneler_RT] * TrackingSensitivity.LipFunnelTop);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Funneler_LB] * TrackingSensitivity.LipFunnelBottom);
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Lip_Funneler_RB] * TrackingSensitivity.LipFunnelBottom);

            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckTop * Math.Min(1f - (float)Math.Pow(expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lip_Raiser_L], 1f / 6f), expressions[(int)XrFaceParameterIndicesANDROID.Lip_Suck_LT]));
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckTop * Math.Min(1f - (float)Math.Pow(expressions[(int)XrFaceParameterIndicesANDROID.Upper_Lip_Raiser_R], 1f / 6f), expressions[(int)XrFaceParameterIndicesANDROID.Lip_Suck_RT]));
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckBottom * expressions[(int)XrFaceParameterIndicesANDROID.Lip_Suck_LB]);
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = Math.Min(1.0f, TrackingSensitivity.LipSuckBottom * expressions[(int)XrFaceParameterIndicesANDROID.Lip_Suck_RB]);
            #endregion

            #region Cheek Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Cheek_Puff_L] * TrackingSensitivity.CheekPuff);
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Cheek_Puff_R] * TrackingSensitivity.CheekPuff);
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Cheek_Suck_L] * TrackingSensitivity.CheekSuck);
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Cheek_Suck_R] * TrackingSensitivity.CheekSuck);
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Cheek_Raiser_L] * TrackingSensitivity.CheekRaiser);
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Cheek_Raiser_R] * TrackingSensitivity.CheekRaiser);
            #endregion

            #region Nose Expression Set             
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerLeft].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Nose_Wrinkler_L] * TrackingSensitivity.NoseSneer);
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerRight].Weight = Math.Min(1.0f, expressions[(int)XrFaceParameterIndicesANDROID.Nose_Wrinkler_R] * TrackingSensitivity.NoseSneer);
            #endregion
        }
        #endregion

        #endregion
    }
}