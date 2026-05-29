using System;
using Bonsai.Harp;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Bonsai.Harp.Net
{
    class TcpTransport : StreamTransport, ITransport
    {
        const int DefaultReadBufferSize = 1048576; // 2^20 = 1 MB
        readonly CancellationTokenSource taskCancellation;
        readonly TcpClient tcpClient;
        readonly NetworkStream networkStream;
        readonly object writeLock;

        public TcpTransport(TcpClient client, IObserver<HarpMessage> observer)
            : base(observer)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            IgnoreErrors = true;
            taskCancellation = new CancellationTokenSource();
            writeLock = new object();
            tcpClient = client;
            tcpClient.NoDelay = true;
            networkStream = tcpClient.GetStream();
            networkStream.ReadTimeout = Timeout.Infinite;
            RunAsync(taskCancellation.Token);
        }

        Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                // using var cancellation = cancellationToken.Register(tcpClient.Dispose);
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var bytesToRead = tcpClient.Available;
                        if (bytesToRead == 0)
                        {
                            var bytesRead = PushData(networkStream, DefaultReadBufferSize, count: 1);
                            if (bytesRead == 0)
                            {
                                break;
                            }

                            bytesToRead = tcpClient.Available;
                        }

                        ReceiveData(networkStream, DefaultReadBufferSize, bytesToRead);
                    }
                    catch (Exception ex)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            OnError(ex);
                        }

                        break;
                    }
                }
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        }

        public void Write(HarpMessage input)
        {
            lock (writeLock)
            {
                networkStream.Write(input.MessageBytes, 0, input.MessageBytes.Length);
            }
        }

        public override void Close()
        {
            if (!taskCancellation.IsCancellationRequested)
            {
                taskCancellation.Cancel();
                taskCancellation.Dispose();
            }
        }
    }
}
