using System;
using System.Collections.Generic;
using System.Linq;
using NQ;

namespace CustomElements;

public static class BlueprintModules
{
    public static bool SamePlacement(ElementInfo a, ElementInfo b)
    {
        if (a.elementType != b.elementType) return false;
        double distance = Math.Abs(a.position.x - b.position.x) + Math.Abs(a.position.y - b.position.y) +
            Math.Abs(a.position.z - b.position.z);
        double dot = a.rotation.x * b.rotation.x + a.rotation.y * b.rotation.y +
            a.rotation.z * b.rotation.z + a.rotation.w * b.rotation.w;
        return distance < 0.0001 && Math.Abs(Math.Abs(dot) - 1) < 0.0001;
    }

    public static Dictionary<ulong, ulong> Match(IEnumerable<ElementInfo> source, IEnumerable<ElementInfo> target)
    {
        var remaining = target.ToList();
        var result = new Dictionary<ulong, ulong>();
        foreach (var element in source)
        {
            var match = remaining.FirstOrDefault(candidate => SamePlacement(element, candidate));
            if (match == null) continue;
            result[element.elementId] = match.elementId; remaining.Remove(match);
        }
        return result;
    }

    public static List<LinkInfo> RemapLinks(IEnumerable<LinkInfo> source, IReadOnlyDictionary<ulong, ulong> ids,
        ISet<ulong> children, ulong construct) => source
        .Where(link => (children.Contains(link.fromElementId) || children.Contains(link.toElementId)) &&
            ids.ContainsKey(link.fromElementId) && ids.ContainsKey(link.toElementId))
        .Select(link => new LinkInfo { constructId = construct, fromElementId = ids[link.fromElementId],
            toElementId = ids[link.toElementId], fromPlug = link.fromPlug, toPlug = link.toPlug,
            plugType = link.plugType }).Distinct().ToList();
}
