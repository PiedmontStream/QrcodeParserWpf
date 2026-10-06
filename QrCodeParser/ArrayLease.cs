using System;
using System.Buffers;
using System.Reflection;
namespace QrCodeParser;


public class ArrayPoolGuard
{
    public static ArrayPoolGuard<T> Rent<T>(int minimumLength)
    {
        return ArrayPoolGuard<T>.Rent(minimumLength);
    }
}

public sealed class ArrayPoolGuard<T> : ArrayPoolGuard, IDisposable
{
    private readonly T[] _array;
    private readonly ArrayPool<T> _pool;

    public static ArrayPoolGuard<T> Rent(int minimumLength)
    {
        T[]? rent = null;
        try
        {
            rent = ArrayPool<T>.Shared.Rent(minimumLength: minimumLength);
            return new ArrayPoolGuard<T>(rent);
        }
        catch
        {
            if (rent != null)
            {
                ArrayPool<T>.Shared.Return(rent);
            }
            throw;
        }
    }
    private ArrayPoolGuard(T[] array, ArrayPool<T>? pool = null)
    {
        _array = array;
        _pool = pool ?? ArrayPool<T>.Shared;
    }

    public T[] Buffer => _array;

    // 离开 using 作用域时，自动将数组归还
    public void Dispose()
    {
        if (_array != null)
        {
            _pool.Return(_array, clearArray: false); // 视情况决定是否清空数组
        }
    }
}
