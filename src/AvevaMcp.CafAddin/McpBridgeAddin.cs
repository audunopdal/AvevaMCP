using System;
using System.Threading;
using Aveva.ApplicationFramework;

namespace AvevaMcp.CafAddin
{
    public sealed class McpBridgeAddin : IAddinInjected
    {
        private PipeBridgeHost _bridge;

        public string Name
        {
            get { return "AvevaMcp.CafAddin"; }
        }

        public string Description
        {
            get { return "Local MCP bridge for AVEVA Unified Engineering 4.1"; }
        }

        public void Start(IDependencyResolver resolver)
        {
            SynchronizationContext context = SynchronizationContext.Current;
            if (context == null)
            {
                throw new InvalidOperationException(
                    "The AVEVA MCP add-in must start on a UE thread with a synchronization context.");
            }

            _bridge = new PipeBridgeHost(context);
            _bridge.Start();
        }

        public void Start(ServiceManager serviceManager)
        {
        }

        public void Stop()
        {
            if (_bridge != null)
            {
                _bridge.Stop();
                _bridge = null;
            }
        }
    }
}
