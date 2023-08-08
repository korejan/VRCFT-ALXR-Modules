using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Reflection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System;
using System.Threading;

namespace ALXR
{
    public sealed class FBTrackingSensitivity
    {
        // Tracking Sensitivity Multipliers
        public float EyeLid          { get;set; } = 1.0f;
        public float EyeSquint       { get;set; } = 1.0f;
        public float EyeWiden        { get;set; } = 1.0f;
        public float BrowInnerUp     { get;set; } = 1.0f;
        public float BrowOuterUp     { get;set; } = 1.0f;
        public float BrowDown        { get;set; } = 1.0f;
        public float CheekPuff       { get;set; } = 1.0f;
        public float CheekSuck       { get;set; } = 1.0f;
        public float CheekRaiser     { get;set; } = 1.0f;
        public float JawOpen         { get;set; } = 1.0f;
        public float MouthApeShape   { get;set; } = 1.0f;
        public float JawX            { get;set; } = 1.0f;
        public float JawForward      { get;set; } = 1.0f;
        public float LipPucker       { get;set; } = 1.0f;
        public float MouthX          { get;set; } = 1.0f;
        public float MouthSmile      { get;set; } = 1.0f;
        public float MouthFrown      { get;set; } = 1.0f;
        public float LipFunnelTop    { get;set; } = 1.0f;
        public float LipFunnelBottom { get;set; } = 1.0f;
        public float LipSuckTop      { get;set; } = 1.0f;
        public float LipSuckBottom   { get;set; } = 1.0f;
        public float ChinRaiserTop   { get;set; } = 1.0f;
        public float ChinRaiserBottom{ get;set; } = 1.0f;
        public float MouthLowerDown  { get;set; } = 1.0f;
        public float MouthUpperUp    { get;set; } = 1.0f;
        public float MouthDimpler    { get;set; } = 1.0f;
        public float MouthStretch    { get;set; } = 1.0f;
        public float MouthPress      { get;set; } = 1.0f;
        public float MouthTightener  { get;set; } = 1.0f;
        public float NoseSneer { get;set; } = 1.0f;

        public static FBTrackingSensitivity AdjerrysDefault
        {
            get
            {
                return new FBTrackingSensitivity()
                {
                    EyeLid           = 1.1f,  
                    EyeSquint        = 1.0f,      
                    EyeWiden         = 1.0f,      
                    BrowInnerUp      = 1.0f,          
                    BrowOuterUp      = 1.0f,          
                    BrowDown         = 1.0f,      
                    CheekPuff        = 1.4f,      
                    CheekSuck        = 2.72f,      
                    CheekRaiser      = 1.1f,          
                    JawOpen          = 1.1f,      
                    MouthApeShape    = 2.0f,          
                    JawX             = 1.0f,  
                    JawForward       = 1.0f,      
                    LipPucker        = 1.21f,      
                    MouthX           = 1.0f,  
                    MouthSmile       = 1.22f,      
                    MouthFrown       = 1.1f,      
                    LipFunnelTop     = 1.13f,          
                    LipFunnelBottom  = 8.0f, //VERY NOT SENSITIV,              
                    LipSuckTop       = 1.0f,      
                    LipSuckBottom    = 1.0f,          
                    ChinRaiserTop    = 0.75f,          
                    ChinRaiserBottom = 0.7f,              
                    MouthLowerDown   = 2.87f,          
                    MouthUpperUp     = 1.75f,          
                    MouthDimpler     = 4.3f,          
                    MouthStretch     = 3.0f,          
                    MouthPress       = 10f, //VERY NOT SENSITIV,      
                    MouthTightener   = 2.13f,          
                    NoseSneer        = 3.16f,
                };
            }
        }

