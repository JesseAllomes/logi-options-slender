using System.Runtime.InteropServices;

namespace Slender.Engine;

/// <summary>VARIANT as laid out on x64 (24 bytes). Only the types Excel's object model hands back are modelled.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ComVariant
{
    public const ushort VT_EMPTY = 0, VT_I2 = 2, VT_I4 = 3, VT_R4 = 4, VT_R8 = 5, VT_BOOL = 11, VT_DISPATCH = 9, VT_UI1 = 17;

    public ushort Vt;
    public ushort Reserved1, Reserved2, Reserved3;
    public long Data1;
    public long Data2;

    public static ComVariant FromInt32(int value) => new() { Vt = VT_I4, Data1 = value };

    /// <summary>Numeric value of an I2/I4/R4/R8/BOOL/UI1 result; throws for anything else.</summary>
    public readonly int ToInt32() => Vt switch
    {
        VT_I2 => (short)Data1,
        VT_I4 => (int)Data1,
        VT_UI1 => (byte)Data1,
        VT_BOOL => (short)Data1 != 0 ? 1 : 0,
        VT_R4 => (int)Math.Round(BitConverter.Int32BitsToSingle((int)Data1)),
        VT_R8 => (int)Math.Round(BitConverter.Int64BitsToDouble(Data1)),
        _ => throw new InvalidCastException($"VARIANT type {Vt} is not numeric."),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DispParams
{
    public ComVariant* Args;
    public int* NamedArgIds;
    public uint ArgCount;
    public uint NamedArgCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ExcepInfo
{
    public ushort Code, Reserved;
    public nint Source, Description, HelpFile;
    public uint HelpContext;
    public nint Reserved2, DeferredFillIn;
    public int SCode;
}

/// <summary>
/// Minimal late-bound IDispatch client over raw interface pointers. No ComWrappers and no reflection, so it is NativeAOT safe.
/// Only 32-bit integer arguments are supported (all that Excel's Window/Worksheet/Range members used here take).
/// </summary>
internal sealed unsafe class ComDispatch : IDisposable
{
    public const int DISPID_PROPERTYPUT = -3;
    public const ushort DISPATCH_METHOD = 1, DISPATCH_PROPERTYGET = 2, DISPATCH_PROPERTYPUT = 4;
    private const int DISP_E_EXCEPTION = unchecked((int)0x80020009);
    private static readonly Guid IidNull = Guid.Empty;

    private nint _ptr;

    public ComDispatch(nint pdisp)
    {
        if (pdisp == 0) throw new ArgumentException("null IDispatch", nameof(pdisp));
        _ptr = pdisp;
    }

    /// <summary>oleacc: the Excel Window object behind an EXCEL7 window. Throws COMException on failure.</summary>
    public static ComDispatch FromWindow(nint hwnd, uint objectId)
    {
        Guid iid = new("00020400-0000-0000-C000-000000000046"); // IID_IDispatch
        int hr = AccessibleObjectFromWindow(hwnd, objectId, &iid, out nint obj);
        if (hr != 0 || obj == 0) throw new COMException("AccessibleObjectFromWindow failed", hr);
        return new ComDispatch(obj);
    }

    /// <summary>
    /// Packs call arguments the way IDispatch::Invoke wants them: rgvarg is in REVERSE order of the call, and named
    /// arguments come first, with namedIds[i] describing dest[i]. dest must hold values.Length entries.
    /// </summary>
    internal static void PackArgs(ReadOnlySpan<int> values, Span<ComVariant> dest)
    {
        if (dest.Length < values.Length) throw new ArgumentException("dest too small", nameof(dest));
        for (int i = 0; i < values.Length; i++)
        {
            dest[i] = ComVariant.FromInt32(values[values.Length - 1 - i]);
        }
    }

    public ComDispatch? GetObject(string name) => ResultObject(Invoke(name, DISPATCH_PROPERTYGET, default, 0));

    public int GetInt(string name)
    {
        ComVariant v = Invoke(name, DISPATCH_PROPERTYGET, default, 0);
        try { return v.ToInt32(); }
        finally { VariantClear(&v); }
    }

    public void PutInt(string name, int value)
    {
        ComVariant v = Invoke(name, DISPATCH_PROPERTYPUT, [value], 1, DISPID_PROPERTYPUT);
        VariantClear(&v);
    }

    /// <summary>Calls a method with no arguments; returns the object it returns, or null.</summary>
    public ComDispatch? Call(string name) => ResultObject(Invoke(name, DISPATCH_METHOD, default, 0));

    /// <summary>Calls a method with one NAMED integer argument (Window.SmallScroll(ToRight:=n)).</summary>
    public void CallNamedInt(string method, string argName, int value)
    {
        ComVariant v = Invoke(method, DISPATCH_METHOD, [value], 1, 0, argName);
        VariantClear(&v);
    }

    public void Dispose()
    {
        nint p = _ptr;
        _ptr = 0;
        if (p != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)p)[2])(p); // Release
    }

    private static ComDispatch? ResultObject(ComVariant v)
    {
        if (v.Vt == ComVariant.VT_DISPATCH && v.Data1 != 0)
        {
            return new ComDispatch((nint)v.Data1); // ownership of the reference moves to the wrapper
        }

        VariantClear(&v);
        return null;
    }

    private ComVariant Invoke(string name, ushort flags, ReadOnlySpan<int> args, int namedCount, int firstNamedId = 0, string? namedArg = null)
    {
        nint p = _ptr;
        if (p == 0) throw new ObjectDisposedException(nameof(ComDispatch));
        nint* vtbl = *(nint**)p;

        // GetIDsOfNames: [member, namedArg?]
        int* ids = stackalloc int[2];
        int nameCount = namedArg is null ? 1 : 2;
        nint n0 = Marshal.StringToCoTaskMemUni(name);
        nint n1 = namedArg is null ? 0 : Marshal.StringToCoTaskMemUni(namedArg);
        try
        {
            nint* names = stackalloc nint[2];
            names[0] = n0;
            names[1] = n1;
            Guid iid = IidNull;
            int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint, uint, int*, int>)vtbl[5])(p, &iid, names, (uint)nameCount, 0x0409, ids);
            if (hr < 0) throw new COMException($"GetIDsOfNames({name}) failed", hr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(n0);
            if (n1 != 0) Marshal.FreeCoTaskMem(n1);
        }

        ComVariant* argv = stackalloc ComVariant[Math.Max(args.Length, 1)];
        PackArgs(args, new Span<ComVariant>(argv, args.Length));
        int namedId = namedArg is null ? firstNamedId : ids[1];
        DispParams dp = new() { Args = argv, NamedArgIds = &namedId, ArgCount = (uint)args.Length, NamedArgCount = (uint)namedCount };

        ComVariant result = default;
        ExcepInfo ex = default;
        uint argErr = 0;
        Guid iid2 = IidNull;
        int hr2 = ((delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, DispParams*, ComVariant*, ExcepInfo*, uint*, int>)vtbl[6])(
            p, ids[0], &iid2, 0x0409, flags, &dp, &result, &ex, &argErr);
        if (hr2 < 0)
        {
            int code = hr2;
            if (hr2 == DISP_E_EXCEPTION && ex.SCode != 0) code = ex.SCode;
            FreeBstr(ex.Source);
            FreeBstr(ex.Description);
            FreeBstr(ex.HelpFile);
            throw new COMException($"Invoke({name}) failed", code);
        }

        return result;
    }

    private static void FreeBstr(nint b)
    {
        if (b != 0) Marshal.FreeBSTR(b);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint hwnd, uint dwId, Guid* riid, out nint ppvObject);

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(ComVariant* pvarg);
}
