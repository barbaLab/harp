using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bonsai.Harp
{
    public interface ITransport : IDisposable
    {
        bool IgnoreErrors { get; set; }

        void SetObserver(IObserver<HarpMessage> observer);

        void Write(HarpMessage input);

        void Close();
    }
}
