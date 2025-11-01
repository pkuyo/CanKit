using System.Buffers;

namespace CanKit.Abstractions.SPI.Common;


/// <summary>
/// Provides a strategy for allocating payload buffers used by CAN frames.
/// 提供为 CAN 帧分配负载缓冲区的策略接口。
/// </summary>
public interface IBufferAllocator
{
    /// <summary>
    /// Rents a buffer with the specified length.
    /// 按指定长度租借一个缓冲区。
    /// </summary>
    /// <param name="length">The required buffer length in bytes. （需要的缓冲区字节数。）</param>
    /// <param name="zeroFill">Whether to zero-initialize the returned memory. （是否对返回内存进行清零初始化。）</param>
    /// <returns>
    /// An <see cref="IMemoryOwner{T}"/> that owns the rented memory and must be disposed when no longer needed
    /// (depending on <see cref="FrameNeedDispose"/>).
    ///  一个 <see cref="IMemoryOwner{T}"/>，用于持有租借到的内存；在不再需要时应释放（是否必须释放取决于 <see cref="FrameNeedDispose"/>）。
    /// </returns>
    IMemoryOwner<byte> Rent(int length, bool zeroFill = false);

    /// <summary>
    /// Indicates whether frames created with buffers from this allocator require disposing
    /// to correctly release the underlying memory.
    /// 指示使用此分配器创建的帧是否需要调用 <c>Dispose</c> 才能正确释放底层内存。
    /// </summary>
    /// <remarks>
    /// If true, callers should dispose frames (or the returned memory owner) to return buffers to the pool.
    /// If false, disposing is a no-op for the underlying memory (e.g., plain arrays) and is optional.
    /// 若为 true，则调用方应释放帧（或内存拥有者）以将缓冲区归还池中；若为 false，释放对底层内存（例如普通数组）无影响，可选。
    /// </remarks>
    bool FrameNeedDispose { get; }
}
