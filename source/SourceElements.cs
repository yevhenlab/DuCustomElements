using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using NQ;
using NQ.Interfaces;
using NQutils.Def;
using Orleans;

namespace CustomElements;

public sealed class SourceElements
{
    private readonly IClusterClient client;
    private readonly IGameplayBank bank;

    public SourceElements(IClusterClient client, IGameplayBank bank) { this.client = client; this.bank = bank; }

    public async Task<ModuleSource> Read(ulong construct)
    {
        var grain = client.GetConstructElementsGrain(construct);
        var all = new List<ElementInfo>();
        foreach (var id in await grain.GetElementsOfType<Element>()) all.Add(await grain.GetElement(id));
        var source = all.Where(e => !(bank.GetDefinition(e.elementType).BaseObject is CoreUnit))
            .OrderBy(e => e.elementType).ThenBy(e => e.position.x).ThenBy(e => e.position.y)
            .ThenBy(e => e.position.z).ThenBy(e => e.elementId).ToList();
        if (source.Count > 128) throw new InvalidOperationException("A custom module supports up to 128 DU elements.");
        var ids = source.Select((e, i) => (e.elementId, key: (ulong)i + 1)).ToDictionary(e => e.elementId, e => e.key);
        foreach (var core in all.Except(source)) ids[core.elementId] = 0;
        var result = new ModuleSource();
        foreach (var e in source) result.elements.Add(Export(e, ids[e.elementId]));
        foreach (var link in source.SelectMany(e => e.links).Distinct())
        {
            if (!ids.ContainsKey(link.fromElementId) || !ids.ContainsKey(link.toElementId))
                throw new InvalidOperationException("Module links must remain inside the source construct.");
            result.links.Add(new LinkInfo { fromElementId = ids[link.fromElementId],
                toElementId = ids[link.toElementId], fromPlug = link.fromPlug, toPlug = link.toPlug,
                plugType = link.plugType });
        }
        return result;
    }

    private EmbeddedElement Export(ElementInfo element, ulong key)
    {
        var definition = bank.GetDefinition(element.elementType);
        if (definition.Name.StartsWith("CustomVoxel", StringComparison.Ordinal) ||
            definition.Name.StartsWith("CustomPart", StringComparison.Ordinal))
            throw new InvalidOperationException("Nested custom modules are not supported yet.");
        if (definition.BaseObject is TerritoryUnit)
            throw new InvalidOperationException("Territory units cannot be embedded in a custom item.");
        return new EmbeddedElement { key = key, itemTypeId = element.elementType,
            assetAlias = string.IsNullOrEmpty(definition.BaseObject.AssetAlias) ? definition.Name : definition.BaseObject.AssetAlias,
            position = new[] { element.position.x, element.position.y, element.position.z },
            rotation = new[] { (double)element.rotation.x, element.rotation.y, element.rotation.z, element.rotation.w },
            properties = element.properties.Where(p =>
                definition.GetDynamicProperty<DynamicProperty>(p.Key)?.IsSavedInBlueprint == true)
                .ToDictionary(p => p.Key, p => p.Value.DeepCopy()) };
    }
}
