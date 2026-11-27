using System.Buffers.Binary;
using VivaAP.Client.Interop;

namespace VivaAP.Client.Patches;

// Stack-only: cannot be boxed, stored on a Patch, captured, or used by an async method.
// Native validation additionally checks the active token and the current OS thread.
internal readonly unsafe ref struct PatchContext(ulong invocation)
{
    private void Access(uint operation, uint argument = 0, void* data = null, uint size = 0)
    {
        int result = NativePatchInterop.Access(invocation, operation, argument, data, size);
        if (result != 0) throw new InvalidOperationException($"Native patch operation {operation} failed ({result}).");
    }

    private uint State { get { uint value = 0; Access(10, data: &value, size: 4); return value; } }
    public bool OriginalStarted => (State & 1) != 0;
    public bool OriginalSkipped => (State & 2) != 0;
    public bool HasSideEffects => (State & 4) != 0;
    public bool HasOutcome => (State & 3) != 0;
    private static uint Register(int register) => register is >= 0 and < 32
        ? (uint)register : throw new ArgumentOutOfRangeException(nameof(register));

    public ulong GetGpr(int register) { ulong value = 0; Access(1, Register(register), &value, 8); return value; }
    public void SetGpr(int register, ulong value) => Access(2, Register(register), &value, 8);
    public double GetFpr(int register) { double value = 0; Access(3, Register(register), &value, 8); return value; }
    public void SetFpr(int register, double value) => Access(4, Register(register), &value, 8);
    public uint GetGuestAddress(int register) => GuestAddress(GetGpr(register));
    public static uint GuestAddress(ulong value) => value is >= 0x10000 and <= uint.MaxValue
        ? (uint)value : throw new InvalidOperationException("Invalid 32-bit guest address.");
    public static uint Add(uint address, uint offset) => checked(address + offset);

    public void ReadBytes(uint guestAddress, Span<byte> destination)
    {
        if (destination.IsEmpty) return;
        fixed (byte* data = destination) Access(5, guestAddress, data, checked((uint)destination.Length));
    }
    public void WriteBytes(uint guestAddress, ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return;
        fixed (byte* data = source) Access(6, guestAddress, data, checked((uint)source.Length));
    }
    public uint ReadUInt32(uint guestAddress)
    {
        Span<byte> bytes = stackalloc byte[4]; ReadBytes(guestAddress, bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
    public bool TryReadUInt32(uint guestAddress, out uint value)
    {
        byte* bytes = stackalloc byte[4];
        int result = NativePatchInterop.Access(invocation, 5, guestAddress, bytes, 4);
        value = result == 0 ? BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(bytes, 4)) : 0;
        if (result != 0 && result != 4) throw new InvalidOperationException($"Native patch read failed ({result}).");
        return result == 0;
    }
    public void WriteUInt32(uint guestAddress, uint value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        WriteBytes(guestAddress, bytes);
    }
    public uint ReadPointer(uint guestAddress) => GuestAddress(ReadUInt32(guestAddress));
    public void CallOriginal() => Access(7);
    public void SkipOriginal() => Access(8);
    public ulong CallGuest(GuestFunction function, ReadOnlySpan<ulong> arguments)
    {
        if (arguments.Length != function.ArgumentCount || arguments.Length > 8)
            throw new ArgumentException("Arguments must match the verified guest integer contract.");
        NativePatchInterop.GuestCall call = default;
        call.Address = function.Address; call.Count = (uint)arguments.Length;
        for (int i = 0; i < arguments.Length; ++i) call.Arguments[i] = arguments[i];
        Access(9, data: &call, size: (uint)sizeof(NativePatchInterop.GuestCall));
        return call.Result;
    }
}
