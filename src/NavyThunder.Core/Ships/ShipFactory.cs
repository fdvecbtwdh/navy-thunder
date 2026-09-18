using NavyThunder.Core.Armor;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;

namespace NavyThunder.Core.Ships;

/// <summary>Builds runtime armor targets from ship definitions.</summary>
public static class ShipFactory
{
    public static Ship Create(ShipDefinition definition) => new(definition);

    /// <summary>Constructs the ship's ArmorTarget (armor plates exposed to ballistics).</summary>
    public static ArmorTarget BuildArmorTarget(Ship ship)
    {
        var target = new ArmorTarget { Id = ship.TargetId };
        foreach (var plate in ship.Definition.ArmorPlates)
        {
            target.Add(MakePlate(plate, ship.WorldPosition));
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

        return def.Face switch
        {
            BoxFace.XMin => new ArmorPlate
            {
                Id = def.Id, Center = new Vec3(def.XMinM + offset.X, cy, cz),
                Normal = new Vec3(-1, 0, 0), AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1),
                HalfU = hy, HalfV = hz, ThicknessMm = def.ThicknessMm,
            },
            BoxFace.XMax => new ArmorPlate
            {
                Id = def.Id, Center = new Vec3(def.XMaxM + offset.X, cy, cz),
                Normal = new Vec3(1, 0, 0), AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1),
                HalfU = hy, HalfV = hz, ThicknessMm = def.ThicknessMm,
            },
            BoxFace.YMin => new ArmorPlate
            {
                Id = def.Id, Center = new Vec3(cx, def.YMinM + offset.Y, cz),
                Normal = new Vec3(0, -1, 0), AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 0, 1),
                HalfU = hx, HalfV = hz, ThicknessMm = def.ThicknessMm,
            },
            BoxFace.YMax => new ArmorPlate
            {
                Id = def.Id, Center = new Vec3(cx, def.YMaxM + offset.Y, cz),
                Normal = new Vec3(0, 1, 0), AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 0, 1),
                HalfU = hx, HalfV = hz, ThicknessMm = def.ThicknessMm,
            },
            BoxFace.ZMin => new ArmorPlate
            {
                Id = def.Id, Center = new Vec3(cx, cy, def.ZMinM + offset.Z),
                Normal = new Vec3(0, 0, -1), AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0),
                HalfU = hx, HalfV = hy, ThicknessMm = def.ThicknessMm,
            },
            _ => new ArmorPlate
            {
                Id = def.Id, Center = new Vec3(cx, cy, def.ZMaxM + offset.Z),
                Normal = new Vec3(0, 0, 1), AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0),
                HalfU = hx, HalfV = hy, ThicknessMm = def.ThicknessMm,
            },
        };
    }
}
