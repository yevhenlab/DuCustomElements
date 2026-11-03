using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend;
using Backend.Storage;
using NQ;
using NQ.Grains.Core;
using NQ.Interfaces;
using NQutils;
using NQutils.Def;
using NQutils.Exceptions;
using NQutils.Sql;
using Orleans;

namespace CustomElements;

public sealed partial class ModuleHooks
{
    private readonly IClusterClient client;
    private readonly IGameplayBank bank;
    private readonly IItemStorageService storage;
    private readonly Compiler compiler;
    private readonly ISql sql;
    private readonly ModuleGroups groups;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, Shape> modules = new();

    public ModuleHooks(IClusterClient client, IGameplayBank bank, IItemStorageService storage,
        Compiler compiler, ISql sql, string folder)
    {
        this.client = client; this.bank = bank; this.storage = storage; this.compiler = compiler;
        this.sql = sql;
        groups = new ModuleGroups(Path.Combine(folder, "custom-element-groups"));
    }

    public void Install(IHookCallManager hooks)
    {
        hooks.Register("ElementManagementGrain.ElementAdd", HookMode.Replace, this, nameof(Add));
        hooks.Register("ElementManagementGrain.MoveElement", HookMode.Replace, this, nameof(Move));
        hooks.Register("ElementManagementGrain.ElementDelete", HookMode.Replace, this, nameof(Pickup));
        hooks.Register("ElementManagementGrain.ElementDestroy", HookMode.Replace, this, nameof(Destroy));
        hooks.Register("ElementManagementGrain.SalvageElement", HookMode.PreCall, this, nameof(CheckSalvage));
        hooks.Register("BlueprintGrain.GetBlueprintInfo", HookMode.Replace, this, nameof(BlueprintInfo));
        hooks.Register("BlueprintGrain.Use", HookMode.Replace, this, nameof(UseBlueprint));
        hooks.Register("BlueprintGrain.UseRaw", HookMode.Replace, this, nameof(UseBlueprint));
    }

    public void Remember(Shape shape)
    {
        // Asset buffers belong to streaming requests; the module cache retains only its small template.
        modules[shape.itemTypeId] = new Shape { itemTypeId = shape.itemTypeId, name = shape.name, hash = shape.hash,
            embeddedElements = shape.embeddedElements, embeddedLinks = shape.embeddedLinks };
    }

    public ModuleGroup Group(ulong parent) => groups.Get(parent);

    private bool IsCustom(ulong type) => bank.GetDefinition(type)?.Name.StartsWith("CustomVoxel",
        StringComparison.Ordinal) == true;

    private async Task<Shape> Template(ulong type)
    {
        if (!modules.ContainsKey(type)) foreach (var shape in await compiler.List()) Remember(shape);
        if (!modules.TryGetValue(type, out var result)) throw Invalid("Custom item template is unavailable.");
        return result;
    }

    private static BusinessException Invalid(string message) => new(NQ.ErrorCode.InvalidParameters, message);

    private void CheckChild(ulong id)
    {
        if (groups.Owner(id) != null) throw Invalid("This element belongs to a custom module. Select its parent item.");
    }

    public async Task<ElementInfo> Add(IIncomingGrainCallContext context, PlayerId player, ElementDeploy deploy)
    {
        if (!IsCustom(deploy.element.elementType))
        {
            await context.Invoke(); return (ElementInfo)context.Result;
        }
        await gate.WaitAsync();
        try { return await Deploy(context, player, deploy); }
        finally { gate.Release(); }
    }

    private async Task<ElementInfo> Deploy(IIncomingGrainCallContext context, PlayerId player, ElementDeploy deploy)
    {
        deploy.element.elementId = deploy.fromInventory.instanceId;
        var template = await Template(deploy.element.elementType);
        foreach (var child in template.embeddedElements)
            await client.GetConstructGrain(deploy.element.constructId).ValidateElementLocation(
                ModuleTransforms.Child(deploy.element, child).Location);
        await context.Invoke();
        var parent = (ElementInfo)context.Result;
        if (template.embeddedElements.Count == 0) return parent;
        var group = groups.Get(parent.elementId) ?? new ModuleGroup { parent = parent.elementId,
            itemTypeId = parent.elementType, links = template.embeddedLinks };
        group.construct = parent.constructId; group.status = "deploying"; groups.Save(group);
        try
        {
            foreach (var child in template.embeddedElements) await DeployChild(player, parent, child, template, group);
            await RestoreLinks(group);
            group.status = "deployed"; groups.Save(group);
            return parent;
        }
        catch (Exception error)
        {
            group.status = "failed"; groups.Save(group);
            Console.WriteLine($"CustomElements module deployment failed parent={parent.elementId}: {error}");
            throw Invalid("Module deployment stopped. Pick up its parent item to preserve it, then retry.");
        }
    }

