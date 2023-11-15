using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VRCFaceTracking;
using ALXR;
using LibALXR;
using static ALXR.ModuleUtils;

namespace ALXRRemoteModule
{
    public sealed class ALXRRemoteModule : ExtTrackingModule
    {
        private bool eyeActive = true;
        private bool lipActive = true;

        private ALXRModuleConfig config = new ALXRModuleConfig();
        private XrPosefOneEuroFilter[] poseFilters = new XrPosefOneEuroFilter[2]
        {
            new XrPosefOneEuroFilter(),
            new XrPosefOneEuroFilter(),
        };

        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            try
            {
                ModuleInformation.Name = "ALXR Remote Module";
                eyeActive = eyeAvailable;
                lipActive = expressionAvailable;
                config = LoadOrNewConfig(Logger);
                Debug.Assert(config != null);

                var filterParams = config.EyeTrackingConfig.EyeTrackingFilterParams.FilterParams;
                Array.ForEach(poseFilters, p => p.FilterParams = filterParams);

                LoadTrackingSensitivity(config, Logger);
                return (eyeActive, lipActive);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex.ToString());
                return (false, false);
            }
        }

        private TcpClient client = null;
        private NetworkStream stream = null;
        private const int ConnectionTimeoutMs = 2000;
        private Stopwatch stopwatch = new Stopwatch();

        public override void Update()
        {
            try
            {
                ConnectToServer();

                var newPacket = ReadALXRFacialEyePacket(stream);
                ApplyFilters(ref newPacket);
                UpdateData(ref newPacket, eyeActive, lipActive);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e.Message);
                client?.Close();
                stream = null;
                client = null;
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
            if (!config.EyeTrackingConfig.EyeTrackingFilterParams.Enable)
                return;
            var deltaTime = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            ApplyEyeGazeFilters(deltaTime, ref newPacket);
        }

        private void ConnectToServer()
        {
            if (client != null && client.Connected)
                return;

            client?.Close();
            client = new TcpClient()
            {
                NoDelay = true,
            };
            var remoteConfig = config.RemoteConfig;
            Logger.Log(LogLevel.Information, $"Attempting to establish a connection at {remoteConfig.ClientIpAddress}:{remoteConfig.PortNo}...");
            var result = client.BeginConnect(remoteConfig.ClientIpAddress, remoteConfig.PortNo, null, null);
            if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(ConnectionTimeoutMs)))
            {
                throw new Exception($"Establishing connection timed-out");
            }
            client.EndConnect(result);

            stream = client.GetStream();
            Debug.Assert(stream != null);
            stream.ReadTimeout = ConnectionTimeoutMs;

            Array.ForEach(poseFilters, x => x.Reset());

            Logger.Log(LogLevel.Information, $"Successfully connected to ALXR client: {remoteConfig.ClientIpAddress}:{remoteConfig.PortNo}");

            Debug.Assert(client != null && stream != null);
        }

        private byte[] rawExprBuffer = new byte[Marshal.SizeOf<ALXRFacialEyePacket>()];
        private ALXRFacialEyePacket ReadALXRFacialEyePacket(NetworkStream stream)
        {
            Debug.Assert(stream != null && stream.CanRead);

            int offset = 0;
            int readBytes = 0;
            do
            {
                readBytes = stream.Read(rawExprBuffer, offset, rawExprBuffer.Length - offset);
                offset += readBytes;
            }
            while (readBytes > 0 && offset < rawExprBuffer.Length);

            if (offset < rawExprBuffer.Length)
                throw new Exception("Failed read packet.");
            return ALXRFacialEyePacket.ReadPacket(rawExprBuffer);

        }

        public override void Teardown()
        {
            client?.Close();
            stream = null;
            client = null;
        }
    }
}
