using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

public static class FieldExtensions
{
    public static string TypeName(this SerializedField field) => field.Schema.Name?.FullName ?? "?";

    public static SerializedField? Member(this SerializedField field, string name)
    {
        foreach (var m in field.Members)
            if (m.Name == name)
                return m;
        return null;
    }

    /// <summary>Depth-first walk yielding each field with its path from the root.</summary>
    public static IEnumerable<(SerializedField Field, IReadOnlyList<SerializedField> Path)> Walk(this SerializedField root)
    {
        var path = new List<SerializedField>();
        var stack = new Stack<(SerializedField Field, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (field, depth) = stack.Pop();
            path.RemoveRange(depth, path.Count - depth);
            yield return (field, path);
            path.Add(field);
            var members = field.Members;
            for (int i = members.Length - 1; i >= 0; i--)
                stack.Push((members[i], depth + 1));
        }
    }
}
