using Bonsai.Harp.Net;
using System;
using System.Reactive;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Bonsai.Harp
{
    public static class DeviceProbe
    {
        public static async Task<string> GetDeviceName(ITransport transport, bool leaveOpen = false)
        {
            var tcs = new TaskCompletionSource<string>();

            LedState ledState = LedState.On;
            LedState visualIndicators = LedState.On;
            EnableFlag heartbeat = EnableFlag.Disabled;

            var writeOpCtrl = OperationControl.FromPayload(MessageType.Write, new OperationControlPayload(
                OperationMode.Standby,
                dumpRegisters: false,
                muteReplies: false,
                ledState,
                visualIndicators,
                heartbeat));
            var cmdReadWhoAmI = HarpCommand.ReadUInt16(WhoAmI.Address);
            var cmdReadVersion = HarpCommand.ReadByte(Version.Address);
            var cmdReadTimestampSeconds = HarpCommand.ReadUInt32(TimestampSeconds.Address);
            var cmdReadDeviceName = HarpCommand.ReadByte(DeviceName.Address);
            var cmdReadSerialNumber = HarpCommand.ReadUInt32(SerialNumber.Address);

            var whoAmI = 0;
            var timestamp = 0u;
            VersionPayload version = default;
            var serialNumber = default(ushort?);
            var messageObserver = Observer.Create<HarpMessage>(
                message =>
                {
                    switch (message.Address)
                    {
                        case OperationControl.Address:
                            transport.Write(cmdReadWhoAmI);
                            transport.Write(cmdReadVersion);
                            transport.Write(cmdReadTimestampSeconds);
                            transport.Write(cmdReadSerialNumber);
                            transport.Write(cmdReadDeviceName);
                            break;
                        case WhoAmI.Address:
                            whoAmI = WhoAmI.GetPayload(message);
                            if (whoAmI == 0) tcs.TrySetResult(string.Empty);
                            else transport.Write(cmdReadDeviceName);
                            break;
                        case Version.Address: if (!message.Error) version = Version.GetPayload(message); break;
                        case TimestampSeconds.Address: timestamp = TimestampSeconds.GetPayload(message); break;
                        case SerialNumber.Address: if (!message.Error) serialNumber = SerialNumber.GetPayload(message); break;
                        case DeviceName.Address:
                            var deviceName = nameof(Device);
                            if (!message.Error) deviceName = DeviceName.GetPayload(message);
                            if (transport is SerialTransport) Console.WriteLine("Serial Harp device.");
                            if (transport is TcpTransport) Console.WriteLine("TCP Harp device.");
                            if (!serialNumber.HasValue) Console.WriteLine($"WhoAmI: {whoAmI}");
                            else Console.WriteLine($"WhoAmI: {whoAmI}-{serialNumber:x4}");
                            Console.WriteLine($"Hw: {version.HardwareVersion}");
                            Console.WriteLine($"Fw: {version.FirmwareVersion}");
                            Console.WriteLine($"Timestamp (s): {timestamp}");
                            Console.WriteLine($"DeviceName: {deviceName}");
                            Console.WriteLine();
                            tcs.TrySetResult(deviceName);
                            break;
                        default:
                            break;
                    }
                },
                ex => tcs.TrySetException(ex),
                () => { if (!tcs.Task.IsCompleted) tcs.TrySetCanceled(); });

            transport.SetObserver(messageObserver);

            try
            {
                transport.Write(writeOpCtrl);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(500)).ConfigureAwait(false);
                if (completed == tcs.Task) return await tcs.Task.ConfigureAwait(false);
            }
            catch { /*ignore*/ }
            finally
            {
                if (!leaveOpen)
                {
                    try { transport.Close(); } catch { }
                }
            }

            return string.Empty;
        }

        public static async Task<string> GetUid(ITransport transport, bool leaveOpen = false)
        {
            var tcs = new TaskCompletionSource<string>();

            LedState ledState = LedState.On;
            LedState visualIndicators = LedState.On;
            EnableFlag heartbeat = EnableFlag.Disabled;

            var writeOpCtrl = OperationControl.FromPayload(MessageType.Write, new OperationControlPayload(
                OperationMode.Standby,
                dumpRegisters: false,
                muteReplies: false,
                ledState,
                visualIndicators,
                heartbeat));
            var cmdReadDeviceUid = HarpCommand.ReadByte(Uid.Address);

            var messageObserver = Observer.Create<HarpMessage>(
                message =>
                {
                    switch (message.Address)
                    {
                        case OperationControl.Address:
                            transport.Write(cmdReadDeviceUid);
                            break;
                        case Uid.Address:
                            var uid = string.Empty;
                            if (!message.Error) uid = Uid.GetPayload(message);
                            tcs.TrySetResult(uid);
                            break;
                        default:
                            break;
                    }
                },
                ex => tcs.TrySetException(ex),
                () => { if (!tcs.Task.IsCompleted) tcs.TrySetCanceled(); });

            transport.SetObserver(messageObserver);

            try
            {
                transport.Write(writeOpCtrl);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(500)).ConfigureAwait(false);
                if (completed == tcs.Task) return await tcs.Task.ConfigureAwait(false);
            }
            catch { /*ignore*/ }
            finally
            {
                if (!leaveOpen)
                {
                    try { transport.Close(); } catch { }
                }
            }

            return string.Empty;
        }

        public static string GetReadableUid(string uid)
        {
            int readableUidLength = 8;
            var readableUid = string.Empty;
            if (string.IsNullOrEmpty(uid)) return readableUid;

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(uid));
                var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
                readableUid = hex.Length > readableUidLength ? hex.Substring(0, readableUidLength) : hex;
            }

            return readableUid;
        }
    }
}
