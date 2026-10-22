using Microsoft.Win32.SafeHandles;

namespace Slender.Yealink.Windows;

/// <summary>
/// Reads a collection's usage layout from its preparsed data. Bit positions are not assumed: for each button usage the
/// reader asks HidP_SetUsages to set it in a zeroed report and sees which bit changed.
/// </summary>
internal static unsafe class HidpCapsReader
{
    /// <summary>Null when the preparsed data cannot be read. topPage is the collection's top-level usage page.</summary>
    public static CollectionCaps? Read(SafeFileHandle handle, out ushort topPage)
    {
        topPage = 0;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var pp)) return null;
        using (pp)
        {
            if (NativeMethods.HidP_GetCaps(pp, out var caps) != NativeMethods.HIDP_STATUS_SUCCESS) return null;
            topPage = caps.UsagePage;
            var inputs = Probe(pp, NativeMethods.HidP_Input, caps.NumberInputButtonCaps, caps.InputReportByteLength);
            var outputs = Probe(pp, NativeMethods.HidP_Output, caps.NumberOutputButtonCaps, caps.OutputReportByteLength);
            return new CollectionCaps(caps.InputReportByteLength, caps.OutputReportByteLength, inputs, outputs);
        }
    }

    private static Dictionary<ushort, UsageSlot> Probe(PreparsedDataHandle pp, int type, ushort capCount, int reportLength)
    {
        var slots = new Dictionary<ushort, UsageSlot>();
        if (capCount == 0 || reportLength < 2) return slots;

        var caps = new HIDP_BUTTON_CAPS[capCount];
        ushort n = capCount;
        fixed (HIDP_BUTTON_CAPS* c = caps)
        {
            if (NativeMethods.HidP_GetButtonCaps(type, c, ref n, pp) != NativeMethods.HIDP_STATUS_SUCCESS) return slots;
        }

        var report = new byte[reportLength];
        for (int i = 0; i < n; i++)
        {
            var cap = caps[i];
            ushort last = cap.IsRange != 0 ? cap.UsageMax : cap.UsageMin;
            for (uint u = cap.UsageMin; u <= last && u - cap.UsageMin < 256; u++)
            {
                int bit = FindBit(pp, type, cap, (ushort)u, report);
                if (bit >= 0) slots.TryAdd((ushort)u, new UsageSlot(cap.ReportID, bit));
            }
        }
        return slots;
    }

    private static int FindBit(PreparsedDataHandle pp, int type, HIDP_BUTTON_CAPS cap, ushort usage, byte[] report)
    {
        fixed (byte* r = report)
        {
            if (NativeMethods.HidP_InitializeReportForID(type, cap.ReportID, pp, r, (uint)report.Length) != NativeMethods.HIDP_STATUS_SUCCESS) return -1;
            uint count = 1;
            if (NativeMethods.HidP_SetUsages(type, cap.UsagePage, cap.LinkCollection, &usage, ref count, pp, r, (uint)report.Length)
                != NativeMethods.HIDP_STATUS_SUCCESS) return -1;
        }
        for (int b = 1; b < report.Length; b++)
            if (report[b] != 0) return b * 8 + System.Numerics.BitOperations.TrailingZeroCount(report[b]);
        return -1;
    }
}
