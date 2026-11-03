using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Backend;
using CustomElements;
using Newtonsoft.Json;
using NQ;
using NQutils;

public sealed class MyDuMod : IMod
{
    private Compiler compiler;
    private Assets assets;
    private IPub pub;

    public string GetName() => "SyncLoader";

    public Task Initialize(IServiceProvider services)
    {
        string folder = Path.GetDirectoryName(typeof(Assets).Assembly.Location);
        var settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(
            Path.Combine(folder, "custom-elements.json")));
        compiler = new Compiler(settings, (IGameplayBank)services.GetService(typeof(IGameplayBank)));
        pub = (IPub)services.GetService(typeof(IPub));
        assets = new Assets(pub, (IGameplayBank)services.GetService(typeof(IGameplayBank)));
        return Task.CompletedTask;
    }

    public async Task<ModInfo> GetModInfoFor(ulong playerId, bool admin)
    {
        // ItemLib builds a separate icon map during login. Register resources before returning mod info,
        // as recommended by the native ModLoader protocol, rather than waiting only for action 0.
        var shapes = await compiler.List();
        foreach (var shape in shapes) Runtime.RegisterBounds?.Invoke(shape);
        await assets.SendAll(playerId, shapes);
        return new ModInfo { name = GetName(), actions = new List<ModActionDefinition>() };
    }

    public async Task TriggerAction(ulong playerId, ModAction action)
    {
        if (action.actionId != 0) throw new InvalidOperationException("Unknown synchronous loader action.");
        try
        {
            var shapes = await compiler.List();
            foreach (var shape in shapes) Runtime.RegisterBounds?.Invoke(shape);
            await assets.SendAll(playerId, shapes);
        }
        finally
        {
            await pub.NotifyTopic(Topics.PlayerNotifications(playerId),
                new NQutils.Messages.VoxelCsgApplied(new VoxelEdit { constructId = 0, flags = 8 }));
        }
        if (Runtime.AssetsReady != null) await Runtime.AssetsReady(playerId);
    }
}
