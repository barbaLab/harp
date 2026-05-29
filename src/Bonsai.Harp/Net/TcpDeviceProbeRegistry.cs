using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Bonsai.Harp.Net
{
	static class TcpDeviceProbeRegistry
	{
		static readonly object syncRoot = new object();
		static readonly Dictionary<string, Dictionary<string, string>> connections = new Dictionary<string, Dictionary<string, string>>();

		public static void Add(string connectionName, string deviceName, string deviceIp)
		{
			if (string.IsNullOrEmpty(connectionName) || string.IsNullOrEmpty(deviceName)) return;
			var clientKey = CreateClientKey(connectionName, deviceIp, deviceName);

			lock (syncRoot)
			{
				Dictionary<string, string> devices;
				if (!connections.TryGetValue(connectionName, out devices))
				{
					devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					connections.Add(connectionName, devices);
				}

				devices[clientKey] = deviceName + " (" + clientKey + ")";
			}
		}

		public static void Remove(string connectionName, string deviceName, string deviceIp)
		{
			if (string.IsNullOrEmpty(connectionName) || string.IsNullOrEmpty(deviceName)) return;
			var clientKey = CreateClientKey(connectionName, deviceIp, deviceName);

			lock (syncRoot)
			{
				Dictionary<string, string> devices;
				if (!connections.TryGetValue(connectionName, out devices)) return;

				if (!devices.Remove(clientKey)) return;

				if (devices.Count == 0)
				{
					connections.Remove(connectionName);
				}
			}
		}

		public static void Clear(string connectionName)
		{
			if (string.IsNullOrEmpty(connectionName)) return;
			lock (syncRoot)
			{
				connections.Remove(connectionName);
			}
		}

		public static string[] GetDeviceNames(string connectionName)
		{
			if (string.IsNullOrEmpty(connectionName)) return Array.Empty<string>();

			lock (syncRoot)
			{
				var names = new List<string>();

				if (connections.TryGetValue(connectionName, out var devices))
				{
					foreach (var device in devices.Values) names.Add(device);
				}

				if (names.Count == 0) return Array.Empty<string>();

				var array = new string[names.Count];
				names.CopyTo(array);
				Array.Sort(array, StringComparer.OrdinalIgnoreCase);
				return array;
			}
		}

        // FIXME: retrieve Harp uid
		public static string CreateClientKey(string connectionName, string deviceIp, string deviceName)
		{
			var keySource = string.Concat(connectionName ?? string.Empty, "|", deviceIp ?? string.Empty, "|", deviceName ?? string.Empty);
			using (var sha256 = SHA256.Create())
			{
				var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keySource));
				var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
				return hex.Length > 8 ? hex.Substring(0, 8) : hex;
			}
		}
	}
}
