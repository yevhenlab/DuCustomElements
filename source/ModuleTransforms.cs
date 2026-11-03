using System;
using NQ;

namespace CustomElements;

public static class ModuleTransforms
{
    public static Vec3 Position(Vec3 origin, Quat rotation, double[] local)
    {
        double tx = 2 * (rotation.y * local[2] - rotation.z * local[1]);
        double ty = 2 * (rotation.z * local[0] - rotation.x * local[2]);
        double tz = 2 * (rotation.x * local[1] - rotation.y * local[0]);
        return new Vec3 { x = origin.x + local[0] + rotation.w * tx + rotation.y * tz - rotation.z * ty,
            y = origin.y + local[1] + rotation.w * ty + rotation.z * tx - rotation.x * tz,
            z = origin.z + local[2] + rotation.w * tz + rotation.x * ty - rotation.y * tx };
    }

    public static Quat Rotation(Quat a, double[] b) => new()
    {
        x = (float)(a.w * b[0] + a.x * b[3] + a.y * b[2] - a.z * b[1]),
        y = (float)(a.w * b[1] - a.x * b[2] + a.y * b[3] + a.z * b[0]),
        z = (float)(a.w * b[2] + a.x * b[1] - a.y * b[0] + a.z * b[3]),
        w = (float)(a.w * b[3] - a.x * b[0] - a.y * b[1] - a.z * b[2])
    };

    public static RelativeLocation ParentLocation(RelativeLocation child, EmbeddedElement template)
    {
        var inverse = new[] { -template.rotation[0], -template.rotation[1], -template.rotation[2], template.rotation[3] };
        var rotation = Rotation(child.rotation, inverse);
        return new RelativeLocation { constructId = child.constructId, rotation = rotation,
            position = Position(child.position, rotation, Array.ConvertAll(template.position, n => -n)) };
    }

    public static ElementInfo Child(ElementInfo parent, EmbeddedElement template, ulong existing = 0) => new()
    {
        constructId = parent.constructId, elementId = existing, elementType = template.itemTypeId,
        position = Position(parent.position, parent.rotation, template.position),
        rotation = Rotation(parent.rotation, template.rotation),
        properties = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, PropertyValue>>(
            Newtonsoft.Json.JsonConvert.SerializeObject(template.properties))
    };
}
