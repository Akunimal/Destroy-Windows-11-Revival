using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using DWS.Core.Logging;

namespace DWS.Core.Security
{
    /// <summary>
    /// System Privilege Broker.
    /// Safely escalates execution context to NT AUTHORITY\SYSTEM without injecting into PPL-protected processes (lsass/winlogon).
    /// Uses native Task Scheduler ServiceAccount semantics and bidirectional Named Pipes for secure IPC.
    /// </summary>
    public sealed class SystemPrivilegeBroker
    {
        private const string TaskName = "DWS_EnterprisePrivilegeWorker";
        public event CoreMessageEventHandler OnWorkerOutput;
        public event CoreErrorEventHandler OnBrokerError;

        public bool IsRunningAsSystem()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return identity != null && identity.IsSystem;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool ExecuteAsSystem(string commandPayload, out string responseOutput)
        {
            responseOutput = string.Empty;

            // Fast path: already running as SYSTEM
            if (IsRunningAsSystem())
            {
                TransactionJournal.Log(JournalEntryType.SecurityOperation, "Process already executing under NT AUTHORITY\\SYSTEM.");
                responseOutput = "EXECUTED_LOCAL_SYSTEM";
                return true;
            }

            string pipeName = "DWS_IPC_" + Guid.NewGuid().ToString("N");
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            TransactionJournal.Log(JournalEntryType.SecurityOperation, $"Spawning SYSTEM worker via Task Scheduler COM/CLI. Pipe: {pipeName}");

            try
            {
                // Register ephemeral scheduled task configured for NT AUTHORITY\SYSTEM
                string taskCommand = $"\"{currentExe}\" /WORKER {pipeName}";
                string createArgs = $"/create /tn \"{TaskName}\" /tr \"{taskCommand}\" /sc once /st 00:00 /sd 01/01/2099 /ru \"NT AUTHORITY\\SYSTEM\" /rl HIGHEST /f";

                RunHiddenProcess("schtasks.exe", createArgs, 10000);

                using (var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    // Trigger task execution
                    RunHiddenProcess("schtasks.exe", $"/run /tn \"{TaskName}\"", 5000);

                    // Wait for worker connection (timeout 15 seconds)
                    var connectAsync = pipeServer.BeginWaitForConnection(null, null);
                    if (!connectAsync.AsyncWaitHandle.WaitOne(15000))
                    {
                        throw new TimeoutException("Privileged SYSTEM worker failed to connect to Named Pipe within timeout.");
                    }
                    pipeServer.EndWaitForConnection(connectAsync);

                    // Send command payload
                    byte[] payloadBytes = Encoding.UTF8.GetBytes(commandPayload ?? string.Empty);
                    byte[] lengthPrefix = BitConverter.GetBytes(payloadBytes.Length);
                    pipeServer.Write(lengthPrefix, 0, 4);
                    if (payloadBytes.Length > 0)
                    {
                        pipeServer.Write(payloadBytes, 0, payloadBytes.Length);
                    }
                    pipeServer.Flush();

                    // Read response stream
                    using (var reader = new StreamReader(pipeServer, Encoding.UTF8))
                    {
                        responseOutput = reader.ReadToEnd();
                    }

                    TransactionJournal.Log(JournalEntryType.SecurityOperation, $"SYSTEM worker completed execution successfully. Bytes received: {responseOutput.Length}");
                    OnWorkerOutput?.Invoke(this, responseOutput);
                    return true;
                }
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, "SystemPrivilegeBroker execution failed", ex);
                OnBrokerError?.Invoke(this, ex);
                return false;
            }
            finally
            {
                // Clean up ephemeral task
                try
                {
                    RunHiddenProcess("schtasks.exe", $"/delete /tn \"{TaskName}\" /f", 5000);
                }
                catch { }
            }
        }

        /// <summary>
        /// Entry point invoked when DWS is launched in worker mode: DWS_Lite.exe /WORKER &lt;pipeName&gt;
        /// </summary>
        public static void RunWorkerClient(string pipeName, Func<string, string> commandHandler)
        {
            try
            {
                using (var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    pipeClient.Connect(10000);

                    byte[] lengthPrefix = new byte[4];
                    int read = pipeClient.Read(lengthPrefix, 0, 4);
                    if (read == 4)
                    {
                        int length = BitConverter.ToInt32(lengthPrefix, 0);
                        byte[] payload = new byte[length];
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int chunk = pipeClient.Read(payload, totalRead, length - totalRead);
                            if (chunk <= 0) break;
                            totalRead += chunk;
                        }

                        string command = Encoding.UTF8.GetString(payload, 0, totalRead);
                        string result = commandHandler != null ? commandHandler(command) : "OK";

                        byte[] responseBytes = Encoding.UTF8.GetBytes(result);
                        pipeClient.Write(responseBytes, 0, responseBytes.Length);
                        pipeClient.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                TransactionJournal.Log(JournalEntryType.Error, "Worker client execution encountered error", ex);
            }
        }

        private static int RunHiddenProcess(string fileName, string args, int timeoutMs)
        {
            using (var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            })
            {
                proc.Start();
                if (!proc.WaitForExit(timeoutMs))
                {
                    proc.Kill();
                    throw new TimeoutException($"Process {fileName} {args} timed out after {timeoutMs}ms");
                }
                return proc.ExitCode;
            }
        }
    }
}
