using System.Runtime.InteropServices;

namespace LanguageCore.Runtime;

public static class HeapUtils
{
    static bool CheckString(ReadOnlySpan<byte> heap, int pointer)
    {
        if (pointer <= 0 || pointer + 1 >= heap.Length)
        { return false; }
        while (pointer + 1 < heap.Length)
        {
            if (heap.Get<char>(pointer) == '\0') return true;
            pointer += sizeof(char);
        }
        return false;
    }

    public static unsafe string? GetString(ReadOnlySpan<byte> heap, int pointer)
    {
        if (!CheckString(heap, pointer)) return null;

        fixed (byte* ptr = heap)
        {
            return Marshal.PtrToStringUni((nint)ptr + pointer);
        }
    }

    public readonly struct HeapBlock
    {
        public readonly bool IsUsed;
        public readonly int Size;

        public HeapBlock(bool isUsed, int size)
        {
            IsUsed = isUsed;
            Size = size;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    readonly struct GetHeapBlockResult
    {
        public readonly byte Status;
        public readonly int Size;
    }

    const string GetBlockCountIdentifier = "memprof_block_count";
    const string GetBlockIdentifier = "memprof_block_get";

    static bool PrepareAnalyzeMemory(
        FrozenDictionary<string, ExposedFunction>? exposedFunctions,
        [NotNullWhen(true)] out ExposedFunction getBlockCount,
        [NotNullWhen(true)] out ExposedFunction getBlock,
        [NotNullWhen(false)] out string? error)
    {
        getBlockCount = default;
        getBlock = default;

        if (exposedFunctions is null)
        {
            error = $"There are no exposed functions avaliable";
            return false;
        }

        if (!exposedFunctions.TryGetValue(GetBlockCountIdentifier, out getBlockCount))
        {
            error = $"Exposed function \"{GetBlockCountIdentifier}\" not found";
            return false;
        }

        if (getBlockCount.ArgumentsSize != 0)
        {
            error = $"Exposed function \"{GetBlockCountIdentifier}\" must not have any arguments";
            return false;
        }

        if (getBlockCount.ReturnValueSize != sizeof(int))
        {
            error = $"Exposed function \"{GetBlockCountIdentifier}\" must have a return type \"i32\"";
            return false;
        }

        if (!exposedFunctions.TryGetValue(GetBlockIdentifier, out getBlock))
        {
            error = $"Exposed function \"{GetBlockIdentifier}\" not found";
            return false;
        }

        if (getBlock.ArgumentsSize != sizeof(int))
        {
            error = $"Exposed function \"{GetBlockIdentifier}\" must have 1 parameter of type \"i32\"";
            return false;
        }

        unsafe
        {
            if (getBlock.ReturnValueSize != sizeof(GetHeapBlockResult))
            {
                error = $"Exposed function \"{GetBlockIdentifier}\" must have a return type of a struct with fields \"u8\" and \"i32\" in order";
                return false;
            }
        }

        error = null;
        return true;
    }

    public static bool AnalyzeMemorySync(
        BytecodeProcessor bytecodeProcessor,
        FrozenDictionary<string, ExposedFunction> exposedFunctions,
        [NotNullWhen(true)] out ImmutableArray<HeapBlock> blocks,
        [NotNullWhen(false)] out string? error)
    {
        blocks = ImmutableArray<HeapBlock>.Empty;

        if (!PrepareAnalyzeMemory(exposedFunctions, out ExposedFunction getBlockCount, out ExposedFunction getBlock, out error)) return false;

        BytecodeProcessor processor = new(
            bytecodeProcessor,
            bytecodeProcessor.Memory.ToArray()
        );
        processor.Registers.CodePointer = processor.Code.Length;

        int blockCount = processor.CallSync(getBlockCount).To<int>();

        if (blockCount < 0)
        {
            error = $"Exposed function \"{GetBlockCountIdentifier}\" returned {blockCount}";
            return false;
        }

        List<HeapBlock> result = new();

        for (int i = 0; i < blockCount; i++)
        {
            GetHeapBlockResult block = processor.CallSync(getBlock, i).To<GetHeapBlockResult>();
            if (block.Size <= 0) continue;
            result.Add(new HeapBlock(block.Status != 0, block.Size));
        }

        blocks = result.ToImmutableArray();
        return true;
    }

    public class AnalyzeMemoryTask
    {
        public readonly List<HeapBlock> Blocks;
        public string? Error;

        readonly BytecodeProcessor BytecodeProcessor;
        readonly ExposedFunction GetBlockCount;
        readonly ExposedFunction GetBlock;

        int BlockCount;
        int CurrentBlockIndex;
        UserCall? LastUserCall;

        AnalyzeMemoryTask(BytecodeProcessor bytecodeProcessor, ExposedFunction getBlockCount, ExposedFunction getBlock)
        {
            Blocks = new List<HeapBlock>();
            Error = null;

            BytecodeProcessor = bytecodeProcessor;
            GetBlockCount = getBlockCount;
            GetBlock = getBlock;

            BlockCount = -1;
            CurrentBlockIndex = 0;
            LastUserCall = null;
        }

        public static bool Create(
            BytecodeProcessor bytecodeProcessor,
            FrozenDictionary<string, ExposedFunction> exposedFunctions,
            [NotNullWhen(true)] out AnalyzeMemoryTask? task,
            [NotNullWhen(false)] out string? error)
        {
            if (!PrepareAnalyzeMemory(exposedFunctions, out ExposedFunction getBlockCount, out ExposedFunction getBlock, out error))
            {
                task = null;
                return false;
            }

            BytecodeProcessor processor = new(
                bytecodeProcessor,
                bytecodeProcessor.Memory.ToArray()
            );
            processor.Registers.CodePointer = processor.Code.Length;

            task = new AnalyzeMemoryTask(processor, getBlockCount, getBlock);
            return true;
        }

        public bool Tick(int tickCount)
        {
            if (BlockCount < 0)
            {
                if (LastUserCall is null) LastUserCall = BytecodeProcessor.Call(GetBlockCount);

                for (int i = 0; i < tickCount && LastUserCall.Result is null; i++)
                {
                    BytecodeProcessor.Tick();
                }

                if (LastUserCall.Result is null) return false;

                BlockCount = LastUserCall.Result.To<int>();
                LastUserCall = null;

                if (BlockCount < 0)
                {
                    Error = $"Exposed function \"{GetBlockCountIdentifier}\" returned {BlockCount}";
                    return true;
                }
            }

            if (CurrentBlockIndex < BlockCount)
            {
                if (LastUserCall is null) LastUserCall = BytecodeProcessor.Call(GetBlock, CurrentBlockIndex);

                for (int i = 0; i < tickCount && LastUserCall.Result is null; i++)
                {
                    BytecodeProcessor.Tick();
                }

                if (LastUserCall.Result is null) return false;

                GetHeapBlockResult block = LastUserCall.Result.To<GetHeapBlockResult>();
                LastUserCall = null;
                CurrentBlockIndex++;

                if (block.Size <= 0) return false;

                Blocks.Add(new HeapBlock(block.Status != 0, block.Size));

                return false;
            }

            return true;
        }
    }
}

[ExcludeFromCodeCoverage]
public static class BytecodeHeapImplementation
{
    const uint BlockStatusMask = 0x80000000;
    const uint BlockSizeMask = 0x7fffffff;
    public const int HeaderSize = sizeof(uint);

    public static int GetUsedSize(ReadOnlySpan<byte> heap)
    {
        int used = 0;
        int i = 0;
        while (i + HeaderSize < 127)
        {
            (int blockSize, bool blockIsUsed) = GetHeader(heap, i);
            if (blockIsUsed) used += blockSize;
            i += blockSize + HeaderSize;
        }
        return used;
    }

    public static (int Size, bool Allocated) GetHeader(ReadOnlySpan<byte> memory, int headerPointer) => (
        (memory.Get<uint>(headerPointer) & BlockSizeMask).I32(),
        (memory.Get<uint>(headerPointer) & BlockStatusMask) != 0
    );
}

[ExcludeFromCodeCoverage]
public static class BrainfuckHeapImplementation
{
    const byte BlockStatusMask = 0x80;
    const byte BlockSizeMask = 0x7f;
    public const int HeaderSize = sizeof(byte);

    public static int GetUsedSize(ReadOnlySpan<byte> heap)
    {
        int used = 0;
        int i = 0;
        while (i + HeaderSize < 127)
        {
            (int blockSize, bool blockIsUsed) = GetHeader(heap, i);
            if (blockIsUsed) used += blockSize;
            i += blockSize + HeaderSize;
        }
        return used;
    }

    public static (int Size, bool Allocated) GetHeader(ReadOnlySpan<byte> memory, int headerPointer) => (
        (memory.Get<byte>(headerPointer) & BlockSizeMask).I32(),
        (memory.Get<byte>(headerPointer) & BlockStatusMask) != 0
    );
}
