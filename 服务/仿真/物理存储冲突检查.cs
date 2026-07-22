using QemuWG.数据;

namespace QemuWG.服务;

public static class 物理存储冲突检查
{
    public static bool 存在冲突(IEnumerable<物理存储挂载> storages)
    {
        var items = storages.ToList();
        for (var left = 0; left < items.Count; left++)
        for (var right = left + 1; right < items.Count; right++)
        {
            if (互相冲突(items[left], items[right])) return true;
        }
        return false;
    }

    public static bool 互相冲突(物理存储挂载 left, 物理存储挂载 right)
    {
        if (!属于同一磁盘(left, right)) return false;
        if (是整盘(left) || 是整盘(right)) return true;
        if (left.PartitionNumber > 0 && left.PartitionNumber == right.PartitionNumber) return true;
        if (left.Size <= 0 || right.Size <= 0) return false;

        var leftEnd = checked(left.Offset + left.Size);
        var rightEnd = checked(right.Offset + right.Size);
        return left.Offset < rightEnd && right.Offset < leftEnd;
    }

    public static bool 运行时互相冲突(物理存储挂载 left, 物理存储挂载 right) =>
        属于同一磁盘(left, right)
        && (!left.ReadOnly || !right.ReadOnly || 互相冲突(left, right));

    private static bool 属于同一磁盘(物理存储挂载 left, 物理存储挂载 right)
    {
        if (!string.IsNullOrWhiteSpace(left.UniqueId) && !string.IsNullOrWhiteSpace(right.UniqueId))
            return string.Equals(left.UniqueId, right.UniqueId, StringComparison.OrdinalIgnoreCase);
        if (left.DiskNumber >= 0 && right.DiskNumber >= 0) return left.DiskNumber == right.DiskNumber;
        return string.Equals(left.DevicePath, right.DevicePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool 是整盘(物理存储挂载 storage) =>
        string.Equals(storage.Kind, "disk", StringComparison.OrdinalIgnoreCase);
}
