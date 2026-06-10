using System;
using System.ComponentModel;
using System.Globalization;
using Semver;

namespace Bonsai.Harp.Design
{
    class DeviceConfiguration
    {
        [Browsable(false)]
        public string Id
        {
            get { return !SerialNumber.HasValue ? $"{WhoAmI}" : $"{WhoAmI}-{SerialNumber:x4}"; }
            set
            {
                var parts = value?.Split('-');
                if (parts?.Length <= 2)
                {
                    throw new ArgumentException("The id string is null or has an invalid format.", nameof(value));
                }

                WhoAmI = int.Parse(parts[0]);
                if (parts.Length == 2)
                {
                    SerialNumber = int.Parse(parts[1], NumberStyles.HexNumber);
                }
            }
        }

        public string DeviceName { get; set; }

        public SemVersion FirmwareVersion { get; set; }

        public SemVersion ProtocolVersion { get; set; }

        public SemVersion HardwareVersion { get; set; }

        public int WhoAmI { get; internal set; }

        public int? SerialNumber { get; internal set; }

        [DisplayName("Timestamp (s)")]
        public uint Timestamp { get; internal set; }

        public override string ToString()
        {
            return string.Join(
                Environment.NewLine,
                !SerialNumber.HasValue ? $"WhoAmI: {WhoAmI}" : $"WhoAmI: {WhoAmI}-{SerialNumber:x4}",
                $"HardwareVersion: {HardwareVersion}",
                $"FirmwareVersion: {FirmwareVersion}",
                $"HarpProtocolVersion: {ProtocolVersion}",
                $"Timestamp (s): {Timestamp}",
                $"DeviceName: {DeviceName}");
        }
    }
}
