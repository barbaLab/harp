using Bonsai.Expressions;
using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive;
using System.Reactive.Subjects;
using DeviceNameRegister = Bonsai.Harp.DeviceName;

namespace Bonsai.Harp.Net
{
    public static class TcpDeviceProbe
    {
        const int ServerTimeoutMilliseconds = 1000;
        const int MaxConcurrentProbes = 8;

        public static string[] TryGetTcpDevices(ITypeDescriptorContext context, string connectionName)
        {
            if (context == null || string.IsNullOrEmpty(connectionName)) return Array.Empty<string>();

            CreateTcpServer server;
            if (!TryGetServer(context, connectionName, out server)) return Array.Empty<string>();
            if (server == null || server.Port <= 0) return Array.Empty<string>();

            TcpListener listener = null;
            TcpClient client = null;
            var probeTasks = new List<Task>();
            var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var probeGate = new SemaphoreSlim(MaxConcurrentProbes, MaxConcurrentProbes);

            try
            {
                listener = new TcpListener(IPAddress.Any, server.Port);
                listener.Start();

                var deadline = DateTime.UtcNow.AddMilliseconds(ServerTimeoutMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    var acceptTask = listener.AcceptTcpClientAsync();
                    if (!acceptTask.Wait(remaining)) break;

                    client = acceptTask.Result;
                    client.NoDelay = true;
                    client.SendTimeout = ServerTimeoutMilliseconds;
                    client.ReceiveTimeout = ServerTimeoutMilliseconds;

                    probeTasks.Add(ProbeClientAsync(client, connectionName, probeGate, deviceNames));
                    client = null;
                }

                if (probeTasks.Count > 0)
                {
                    Task.WaitAll(probeTasks.ToArray());
                }

                if (deviceNames.Count == 0) return Array.Empty<string>();

                var result = new string[deviceNames.Count];
                deviceNames.CopyTo(result);
                Array.Sort(result, StringComparer.OrdinalIgnoreCase);
                return result;
            }
            catch
            {
                // Ignore any exceptions that may occur during the design-time Harp devices probe.
                return Array.Empty<string>();
            }
            finally
            {
                try { client?.Close(); } catch { }
                try { probeGate.Dispose(); } catch { }
                try { listener?.Stop(); } catch { }
            }
        }

        static bool TryGetServer(ITypeDescriptorContext context, string connectionName, out CreateTcpServer server)
        {
            server = null;
            if (context == null || string.IsNullOrEmpty(connectionName)) return false;

            var workflowBuilder = (WorkflowBuilder)context.GetService(typeof(WorkflowBuilder));
            if (workflowBuilder == null || workflowBuilder.Workflow == null) return false;

            server = workflowBuilder.Workflow.Descendants()
                .Select(builder => ExpressionBuilder.GetWorkflowElement(builder) as CreateTcpServer)
                .FirstOrDefault(createTcpServer => createTcpServer != null && string.Equals(createTcpServer.Name, connectionName, StringComparison.OrdinalIgnoreCase));

            return server != null;
        }

        static async Task ProbeClientAsync(TcpClient client, string connectionName, SemaphoreSlim probeGate, HashSet<string> deviceNames)
        {
            await probeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var deviceName = await GetDeviceName(client).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    var deviceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    deviceName = $"{deviceName} ({CreateClientKey(connectionName, deviceIp, deviceName)})";
                    deviceNames.Add(deviceName);
                }
            }
            finally
            {
                try { client?.Close(); } catch { }
                probeGate.Release();
            }
        }

        public static async Task<string> GetDeviceName(TcpClient client)
        {
            if (client == null) return string.Empty;

            var transport = new TcpTransport(client, new Subject<HarpMessage>());
            transport.IgnoreErrors = true;

            return await DeviceProbe.GetDeviceName(transport).ConfigureAwait(false);
        }

        public static string CreateClientKey(string connectionName, string deviceIp, string deviceName)
        {
            var keySource = string.Concat(connectionName ?? string.Empty, "|", deviceIp ?? string.Empty, "|", deviceName ?? string.Empty);
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keySource));
                var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
                return hex.Length > 8 ? hex.Substring(0, 8) : hex;
            }
        }
    }
}
