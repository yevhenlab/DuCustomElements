using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CustomElements;
using Newtonsoft.Json;
using NQ;
using NQ.Interfaces;

public sealed partial class MyDuMod
{
    private sealed class RenameRequest
    {
        public ulong construct;
        public ulong element;
        public ulong type;
        public string nonce;
        public string name;
    }

    private readonly ConcurrentDictionary<ulong, RenameRequest> renames = new();

    private async Task OpenRename(ulong player, ModAction action)
    {
        ulong type;
        if (action.actionId == 14) type = JsonConvert.DeserializeObject<RenameRequest>(action.payload).type;
        else type = (await client.GetConstructElementsGrain(action.constructId).GetElement(action.elementId)).elementType;
        var shape = (await compiler.List()).FirstOrDefault(s => s.itemTypeId == type);
        if (shape == null) throw new InvalidOperationException("Select the placed custom item's voxel mesh to rename it.");
        designs.CheckCreator(shape.itemTypeId, player);
        var request = new RenameRequest { construct = action.constructId, element = action.elementId,
            type = shape.itemTypeId, nonce = Guid.NewGuid().ToString("N"), name = definitions.DisplayName(shape) };
        renames[player] = request;
        string folder = Path.GetDirectoryName(panelPath);
        await Hud(player, "modinjectjs", File.ReadAllText(Path.Combine(folder, "custom-elements-name-panel.js")) +
            "\nwindow.customElementsNamePanel.open(" + JsonConvert.SerializeObject(request) + ");");
    }

    private void StartPreviewDelivery(ulong player)
    {
        _ = previewDelivery.Start(player, nonce => InstallPreview(player, nonce),
            message => Console.WriteLine($"CustomElements preview player={player}: {message}"));
    }

    private async Task InstallPreview(ulong player, string nonce = null)
    {
        var catalog = (await compiler.List()).Select(s => new { type = s.itemTypeId, hash = s.hash,
            name = definitions.DisplayName(s), canRename = designs.IsCreator(s.itemTypeId, player),
            elements = s.embeddedElements.GroupBy(e => e.itemTypeId).OrderBy(g => g.Key)
                .Select(g => new { type = g.Key, quantity = g.Count() }),
            materials = s.materialVolumes.OrderBy(m => m.Key)
                .Select(m => new { type = m.Key, quantity = m.Value }) });
        string script = File.ReadAllText(Path.Combine(Path.GetDirectoryName(panelPath), "custom-elements-preview.js"));
        await Hud(player, "modinjectjs", "if(typeof ItemInspector==='function' && " +
            "typeof inventoryPanelView!=='undefined' && inventoryPanelView.itemInspector){" +
            "window.customElementsItemCatalog=" +
            JsonConvert.SerializeObject(catalog) + ";window.customElementsPreviewDesigns=" +
            "window.customElementsItemCatalog.filter(function(d){return d.canRename;});\n" + script +
            (nonce == null ? "" : "\nif(typeof ItemInspector==='function' && " +
                "typeof inventoryPanelView!=='undefined' && inventoryPanelView.itemInspector && " +
                "window.customElementsPreview && window.customElementsPreview.ready===true)" +
                "CPPMod.sendModAction('CustomElements',22,[0,0]," + JsonConvert.SerializeObject(nonce) + ");") + "}");
    }

    private async Task Rename(ulong player, ModAction action)
    {
        var submitted = JsonConvert.DeserializeObject<RenameRequest>(action.payload);
        if (!renames.TryRemove(player, out var opened) || submitted == null ||
            opened.nonce != submitted.nonce || opened.construct != action.constructId || opened.element != action.elementId)
            throw new InvalidOperationException("The rename dialog expired. Reopen it and try again.");
        await generation.WaitAsync();
        try
        {
            var shape = (await compiler.List()).Single(s => s.itemTypeId == opened.type);
            designs.Rename(opened.type, player, submitted.name);
            await definitions.Refresh(shape);
            await Notify(player, "Shared name changed to “" + definitions.DisplayName(shape) +
                "”. All copies use this name after their client reconnects.", false);
        }
        finally { generation.Release(); }
    }
}
