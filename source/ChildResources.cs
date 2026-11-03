using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Backend;
using Newtonsoft.Json.Linq;

namespace CustomElements;

public sealed class ChildResources
{
    private readonly IGameplayBank bank;
    private readonly JObject catalog;

    public ChildResources(IGameplayBank bank, string path)
    {
        this.bank = bank;
        catalog = JObject.Parse(File.ReadAllText(path));
    }

    public JObject For(IEnumerable<Shape> shapes)
    {
        var entries = new JObject();
        foreach (var shape in shapes)
        {
            foreach (var asset in (shape.assets ?? new List<Asset>()).Where(a => a.kind == 1))
            {
                var definitions = (JObject)JObject.Parse(Encoding.UTF8.GetString(asset.data))["elements"];
                foreach (var entry in definitions.Properties()) entries[entry.Name] = entry.Value.DeepClone();
            }
            foreach (var type in shape.embeddedElements.Select(e => e.itemTypeId).Distinct())
            {
                var original = bank.GetDefinition(type);
                string alias = string.IsNullOrEmpty(original.BaseObject.AssetAlias) ?
                    original.Name : original.BaseObject.AssetAlias;
                if (catalog[alias] is not JObject resource)
                {
                    Console.WriteLine("CustomElements missing native resource alias=" + alias);
                    continue;
                }
                entries[Definitions.ChildName(shape, type, bank)] = resource.DeepClone();
            }
        }
        return new JObject { ["elements"] = entries };
    }
}
