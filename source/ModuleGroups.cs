using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NQ;

namespace CustomElements;

public sealed class ModuleChild
{
    public ulong key { get; set; }
    public ElementInfo element { get; set; }
    public bool active { get; set; }
}

public sealed class ModuleGroup
{
    public ulong parent { get; set; }
    public ulong itemTypeId { get; set; }
    public ulong construct { get; set; }
    public string status { get; set; } = "deploying";
    public List<ModuleChild> children { get; set; } = new();
    public List<LinkInfo> links { get; set; } = new();
}

public sealed class ModuleGroups
{
    private readonly string folder;
    private readonly ConcurrentDictionary<ulong, ModuleGroup> groups = new();

    public ModuleGroups(string folder)
    {
        this.folder = folder;
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(folder, "*.json"))
        {
            var group = JsonConvert.DeserializeObject<ModuleGroup>(File.ReadAllText(path));
            if (group == null || group.parent == 0) throw new InvalidOperationException("Invalid module group file.");
            groups[group.parent] = group;
        }
    }

    private static ModuleGroup Copy(ModuleGroup group) => group == null ? null :
        JsonConvert.DeserializeObject<ModuleGroup>(JsonConvert.SerializeObject(group));
    public ModuleGroup Get(ulong parent) => Copy(groups.GetValueOrDefault(parent));
    public ModuleGroup Owner(ulong child) => groups.Values.FirstOrDefault(g =>
        g.children.Any(c => c.element.elementId == child));
    public void Remove(ulong parent)
    {
        File.Delete(Path.Combine(folder, parent + ".json"));
        groups.TryRemove(parent, out _);
    }

    public void Save(ModuleGroup group)
    {
        string path = Path.Combine(folder, group.parent + ".json");
        string pending = path + ".pending";
        File.WriteAllText(pending, JsonConvert.SerializeObject(group));
        File.Move(pending, path, overwrite: true);
        groups[group.parent] = Copy(group);
    }
}
