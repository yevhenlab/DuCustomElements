using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Backend;
using Newtonsoft.Json;
using NQutils.Def;

namespace CustomElements;

public sealed class Compiler
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly string url;
    private readonly IGameplayBank bank;

    public Compiler(Settings settings, IGameplayBank bank)
    {
        url = settings.compilerUrl.TrimEnd('/');
        this.bank = bank;
    }

    public async Task<List<Shape>> List() => JsonConvert.DeserializeObject<List<Shape>>(
        await http.GetStringAsync(url + "/shapes"));

    public async Task<Shape> Prepare(VoxelData voxels, ModuleSource module = null)
    {
        var names = new Dictionary<string, string[]>();
        var reserved = new Dictionary<string, string>();
        foreach (var definition in bank.GetDefinitions())
        {
            reserved[definition.Id.ToString()] = definition.Name;
            if (definition.BaseObject is Honeycomb)
                names[definition.Id.ToString()] = new[] { definition.BaseObject.AssetAlias, definition.Name };
        }
        var content = JsonConvert.SerializeObject(new { cells = voxels.cells, meshes = voxels.meshes,
            materialNames = names, reservedIds = reserved,
            embeddedElements = module?.elements, embeddedLinks = module?.links });
        using var response = await http.PostAsync(url + "/prepare",
            new StringContent(content, Encoding.UTF8, "application/json"));
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(body);
        return JsonConvert.DeserializeObject<Shape>(body);
    }
}
