using NavyThunder.Core.Armor;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;

namespace NavyThunder.Core.Ships;

/// <summary>Builds runtime armor targets from ship definitions.</summary>
public static class ShipFactory
{
    public static Ship Create(ShipDefinition definition, string? instanceKey = null)
        => new(definition, instanceKey);

    /// <summary>Constructs the ship's ArmorTarget (armor plates exposed to ballistics).</summary>
    public static ArmorTarget BuildArmorTarget(Ship ship)
    {
        var target = new ArmorTarget { Id = ship.TargetId };
        foreach (var plate in ship.Definition.ArmorPlates)
        {
            var built = MakePlate(plate, ship.WorldPosition);
            target.Add(built);
        }

        return target;
    }

    private static ArmorPlate MakePlate(ArmorPlateDefinition def, Vec3 offset)
    {
        double cx = (def.XMinM + def.XMaxM) / 2 + offset.X;
        double cy = (def.YMinM + def.YMaxM) / 2 + offset.Y;
        double cz = (def.ZMinM + def.ZMaxM) / 2 + offset.Z;
        double hy = (def.YMaxM - def.YMinM) / 2;
        double hx = (def.XMaxM - def.XMinM) / 2;
        double hz = (def.ZMaxM - def.ZMinM) / 2;

        Vec3 baseCenter = def.Face switch
        {
            BoxFace.XMin => new Vec3(def.XMinM, cy, cz),
            BoxFace.XMax => new Vec3(def.XMaxM, cy, cz),
            BoxFace.YMin => new Vec3(cx, def.YMinM, cz),
            BoxFace.YMax => new Vec3(cx, def.YMaxM, cz),
            BoxFace.ZMin => new Vec3(cx, cy, def.ZMinM),
            _ => new Vec3(cx, cy, def.ZMaxM),
        };

        return new ArmorPlate
        {
            Id = def.Id,
            BaseCenter = baseCenter,
            Offset = offset,
            Normal = def.Face switch
            {
                BoxFace.XMin => new Vec3(-1, 0, 0),
                BoxFace.XMax => new Vec3(1, 0, 0),
                BoxFace.YMin => new Vec3(0, -1, 0),
                BoxFace.YMax => new Vec3(0, 1, 0),
                BoxFace.ZMin => new Vec3(0, 0, -1),
                _ => new Vec3(0, 0, 1),
            },
            AxisU = def.Face is BoxFace.XMin or BoxFace.XMax ? new Vec3(0, 1, 0)
                    : def.Face is BoxFace.YMin or BoxFace.YMax ? new Vec3(1, 0, 0)
                    : new Vec3(1, 0, 0),
            AxisV = def.Face is BoxFace.XMin or BoxFace.XMax ? new Vec3(0, 0, 1)
                    : def.Face is BoxFace.YMin or BoxFace.YMax ? new Vec3(0, 0, 1)
                    : new Vec3(0, 1, 0),
            HalfU = def.Face is BoxFace.XMin or BoxFace.XMax ? hy
                    : def.Face is BoxFace.YMin or BoxFace.YMax ? hx
                    : hx,
            HalfV = def.Face is BoxFace.XMin or BoxFace.XMax ? hz
                    : def.Face is BoxFace.YMin or BoxFace.YMax ? hz
                    : hy,
            ThicknessMm = def.ThicknessMm,
        };
    }
}
