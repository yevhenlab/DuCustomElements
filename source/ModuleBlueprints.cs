using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Database;
using NQ;
using NQ.Interfaces;
using NQutils;
using NQutils.Def;
using Orleans;

namespace CustomElements;

public sealed partial class ModuleHooks
{
    private bool IsModuleChild(ulong type) => bank.GetDefinition(type)?.Name.StartsWith("CustomPart",
        StringComparison.Ordinal) == true;

    public async Task<BlueprintProperties> BlueprintInfo(IIncomingGrainCallContext context, BlueprintId id)
    {
        await context.Invoke();
        var info = Newtonsoft.Json.JsonConvert.DeserializeObject<BlueprintProperties>(
            Newtonsoft.Json.JsonConvert.SerializeObject(context.Result));
        info.requiredItems.RemoveAll(item => IsModuleChild(item.id));
        return info;
    }

    public async Task<ConstructId> UseBlueprint(IIncomingGrainCallContext context, BlueprintDeploy deploy,
        ulong player)
    {
        var info = await sql.GetBlueprint(deploy.blueprintId);
        ulong sourceId = info.dataId ?? deploy.blueprintId;
        var source = await sql.GetElementsInBlueprint(sourceId);
        if (!source.Any(element => IsCustom(element.elementType)))
        {
            await context.Invoke(); return (ConstructId)context.Result;
        }
        var excludes = deploy.excludeElements.ToList();
        // Children remain in the snapshot for their saved state, but are included in wrapper costs.
        deploy.excludeElements = excludes.Concat(source.Where(e => IsModuleChild(e.elementType))
            .Select(e => e.elementType)).Distinct().ToList();
        ConstructId result;
        try { await context.Invoke(); result = (ConstructId)context.Result; }
        finally { deploy.excludeElements = excludes; }
        await ExpandBlueprint(player, result, source, await sql.GetLinksInBlueprint(sourceId));
        return result;
    }

    public async Task RepairBlueprintConstruct(ulong player, ulong construct)
    {
        await client.GetConstructGrain(construct).CheckBuild(player);
        var properties = await sql.GetJsonProperties(construct);
        ulong blueprint = properties.serverProperties.blueprintId ?? 0;
        if (blueprint == 0) throw Invalid("This construct has no source blueprint reference.");
        var info = await sql.GetBlueprint(blueprint);
        ulong source = info.dataId ?? blueprint;
        await ExpandBlueprint(player, construct, await sql.GetElementsInBlueprint(source),
            await sql.GetLinksInBlueprint(source));
    }

    private async Task ExpandBlueprint(ulong player, ulong construct, List<ElementInfo> source,
        List<LinkInfo> links)
    {
        await gate.WaitAsync();
        try
        {
            var target = await sql.GetElementsInConstruct(construct);
            var ids = BlueprintModules.Match(source.Where(e => !IsModuleChild(e.elementType)), target);
            var mappedChildren = new HashSet<ulong>();
            var remaining = source.Where(e => IsModuleChild(e.elementType)).ToList();
            var deployedChildren = target.Where(e => IsModuleChild(e.elementType)).ToList();
            var fallback = new List<LinkInfo>();
            var created = new List<ModuleGroup>();
            foreach (var parent in target.Where(e => IsCustom(e.elementType)))
            {
                if (groups.Get(parent.elementId) != null) continue;
                var original = source.FirstOrDefault(e => ids.GetValueOrDefault(e.elementId) == parent.elementId);
                if (original == null) throw Invalid("Could not match the deployed custom item to its blueprint.");
                created.Add(await ExpandBlueprintModule(player, parent, original, remaining, deployedChildren,
                    ids, mappedChildren, fallback));
            }
            var existing = await sql.GetLinksInConstruct(construct);
            var edits = BlueprintModules.RemapLinks(links, ids, mappedChildren, construct).Concat(fallback)
                .Distinct().Where(link => !existing.Contains(link)).ToList();
            if (edits.Count > 0) await client.GetConstructElementsGrain(construct).BatchEdit(
                new LinkBatchEdit { constructId = construct, toCreate = edits });
            foreach (var group in created) { group.status = "deployed"; groups.Save(group); }
            Console.WriteLine($"CustomElements blueprint expanded construct={construct} modules={created.Count} " +
                $"children={created.Sum(g => g.children.Count)} links={edits.Count}");
        }
        finally { gate.Release(); }
    }

    private async Task<ModuleGroup> ExpandBlueprintModule(ulong player, ElementInfo parent, ElementInfo original,
        List<ElementInfo> remaining, List<ElementInfo> deployed, Dictionary<ulong, ulong> ids,
        HashSet<ulong> mappedChildren, List<LinkInfo> fallback)
    {
        var template = await Template(parent.elementType);
        var group = new ModuleGroup { parent = parent.elementId, itemTypeId = parent.elementType,
            construct = parent.constructId, links = template.embeddedLinks };
        groups.Save(group);
        bool incompleteSnapshot = false;
        foreach (var part in template.embeddedElements)
        {
            var expected = ModuleTransforms.Child(original, part);
            expected.elementType = GameplayObject.IdFor(Definitions.ChildName(template, part.itemTypeId, bank));
            var saved = remaining.FirstOrDefault(e => BlueprintModules.SamePlacement(expected, e));
            var instance = Newtonsoft.Json.JsonConvert.DeserializeObject<EmbeddedElement>(
                Newtonsoft.Json.JsonConvert.SerializeObject(part));
            if (saved != null) instance.properties = saved.DeepCopy().properties;
            else incompleteSnapshot = true;
            var location = ModuleTransforms.Child(parent, part); location.elementType = expected.elementType;
            var existing = deployed.FirstOrDefault(e => BlueprintModules.SamePlacement(location, e));
            if (existing == null) await DeployChild(player, parent, instance, template, group);
            else
            {
                group.children.Add(new ModuleChild { key = part.key, element = existing, active = true });
                groups.Save(group); deployed.Remove(existing);
            }
            if (saved == null) continue;
            ids[saved.elementId] = group.children.Single(c => c.key == part.key).element.elementId;
            mappedChildren.Add(saved.elementId); remaining.Remove(saved);
        }
        // Older blueprints may contain just the wrapper, with no component snapshot at all.
        if (incompleteSnapshot) fallback.AddRange(await ModuleLinks(group));
        return group;
    }
}
