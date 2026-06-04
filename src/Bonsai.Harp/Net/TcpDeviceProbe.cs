using Bonsai.Expressions;
using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Subjects;

namespace Bonsai.Harp.Net
{
    /// <summary>
    /// Provides methods to probe TCP connections for Harp devices, allowing for the discovery of device names and unique identifiers over the network.
    /// </summary>
    public static class TcpDeviceProbe
    {
        const int ServerTimeoutMilliseconds = 1000;
        const int MaxConcurrentProbes = 8;

        /// <summary>
        /// Attempts to retrieve the names of Harp devices connected to the specified TCP server.
        /// This method listens for incoming TCP connections on the specified port and probes each connection for a Harp device, collecting their names and unique identifiers.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="connectionName"></param>
        /// <returns>
        /// An array of strings containing the names and unique identifiers of the discovered Harp devices formatted as "deviceName (uniqueIdentifier)".
        /// </returns>
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

                    probeTasks.Add(ProbeClientAsync(client, probeGate, deviceNames));
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

        static async Task ProbeClientAsync(TcpClient client, SemaphoreSlim probeGate, HashSet<string> deviceNames)
        {
            await probeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var deviceName = await GetDeviceName(client).ConfigureAwait(false);
                var deviceUid = await GetUid(client).ConfigureAwait(false);
                deviceUid = DeviceProbe.GetReadableUid(deviceUid);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    deviceName = $"{deviceName} ({deviceUid})";
                    deviceNames.Add(deviceName);
                }
            }
            finally
            {
                try { client?.Close(); } catch { }
                probeGate.Release();
            }
        }

        /// <summary>
        /// Asynchronously retrieves the name of the Harp device connected via the specified TCP client.
        /// </summary>
        /// <param name="client">The TCP client connected to the Harp device.</param>
        /// <returns>
        /// A task that represents the asynchronous operation and returns the device name.
        /// </returns>
        public static async Task<string> GetDeviceName(TcpClient client)
        {
            if (client == null) return string.Empty;

            var transport = new TcpTransport(client, new Subject<HarpMessage>());
            transport.IgnoreErrors = true;

            return await DeviceProbe.GetDeviceName(transport).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously retrieves the unique identifier of the Harp device connected via the specified TCP client.
        /// </summary>
        /// <param name="client">The TCP client connected to the Harp device.</param>
        /// <returns>
        /// A task that represents the asynchronous operation and returns the unique identifier of the connected Harp device.
        /// </returns>
        public static async Task<string> GetUid(TcpClient client)
        {
            if (client == null) return string.Empty;

            var transport = new TcpTransport(client, new Subject<HarpMessage>());
            transport.IgnoreErrors = true;

            return await DeviceProbe.GetUid(transport).ConfigureAwait(false);
        }
    }
}
