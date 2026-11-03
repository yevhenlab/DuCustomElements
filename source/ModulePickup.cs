using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend;
using Backend.Database;
using Backend.Storage;
using NQ;
using NQ.Interfaces;
using NQutils.Def;
using NQutils.Storage;
using Orleans;

namespace CustomElements;

public sealed partial class ModuleHooks
{
    public Task<ElementInfo> Pickup(IIncomingGrainCallContext context, PlayerId player, ElementInConstruct target) =>
        Remove(context, player, target, false);

    public Task<ElementInfo> Destroy(IIncomingGrainCallContext context, PlayerId player, ElementInConstruct target) =>
        Remove(context, player, target, true);

    private async Task<ElementInfo> Remove(IIncomingGrainCallContext context, PlayerId player,
        ElementInConstruct target, bool destroy)
    {
        var owner = groups.Owner(target.elementId);
        if (owner != null) target = new ElementInConstruct { constructId = owner.construct, elementId = owner.parent };
        if (groups.Get(target.elementId) == null) { await context.Invoke(); return (ElementInfo)context.Result; }
        await gate.WaitAsync();
        try { return await RemoveGroup(player, target, destroy); }
        finally { gate.Release(); }
    }

    private async Task<ElementInfo> RemoveGroup(PlayerId player, ElementInConstruct target, bool destroy)
    {
        var group = groups.Get(target.elementId);
        if (group.construct != target.constructId || group.status == "stored" || group.status == "destroyed")
            throw Invalid("This custom module is not deployed on the selected construct.");
        var elements = client.GetConstructElementsGrain(group.construct);
        var construct = client.GetConstructGrain(group.construct);
        await construct.CheckBuild(player);
        var parent = (await elements.GetElement(group.parent)).DeepCopy();
        await SnapshotChildren(group, elements);
        await ValidatePickup(group, parent, player);
        bool stackable = !destroy && await CanStack(group, parent, player);
        group.status = destroy ? "destroying" : "pickingup"; groups.Save(group);
        var detached = new List<ModuleChild>();
        bool parentDetached = false, commitStarted = false, committed = false;
        try
        {
            foreach (var child in group.children.Where(c => c.active).ToList())
            {
                await elements.DeleteElement(child.element.elementId, player, false);
                detached.Add(child); child.active = false; groups.Save(group);
                await construct.ElementPickupPost(player, child.element);
            }
            if (destroy)
            {
                await elements.DeleteElement(parent.elementId, null, true);
                committed = true;
            }
            else
            {
                await StoreParent(player, parent, elements, stackable, () => parentDetached = true, () =>
                {
                    commitStarted = true; group.status = "storing"; groups.Save(group);
                });
                committed = true;
            }
            parentDetached = true;
            group.status = destroy ? "destroyed" : "stored"; groups.Save(group);
            if (destroy) await construct.ElementPickupPost(player, parent);
            if (destroy || stackable)
            {
                foreach (var child in group.children) await sql.DeleteElement(child.element.elementId);
                groups.Remove(parent.elementId);
            }
            if (!destroy) Console.WriteLine($"CustomElements module pickup parent={parent.elementId} stackable={stackable}");
            return parent;
        }
        catch (Exception error)
        {
            Console.WriteLine($"CustomElements module removal failed parent={parent.elementId}: {error}");
            // A committed inventory transfer must never be compensated by recreating the placed parent.
            if (commitStarted && !committed)
            {
                group.status = "pickup-commit-unknown"; groups.Save(group);
                throw Invalid("Module pickup commit outcome is uncertain. Its saved group needs reconciliation.");
            }
            if (!committed)
            {
                if (parentDetached) await client.GetPlayerGrain(player).AddConstructElement(parent);
                foreach (var child in detached)
                {
                    child.element = await client.GetPlayerGrain(player).AddConstructElement(child.element);
                    child.active = true;
                }
                group.status = "deployed"; groups.Save(group); await RestoreLinks(group);
            }
            throw;
        }
    }

    private async Task<bool> CanStack(ModuleGroup group, ElementInfo parent, PlayerId player)
    {
        if (!ModuleInventory.IsPristine(bank, await Template(group.itemTypeId), group, parent)) return false;
        foreach (var child in group.children.Where(c => bank.GetDefinition(c.element.elementType).BaseObject is ContainerUnit))
            if ((await storage.Get(StorageRef.Container(child.element.elementId), player)).content.Count > 0) return false;
        return true;
    }

    private async Task StoreParent(PlayerId player, ElementInfo parent, IConstructElementsGrain elements, bool stackable,
        Action detached, Action committing)
    {
        var (properties, propertiesToRemove) = ElementPropertiesHelper.PropertiesForInventory(bank, parent);
        await using var transaction = await storage.MakeTransaction(Tag.HttpCall("CustomElements.PickupModule"));
        await storage.GiveOrTakeItems(transaction, StorageRef.PlayerInventory(player), new[]
        {
            new ItemAndQuantity { item = new ItemInfo { type = parent.elementType, id = stackable ? 0 : parent.elementId,
                properties = properties }, quantity = 1 }
        }, new OperationOptions());
        await elements.DeleteElement(parent.elementId, player, stackable, propertiesToRemove);
        detached();
        await client.GetConstructGrain(parent.constructId).ElementPickupPost(player, parent);
        committing();
        await transaction.Commit();
    }

    private async Task ValidatePickup(ModuleGroup group, ElementInfo parent, PlayerId player)
    {
        var construct = client.GetConstructGrain(group.construct);
        var elements = client.GetConstructElementsGrain(group.construct);
        foreach (var element in group.children.Where(c => c.active).Select(c => c.element).Append(parent))
        {
            if (await elements.GetElementUser(element.elementId) != null)
                throw Invalid("Stop using the module's elements before picking it up.");
            await construct.ElementPickupPre(player, element);
        }
    }

    private async Task SnapshotChildren(ModuleGroup group, IConstructElementsGrain elements)
    {
        foreach (var child in group.children.Where(c => c.active))
            child.element = (await elements.GetElement(child.element.elementId)).DeepCopy();
        var ids = group.children.ToDictionary(c => c.element.elementId, c => c.key);
        ids[await TargetCore(group.construct)] = 0;
        group.links = group.children.Where(c => c.active).SelectMany(c => c.element.links).Distinct()
            .Where(l => ids.ContainsKey(l.fromElementId) && ids.ContainsKey(l.toElementId))
            .Select(l => new LinkInfo { fromElementId = ids[l.fromElementId], toElementId = ids[l.toElementId],
                fromPlug = l.fromPlug, toPlug = l.toPlug, plugType = l.plugType }).ToList();
    }
}
