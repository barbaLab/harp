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
            var device = context?.Instance as Device;
            if (device == null || string.IsNullOrEmpty(device.ConnectionName)) return UITypeEditorEditStyle.None;

            var doesServerExist = DesignTimeTcpDeviceDiscovery.TryGetServer(context, device.ConnectionName, out var server) && server != null;
            if (!doesServerExist)
            {
                throw new InvalidOperationException($"No CreateTcpServer with connection name '{device.ConnectionName}' is available in the workflow.");
            }

            if (server.Port == 0 || server.Port > 65535)
            {
                throw new InvalidOperationException($"Invalid port number specified in CreateTcpServer '{device.ConnectionName}'. Must be between 1 and 65535.");
            }

            return UITypeEditorEditStyle.DropDown;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            if (context == null || provider == null) return value;

            var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
            if (editorService == null) return value;

            var device = context?.Instance as Device;
            if (device == null || string.IsNullOrEmpty(device.ConnectionName)) return value;

            using var dropdown = new DeviceNameDropDownControl();
            using var discovery = DesignTimeTcpDeviceDiscovery.TryStart(context, device.ConnectionName, dropdown.AddDeviceName, dropdown.RemoveDeviceName, out var statusText);
            dropdown.SetStatus(statusText);

            dropdown.SelectionCommitted += (_, __) => editorService.CloseDropDown();
            editorService.DropDownControl(dropdown);
            discovery?.Dispose();

            return dropdown.SelectedDeviceName ?? value;
        }
    }

    sealed class DeviceNameDropDownControl : UserControl
    {
        readonly ListBox listBox;
        readonly Label statusLabel;

        public DeviceNameDropDownControl()
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
            MinimumSize = new Size(220, 100);
            Size = MinimumSize;
        }

        public event EventHandler SelectionCommitted;

        public string SelectedDeviceName => listBox.SelectedItem as string;

        public void AddDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;

            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(AddDeviceName, deviceName);
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

        public void RemoveDeviceName(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;

            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(RemoveDeviceName, deviceName);
                return;
            }

            listBox.Items.Remove(deviceName);
            if (listBox.Items.Count == 0)
            {
                statusLabel.Visible = true;
                statusLabel.BringToFront();
            }
        }

        public void SetStatus(string text)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(SetStatus, text);
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

    sealed class DesignTimeTcpDeviceDiscovery : IDisposable
    {
        const int MaxConcurrentProbes = 8;

        readonly TcpListener listener;
        readonly CancellationTokenSource cancellation;
        readonly SemaphoreSlim probeGate;
        List<TcpClient> activeClients;
        readonly object clientsLock;
        readonly Action<string> onDeviceDiscovered;
        readonly Action<string> onDeviceDisconnected;
        bool disposed;

        DesignTimeTcpDeviceDiscovery(int port, Action<string> onDeviceDiscovered, Action<string> onDeviceDisconnected)
        {
            this.onDeviceDiscovered = onDeviceDiscovered;
            this.onDeviceDisconnected = onDeviceDisconnected;
            cancellation = new CancellationTokenSource();
            probeGate = new SemaphoreSlim(MaxConcurrentProbes, MaxConcurrentProbes);
            activeClients = new List<TcpClient>();
            clientsLock = new object();

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _ = Task.Run(AcceptClientsAsync);
        }

        internal static IDisposable TryStart(ITypeDescriptorContext context, string connectionName, Action<string> onDeviceName, Action<string> onDeviceDisconnected, out string statusText)
        {
            statusText = "Waiting for Harp devices...";

            if (!TryGetServer(context, connectionName, out var server) || server == null || server.Port == 0 || server.Port > 65535)
            {
                return null;
            }

            return new DesignTimeTcpDeviceDiscovery(server.Port, onDeviceName, onDeviceDisconnected);
        }

        async Task AcceptClientsAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (client == null) break;

                    client.NoDelay = true;
                    client.SendTimeout = Timeout.Infinite;
                    client.ReceiveTimeout = Timeout.Infinite;

                    lock (clientsLock)
                    {
                        activeClients.Add(client);
                    }

                    var displayName = await Task.Run(() => ProbeClientAsync(client));
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        _ = Task.Run(() => MonitorClientAsync(client, displayName));
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (cancellation.IsCancellationRequested) break; }
                catch { /*ignore*/ }
            }
        }

        async Task<string> ProbeClientAsync(TcpClient client)
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
                        onDeviceDiscovered?.Invoke(displayName);
                    }
                }
            }
            catch (OperationCanceledException) { /*ignore*/ }
            catch { /*ignore*/ }
            finally
            {
                if (gateAcquired)
                {
                    try { probeGate.Release(); } catch { }
                }
            }

            return displayName;
        }

        async Task MonitorClientAsync(TcpClient client, string displayName)
        {
            while (!cancellation.IsCancellationRequested)
            {
                // TODO: Check Harp heartbeats
            }
            // await Task.Delay(5000, cancellation.Token).ConfigureAwait(false);

            lock (clientsLock)
            {
                Console.WriteLine($"Client '{displayName}' disconnected.");
                activeClients.Remove(client);
                onDeviceDisconnected?.Invoke(displayName);
            }
            try { client.Close(); } catch { }
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

        internal static bool TryStop(IDisposable discovery)
        {
            if (discovery == null) return false;

            try { discovery.Dispose(); }
            catch { /*ignore*/ }
            return true;
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
    }
}
