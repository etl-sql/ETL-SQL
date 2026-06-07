using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Builds the child-process invocations for report actions launched from the TUI
    /// (serve, and later publish/export). The TUI re-invokes the same ETL-SQL executable
    /// with the relevant CLI verb; output is redirected so the child never disturbs the
    /// terminal UI, and an explicit port is used so the TUI knows the URL to show/open.
    /// </summary>
    public static class ReportLauncher
    {
        public static ProcessStartInfo BuildServeProcess(string exePath, string scriptFullPath, int port)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("serve");
            psi.ArgumentList.Add(scriptFullPath);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add("--no-browser"); // the TUI opens the browser itself, reliably
            return psi;
        }

        /// <summary>Reserves an OS-assigned free loopback port.</summary>
        public static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Waits for the server to accept connections, then opens the URL in the default browser.</summary>
        public static async Task OpenWhenReadyAsync(string url, int port, int timeoutSeconds = 20)
        {
            var sw = Stopwatch.StartNew();
            bool ready = false;
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync("localhost", port);
                    ready = true;
                    break;
                }
                catch { await Task.Delay(250); }
            }
            if (!ready) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
        }
    }
}
