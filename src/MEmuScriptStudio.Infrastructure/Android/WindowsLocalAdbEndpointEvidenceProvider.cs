using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed record LocalAdbEndpointEvidence(
    bool IsLocalEndpoint,
    bool IsMemuOwned,
    int? ListenerProcessId = null,
    string? ListenerExecutableName = null,
    string? ListenerExecutablePath = null);

public interface ILocalAdbEndpointEvidenceProvider
{
    LocalAdbEndpointEvidence Inspect(string serial);
}

public sealed class WindowsLocalAdbEndpointEvidenceProvider : ILocalAdbEndpointEvidenceProvider
{
    private const int AddressFamilyInternet = 2;
    private const uint TcpStateListen = 2;
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const int ErrorInsufficientBuffer = 122;
    private const int TcpTableOwnerPidAll = 5;

    private static readonly HashSet<string> MemuListenerExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "MEmu.exe",
        "MEmuHeadless.exe",
        "MEmuSVC.exe"
    };

    public LocalAdbEndpointEvidence Inspect(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (!IPEndPoint.TryParse(serial.Trim(), out var endpoint) ||
            endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !IPAddress.IsLoopback(endpoint.Address))
            return new LocalAdbEndpointEvidence(false, false);

        try
        {
            foreach (var listener in ReadTcpRows().Where(row =>
                         row.State == TcpStateListen &&
                         DecodePort(row.LocalPort) == endpoint.Port &&
                         ListenerAccepts(row.LocalAddress, endpoint.Address)))
            {
                var processId = checked((int)listener.OwningProcessId);
                var executablePath = TryReadExecutablePath(processId);
                var executableName = executablePath is null
                    ? TryReadExecutableName(processId)
                    : Path.GetFileName(executablePath);
                if (IsPositiveMemuOwner(executableName, executablePath))
                {
                    return new LocalAdbEndpointEvidence(
                        true,
                        true,
                        processId,
                        executableName,
                        executablePath);
                }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OverflowException)
        {
            // Missing or unreadable host evidence is deliberately Unknown, never a reason to hide a device.
        }

        return new LocalAdbEndpointEvidence(true, false);
    }

    internal static bool IsPositiveMemuOwner(string? executableName, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executableName) || string.IsNullOrWhiteSpace(executablePath) ||
            !MemuListenerExecutables.Contains(Path.GetFileName(executableName)))
            return false;

        var segments = executablePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "Microvirt", StringComparison.OrdinalIgnoreCase) &&
                segments[index + 1].StartsWith("MEmu", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool ListenerAccepts(uint listenerAddressValue, IPAddress endpointAddress)
    {
        var listenerAddress = new IPAddress(listenerAddressValue);
        return listenerAddress.Equals(IPAddress.Any) || listenerAddress.Equals(endpointAddress);
    }

    private static IReadOnlyList<MibTcpRowOwnerPid> ReadTcpRows()
    {
        var size = 0;
        var firstResult = NativeMethods.GetExtendedTcpTable(
            nint.Zero, ref size, true, AddressFamilyInternet, TcpTableOwnerPidAll, 0);
        if (firstResult != ErrorInsufficientBuffer)
            throw new Win32Exception(firstResult, "Không thể xác định kích thước bảng TCP của Windows.");

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var secondResult = NativeMethods.GetExtendedTcpTable(
                buffer, ref size, true, AddressFamilyInternet, TcpTableOwnerPidAll, 0);
            if (secondResult != 0)
                throw new Win32Exception(secondResult, "Không thể đọc bảng TCP của Windows.");

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var rows = new List<MibTcpRowOwnerPid>(Math.Max(0, count));
            var current = buffer + sizeof(uint);
            for (var index = 0; index < count; index++)
            {
                rows.Add(Marshal.PtrToStructure<MibTcpRowOwnerPid>(current));
                current += rowSize;
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int DecodePort(uint networkOrderPort) =>
        (ushort)IPAddress.NetworkToHostOrder((short)networkOrderPort);

    private static string? TryReadExecutablePath(int processId)
    {
        using var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid) return null;
        var capacity = 1024;
        var path = new StringBuilder(capacity);
        return NativeMethods.QueryFullProcessImageName(process, 0, path, ref capacity)
            ? path.ToString()
            : null;
    }

    private static string? TryReadExecutableName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return $"{process.ProcessName}.exe";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        internal uint State;
        internal uint LocalAddress;
        internal uint LocalPort;
        internal uint RemoteAddress;
        internal uint RemotePort;
        internal uint OwningProcessId;
    }

    private static class NativeMethods
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        internal static extern int GetExtendedTcpTable(
            nint tcpTable,
            ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool order,
            int addressFamily,
            int tableClass,
            uint reserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(
            SafeProcessHandle process,
            uint flags,
            StringBuilder executablePath,
            ref int size);
    }
}
