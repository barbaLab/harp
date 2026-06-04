using Bonsai.Expressions;
using Bonsai.Harp.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace Bonsai.Harp.Design
{
    /// <summary>
    /// Provides a drop-down editor that discovers TCP Harp devices while the popup is open.
    /// </summary>
    public class DeviceNameEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return TryGetDeviceServer(context) ? UITypeEditorEditStyle.DropDown : UITypeEditorEditStyle.None;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            if (context == null || provider == null) return value;

            var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
            if (editorService == null) return value;

            var device = GetDevice(context.Instance);
            if (device == null || string.IsNullOrEmpty(device.ConnectionName)) return value;

            using var dropdown = new DeviceNameDropDownControl(value as string);
            using var discovery = TcpDeviceDiscovery.TryCreate(context, device.ConnectionName, dropdown.AddDeviceName, out var statusText);
            dropdown.SetStatus(statusText);

            dropdown.SelectionCommitted += (_, __) => editorService.CloseDropDown();
            editorService.DropDownControl(dropdown);

            return dropdown.SelectedDeviceName ?? value;
        }

        static Device GetDevice(object instance)
        {
            if (instance is Device device)
            {
                return device;
            }

            if (instance is object[] array)
            {
                foreach (var item in array)
                {
                    if (item is Device arrayDevice)
                    {
                        return arrayDevice;
                    }
                }
            }

            return null;
        }

        static bool TryGetDeviceServer(ITypeDescriptorContext context)
        {
            return TcpDeviceDiscovery.TryGetServer(context, GetDevice(context?.Instance)?.ConnectionName, out var server) && server != null && server.Port > 0;
        }
    }

    sealed class DeviceNameDropDownControl : UserControl
    {
        readonly ListBox listBox;
        readonly Label statusLabel;

        public DeviceNameDropDownControl(string selectedDeviceName)
        {
            listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None
            };
            listBox.MouseUp += ListBox_MouseUp;
            listBox.KeyDown += ListBox_KeyDown;

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0)
            };

            Controls.Add(listBox);
            Controls.Add(statusLabel);
            statusLabel.BringToFront();
            BackColor = SystemColors.Window;
            MinimumSize = new Size(160, 80);
            Size = MinimumSize;

            // if (!string.IsNullOrEmpty(selectedDeviceName))
            // {
            //     listBox.Items.Add(selectedDeviceName);
            //     listBox.SelectedItem = selectedDeviceName;
            //     statusLabel.Visible = false;
            //     listBox.BringToFront();
            // }
        }

        public event EventHandler SelectionCommitted;

        public string SelectedDeviceName => listBox.SelectedItem as string;

        public void AddDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;

            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke((Action<string>)AddDeviceName, deviceName);
                return;
            }

            if (!listBox.Items.Contains(deviceName))
            {
                listBox.Items.Add(deviceName);
                if (statusLabel.Visible)
                {
                    statusLabel.Visible = false;
                    listBox.BringToFront();
                }
            }
        }

        public void SetStatus(string text)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke((Action<string>)SetStatus, text);
                return;
            }

            statusLabel.Text = text ?? string.Empty;
            statusLabel.Visible = listBox.Items.Count == 0;
            if (statusLabel.Visible)
            {
                statusLabel.BringToFront();
            }
        }

        void ListBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && listBox.SelectedIndex >= 0)
            {
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
            }
        }

        void ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && listBox.SelectedIndex >= 0)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    sealed class TcpDeviceDiscovery : IDisposable
    {
        const int ServerTimeoutMilliseconds = 1000;
        const int MaxConcurrentProbes = 8;

        readonly TcpListener listener;
        readonly CancellationTokenSource cancellation;
        readonly SemaphoreSlim probeGate;
        readonly HashSet<string> deviceNames;
        List<TcpClient> activeClients;
        readonly object clientsLock;
        readonly string connectionName;
        readonly Action<string> onDeviceDiscovered;
        bool disposed;

        TcpDeviceDiscovery(int port, string connectionName, Action<string> onDeviceDiscovered)
        {
            this.connectionName = connectionName;
            this.onDeviceDiscovered = onDeviceDiscovered;
            cancellation = new CancellationTokenSource();
            probeGate = new SemaphoreSlim(MaxConcurrentProbes, MaxConcurrentProbes);
            deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            activeClients = new List<TcpClient>();
            clientsLock = new object();

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _ = Task.Run(AcceptClientsAsync);
        }

        public static IDisposable TryCreate(ITypeDescriptorContext context, string connectionName, Action<string> onDeviceName, out string statusText)
        {
            statusText = "Waiting for Harp devices...";

            if (!TryGetServer(context, connectionName, out var server) || server == null || server.Port <= 0)
            {
                statusText = "No TCP server is available for this connection.";
                return new EmptyDisposable();
            }

            return new TcpDeviceDiscovery(server.Port, connectionName, onDeviceName);
        }

        async Task AcceptClientsAsync()
        {
            Console.WriteLine("Waiting for TCP clients...");
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (client == null) break;

                    client.NoDelay = true;
                    client.SendTimeout = ServerTimeoutMilliseconds;
                    client.ReceiveTimeout = ServerTimeoutMilliseconds;
                    Console.WriteLine($"Accepted TCP client from {client.Client.RemoteEndPoint}");

                    lock (clientsLock)
                    {
                        activeClients.Add(client);
                    }

                    _ = Task.Run(() => ProbeClientAsync(client));
                    // client = null;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (cancellation.IsCancellationRequested) break;
                }
                finally
                {
                    // if (client != null)
                    // {
                    //     CloseClient(client);
                    // }
                }
            }
        }

        async Task ProbeClientAsync(TcpClient client)
        {
            var displayName = string.Empty;
            var gateAcquired = false;
            try
            {
                await probeGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                gateAcquired = true;

                var deviceName = await TcpDeviceProbe.GetDeviceName(client).ConfigureAwait(false);
                var deviceUid = await TcpDeviceProbe.GetUid(client).ConfigureAwait(false);
                deviceUid = DeviceProbe.GetReadableUid(deviceUid);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    displayName = $"{deviceName} ({deviceUid})";

                    lock (clientsLock)
                    {
                        if (deviceNames.Add(displayName))
                        {
                            onDeviceDiscovered?.Invoke(displayName);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                if (gateAcquired)
                {
                    try { probeGate.Release(); } catch { }
                }

                // CloseClient(client);
            }
        }

        internal static bool TryGetServer(ITypeDescriptorContext context, string connectionName, out CreateTcpServer server)
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

        void CloseClient(TcpClient client)
        {
            if (client == null) return;

            lock (clientsLock)
            {
                activeClients.Remove(client);
            }

            try { client.Close(); } catch { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try { cancellation.Cancel(); } catch { }
            try { listener.Stop(); } catch { }

            List<TcpClient> clients;
            lock (clientsLock)
            {
                clients = activeClients.ToList();
                activeClients.Clear();
            }

            foreach (var client in clients)
            {
                try { client.Close(); } catch { }
            }

            try { probeGate.Dispose(); } catch { }
            try { cancellation.Dispose(); } catch { }
        }

        sealed class EmptyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
