using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NQ;

namespace CustomElements;

// Adapter for the installed server's bounds cache, which has no public registration API.
public sealed class Bounds
{
    private readonly object service;
    private readonly FieldInfo cache;
    private readonly object gate = new();

    public Bounds(object service)
    {
        this.service = service ?? throw new InvalidOperationException("Element bounds service is unavailable.");
        cache = service.GetType().GetField("_elementBoundingBoxes", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cache?.FieldType != typeof(Dictionary<ItemType, BoundingBox>))
            throw new InvalidOperationException("Unsupported server element-bounds cache layout.");
    }

    public static Bounds FromServices(IServiceProvider services)
    {
        var contract = Type.GetType("Services.IElementBoundingBox, Grains", throwOnError: true);
        return new Bounds(services.GetService(contract));
    }

    public void Register(Shape shape)
    {
        var min = Read(shape.visualBounds, "min");
        var max = Read(shape.visualBounds, "max");
        for (int axis = 0; axis < 3; axis++)
            if (min[axis] >= max[axis] || max[axis] - min[axis] > 64)
                throw new InvalidOperationException("Invalid custom element server bounds.");
        lock (gate)
        {
            var snapshot = new Dictionary<ItemType, BoundingBox>((Dictionary<ItemType, BoundingBox>)cache.GetValue(service));
            snapshot[shape.itemTypeId] = new BoundingBox
            {
                min = new Vec3 { x = min[0], y = min[1], z = min[2] },
                max = new Vec3 { x = max[0], y = max[1], z = max[2] }
            };
            // Publish a new snapshot so concurrent native readers never observe dictionary mutation.
            cache.SetValue(service, snapshot);
        }
    }

    private static double[] Read(JObject box, string key)
    {
        var values = box?[key]?.ToObject<double[]>();
        if (values == null || values.Length != 3 || Array.Exists(values, value => !double.IsFinite(value)))
            throw new InvalidOperationException("Invalid custom element server bounds.");
        return values;
    }
}