    private async Task DeployChild(PlayerId player, ElementInfo parent, EmbeddedElement template,
        Shape shape, ModuleGroup group)
    {
        var previous = group.children.FirstOrDefault(c => c.key == template.key);
        var child = ModuleTransforms.Child(parent, template, previous?.element.elementId ?? 0);
        if (previous != null) child.properties.Clear();
        child.elementType = GameplayObject.IdFor(Definitions.ChildName(shape, template.itemTypeId, bank));
        child = await client.GetPlayerGrain(player).AddConstructElement(child);
        if (previous == null) group.children.Add(new ModuleChild { key = template.key, element = child, active = true });
        else { previous.element = child; previous.active = true; }
        groups.Save(group);
    }

    private async Task RestoreLinks(ModuleGroup group)
    {
        var links = await ModuleLinks(group);
        if (links.Count > 0) await client.GetConstructElementsGrain(group.construct).BatchEdit(
            new LinkBatchEdit { constructId = group.construct, toCreate = links });
    }

    private async Task<List<LinkInfo>> ModuleLinks(ModuleGroup group)
    {
        var ids = group.children.Where(c => c.active).ToDictionary(c => c.key, c => c.element.elementId);
        ids[0] = await TargetCore(group.construct);
        return group.links.Where(l => ids.ContainsKey(l.fromElementId) && ids.ContainsKey(l.toElementId))
            .Select(l => new LinkInfo { constructId = group.construct, fromElementId = ids[l.fromElementId],
                toElementId = ids[l.toElementId], fromPlug = l.fromPlug, toPlug = l.toPlug, plugType = l.plugType }).ToList();
    }

    private async Task<ulong> TargetCore(ulong construct)
    {
        var ids = await client.GetConstructElementsGrain(construct).GetElementsOfType<CoreUnit>();
        return ids.Count > 0 ? (ulong)ids[0] : throw Invalid("Target construct has no core.");
    }

    public async Task<ElementInfo> Move(IIncomingGrainCallContext context, PlayerId player, ElementLocation target)
    {
        var owner = groups.Owner(target.elementId);
        if (owner != null)
        {
            var template = await Template(owner.itemTypeId);
            var key = owner.children.Single(c => c.element.elementId == (ulong)target.elementId).key;
            var local = template.embeddedElements.Single(e => e.key == key);
            await client.GetElementManagementGrain().MoveElement(player, new ElementLocation
                { elementId = owner.parent, location = ModuleTransforms.ParentLocation(target.location, local) });
            return await client.GetConstructElementsGrain(owner.construct).GetElement(target.elementId);
        }
        var group = groups.Get(target.elementId);
        if (group == null) { await context.Invoke(); return (ElementInfo)context.Result; }
        await gate.WaitAsync();
        try { return await MoveGroup(context, player, target, groups.Get(target.elementId)); }
        finally { gate.Release(); }
    }

    private async Task<ElementInfo> MoveGroup(IIncomingGrainCallContext context, PlayerId player,
        ElementLocation target, ModuleGroup group)
    {
        if (group.status != "deployed") throw Invalid("Pick up the incomplete module before moving it.");
        var template = await Template(group.itemTypeId);
        var parent = await client.GetConstructElementsGrain(group.construct).GetElement(group.parent);
        var moved = parent.DeepCopy(); moved.position = target.location.position; moved.rotation = target.location.rotation;
        foreach (var child in template.embeddedElements)
            await client.GetConstructGrain(group.construct).ValidateElementLocation(ModuleTransforms.Child(moved, child).Location);
        await context.Invoke();
        try
        {
            foreach (var child in group.children)
            {
                var source = template.embeddedElements.Single(e => e.key == child.key);
                var position = ModuleTransforms.Child(moved, source, child.element.elementId);
                await client.GetConstructElementsGrain(group.construct).MoveElement(new ElementLocation
                    { elementId = child.element.elementId, location = position.Location });
            }
            return (ElementInfo)context.Result;
        }
        catch
        {
            await client.GetConstructElementsGrain(group.construct).MoveElement(new ElementLocation
                { elementId = parent.elementId, location = parent.Location });
            foreach (var child in group.children)
            {
                var source = template.embeddedElements.Single(e => e.key == child.key);
                await client.GetConstructElementsGrain(group.construct).MoveElement(new ElementLocation
                    { elementId = child.element.elementId, location = ModuleTransforms.Child(parent, source).Location });
            }
            throw;
        }
    }

    public Task CheckSalvage(string grainKey, PlayerId player, ElementId element)
    {
        CheckChild(element);
        if (groups.Get(element) != null) throw Invalid("Pick up the custom module before recycling it.");
        return Task.CompletedTask;
    }
}
