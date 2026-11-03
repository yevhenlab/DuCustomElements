using System.Collections.Generic;
using System.Linq;
using Backend;
using NQ;
using NQutils.Def;

namespace CustomElements;

public static class ModuleInventory
{
    public static bool IsPristine(IGameplayBank bank, Shape template, ModuleGroup group, ElementInfo parent)
    {
        if (ElementPropertiesHelper.PropertiesForInventory(bank, parent).Item1.Count != 0 ||
            group.children.Count != template.embeddedElements.Count || group.children.Any(c => !c.active)) return false;
        foreach (var source in template.embeddedElements)
        {
            var child = group.children.SingleOrDefault(c => c.key == source.key);
            if (child == null) return false;
            var actual = Persistent(bank, child.element.elementType, child.element.properties);
            var expected = Persistent(bank, source.itemTypeId, source.properties);
            if (actual.Count != expected.Count || actual.Any(p =>
                !expected.TryGetValue(p.Key, out var value) || !p.Value.Equals(value))) return false;
        }
        return new HashSet<string>(group.links.Select(LinkKey)).SetEquals(template.embeddedLinks.Select(LinkKey));
    }

    private static Dictionary<string, PropertyValue> Persistent(IGameplayBank bank, ulong type,
        Dictionary<string, PropertyValue> properties)
    {
        var definition = bank.GetDefinition(type);
        return properties.Where(pair =>
        {
            var property = definition.GetDynamicProperty<DynamicProperty>(pair.Key);
            if (property == null) return true; // Preserve unknown saved state rather than discard it.
            return !definition.HasStaticProperty(pair.Key) &&
                (property.IsSavedInInventory || property.IsSavedInBlueprint) && !property.IsDefault(pair.Value);
        }).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static string LinkKey(LinkInfo link) =>
        $"{link.fromElementId}:{link.fromPlug}:{link.toElementId}:{link.toPlug}:{(int)link.plugType}";
}