        public void CopyTo(FBTrackingSensitivity dst)
        {
            if (dst == null)
                return;
            dst.EyeLid           = this.EyeLid;
            dst.EyeSquint        = this.EyeSquint;
            dst.EyeWiden         = this.EyeWiden;
            dst.BrowInnerUp      = this.BrowInnerUp;
            dst.BrowOuterUp      = this.BrowOuterUp;
            dst.BrowDown         = this.BrowDown;
            dst.CheekPuff        = this.CheekPuff;
            dst.CheekSuck        = this.CheekSuck;
            dst.CheekRaiser      = this.CheekRaiser;
            dst.JawOpen          = this.JawOpen;
            dst.MouthApeShape    = this.MouthApeShape;
            dst.JawX             = this.JawX;
            dst.JawForward       = this.JawForward;
            dst.LipPucker        = this.LipPucker;
            dst.MouthX           = this.MouthX;
            dst.MouthSmile       = this.MouthSmile;
            dst.MouthFrown       = this.MouthFrown;
            dst.LipFunnelTop     = this.LipFunnelTop;
            dst.LipFunnelBottom  = this.LipFunnelBottom;
            dst.LipSuckTop       = this.LipSuckTop;
            dst.LipSuckBottom    = this.LipSuckBottom;
            dst.ChinRaiserTop    = this.ChinRaiserTop;
            dst.ChinRaiserBottom = this.ChinRaiserBottom;
            dst.MouthLowerDown   = this.MouthLowerDown;
            dst.MouthUpperUp     = this.MouthUpperUp;
            dst.MouthDimpler     = this.MouthDimpler;
            dst.MouthStretch     = this.MouthStretch;
            dst.MouthPress       = this.MouthPress;
            dst.MouthTightener   = this.MouthTightener;
            dst.NoseSneer        = this.NoseSneer;
        }

        [JsonIgnore]
        private FileSystemWatcher fileWatcher = null;

        private void SetFileWatcher(string jsonFullPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(jsonFullPath));
            fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(jsonFullPath));
            Debug.Assert(fileWatcher != null);
            fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            fileWatcher.Changed += OnChanged;
            fileWatcher.Filter = Path.GetFileName(jsonFullPath);
            fileWatcher.IncludeSubdirectories = false;
            fileWatcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (e.ChangeType != WatcherChangeTypes.Changed)
                    return;
                Thread.Sleep(100);
                var result = ReadJsonFile(e.FullPath);
                if (result == null)
                    throw new Exception($"Failed read tracking sensitivity file: {e.FullPath}");
                result.CopyTo(this);
            } catch (Exception) { }
        }

        public static FBTrackingSensitivity LoadAndMonitor(ILogger logger, string filename)
        {
            try
            {
                if (logger == null || string.IsNullOrEmpty(filename))
                    return null;

                if (string.IsNullOrEmpty(Path.GetDirectoryName(filename)))
                {
                    var defaultDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    filename = Path.Combine(defaultDirectory, filename);
                }

                logger.LogInformation($"Attempting to load tracking sensitivity file: {filename}");
                if (!File.Exists(filename))
                {
                    logger.LogError($"Failed to find tracking sensitivity json, file: {filename} doest not exist.");
                    return null;
                }
                var result = ReadJsonFile(filename);
                if (result == null)
                    throw new Exception($"Failed read tracking sensitivity file: {filename}");

                result.SetFileWatcher(filename);

                logger.LogInformation($"Successfully loaded tracking sensitivity file.");

                return result;

            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to read alxr-config, reason: {ex.Message}");
                return null;
            }
        }

        public string ToJsonString()
        {
            var jsonOptions = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true
            };
            //AddJsonConverters(jsonOptions);
            return JsonSerializer.Serialize(this, jsonOptions);
        }

        public void WriteJsonFile(string filename) =>
            WriteJsonFile(this, filename);

        public static void WriteJsonFile(FBTrackingSensitivity config, string filename)
        {
            if (config == null)
                return;
            var jsonStr = config.ToJsonString();
            File.WriteAllText(filename, jsonStr);
        }

        public static FBTrackingSensitivity ReadJsonFile(string filename)
        {
            var jsonStr = File.ReadAllText(filename);
            var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
            //AddJsonConverters(jsonOptions);
            return JsonSerializer.Deserialize<FBTrackingSensitivity>(jsonStr, jsonOptions);
        }

    }
}
