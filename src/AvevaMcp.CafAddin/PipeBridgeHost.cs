using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AvevaMcp.CafAddin
{
    internal sealed class PipeBridgeHost
    {
        private const string PipeName = "AvevaUE41Mcp";
        private readonly SynchronizationContext _uiContext;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private Task _listener;
        private NamedPipeServerStream _activePipe;

        public PipeBridgeHost(SynchronizationContext uiContext)
        {
            _uiContext = uiContext;
        }

        public void Start()
        {
            _listener = Task.Run((Func<Task>)ListenAsync);
            _listener.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Trace.TraceError("AVEVA MCP pipe listener failed: " + task.Exception);
                }
            }, TaskScheduler.Default);
        }

        public void Stop()
        {
            _shutdown.Cancel();
            NamedPipeServerStream activePipe = Interlocked.Exchange(ref _activePipe, null);
            if (activePipe != null)
            {
                try
                {
                    activePipe.Dispose();
                }
                catch (IOException exception)
                {
                    Trace.TraceWarning("Error closing AVEVA MCP pipe: " + exception.Message);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private async Task ListenAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                using (var pipe = CreatePipe())
                {
                    Interlocked.Exchange(ref _activePipe, pipe);
                    try
                    {
                        await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                        await HandleConnectionAsync(pipe, _shutdown.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (IOException) when (_shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                    finally
                    {
                        Interlocked.CompareExchange(ref _activePipe, null, pipe);
                    }
                }
            }
        }

        private static NamedPipeServerStream CreatePipe()
        {
            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null)
            {
                throw new InvalidOperationException("Could not determine the current Windows user for pipe security.");
            }

            IntPtr securityDescriptor;
            string sddl = "D:P(A;;GA;;;" + currentUser.Value + ")";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                1,
                out securityDescriptor,
                IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var securityAttributes = new SecurityAttributes
                {
                    Length = Marshal.SizeOf(typeof(SecurityAttributes)),
                    SecurityDescriptor = securityDescriptor,
                    InheritHandle = false
                };
                SafePipeHandle handle = CreateNamedPipe(
                    @"\\.\pipe\" + PipeName,
                    PipeAccessDuplex | FileFlagOverlapped,
                    PipeTypeByte | PipeWait | PipeRejectRemoteClients,
                    1,
                    4096,
                    4096,
                    0,
                    ref securityAttributes);

                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error);
                }

                try
                {
                    return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }
            finally
            {
                LocalFree(securityDescriptor);
            }
        }

        private const uint PipeAccessDuplex = 0x00000003;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint PipeTypeByte = 0x00000000;
        private const uint PipeWait = 0x00000000;
        private const uint PipeRejectRemoteClients = 0x00000008;

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool InheritHandle;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            IntPtr securityDescriptorSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafePipeHandle CreateNamedPipe(
            string name,
            uint openMode,
            uint pipeMode,
            uint maxInstances,
            uint outBufferSize,
            uint inBufferSize,
            uint defaultTimeout,
            ref SecurityAttributes securityAttributes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
        {
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    return;
                }

                JObject request = JObject.Parse(line);
                JToken id = request["id"] ?? JValue.CreateNull();
                try
                {
                    string method = request.Value<string>("method");
                    JObject parameters = request["parameters"] as JObject ?? new JObject();
                    JToken result = await RunOnUiThreadAsync(
                        method,
                        parameters,
                        cancellationToken).ConfigureAwait(false);

                    await writer.WriteLineAsync(new JObject
                    {
                        ["id"] = id,
                        ["result"] = result,
                        ["error"] = JValue.CreateNull()
                    }.ToString(Formatting.None)).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await writer.WriteLineAsync(new JObject
                    {
                        ["id"] = id,
                        ["result"] = JValue.CreateNull(),
                        ["error"] = new JObject { ["message"] = exception.Message }
                    }.ToString(Formatting.None)).ConfigureAwait(false);
                }
            }
        }

        private Task<JToken> RunOnUiThreadAsync(
            string method,
            JObject parameters,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<JToken>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _uiContext.Post(_ =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled();
                    return;
                }

                try
                {
                    completion.TrySetResult(CafOperations.Execute(method, parameters));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, null);
            return completion.Task;
        }
    }
}
