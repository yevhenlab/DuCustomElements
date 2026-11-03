using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Backend;
using NQ;
using NQutils;

namespace CustomElements;

public sealed class Assets
{
    private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> queues = new();
    private readonly IPub pub;
    private readonly ChildResources children;

    public Assets(IPub pub, IGameplayBank bank)
    {
        this.pub = pub;
        children = new ChildResources(bank, Path.Combine(
            Path.GetDirectoryName(typeof(Assets).Assembly.Location), "element-resources.json"));
    }

    public Task Send(ulong player, Shape shape) => SendAll(player, new[] { shape });

    public async Task SendAll(ulong player, IEnumerable<Shape> shapes)
    {
        var entries = shapes.ToList();
        var queue = queues.GetOrAdd(player, _ => new SemaphoreSlim(1, 1));
        await queue.WaitAsync();
        try
        {
            // PubSub uses multiple publishers: awaiting NotifyTopic does not preserve client arrival order.
            // Match the official raw ModLoader's phase barriers before loading dependent elements.
            foreach (var asset in entries.SelectMany(shape => shape.assets).Where(asset => asset.kind != 1))
                await SendAsset(player, asset);
            await Task.Delay(1000);
            var childDefinition = children.For(entries);
            await Send(player, new VoxelEdit { constructId = 0, flags = 1,
                operation = Encoding.UTF8.GetBytes(childDefinition.ToString(Newtonsoft.Json.Formatting.None)) });
            await Task.Delay(1000);
            foreach (var child in ((Newtonsoft.Json.Linq.JObject)childDefinition["elements"]).Properties())
                await Send(player, new VoxelEdit { constructId = 0, flags = 3, hashContext = child.Name });
            await Task.Delay(500);
            // Native flag 6 also calls ItemLib's icon-map rebuild. Flag 3 only updates ElementLibrary.
            // Dual 1.4.18: resource handler 0x160873e -> thunk 0x64ce -> ItemLib refresh 0xc5f640.
            if (entries.Count > 0)
                await Send(player, new VoxelEdit { constructId = 0, flags = 6 });
            await Task.Delay(500);
        }
        finally { queue.Release(); }
    }

    private Task SendAsset(ulong player, Asset asset)
    {
        if (!asset.path.StartsWith("resources_generated/custom_elements/", StringComparison.Ordinal) ||
            asset.path.Contains("..") || (asset.kind != 1 && asset.kind != 2))
            throw new InvalidOperationException("Invalid generated asset path.");
        return Send(player, new VoxelEdit { constructId = 0, flags = (ulong)asset.kind,
            operation = asset.data, hashContext = asset.kind == 1 ? "" : asset.path });
    }

    private Task Send(ulong player, VoxelEdit edit) => pub.NotifyTopic(Topics.PlayerNotifications(player),
        new NQutils.Messages.VoxelCsgApplied(edit));
}
