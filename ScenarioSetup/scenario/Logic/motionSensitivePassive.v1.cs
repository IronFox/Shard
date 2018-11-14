using VectorMath;
using Shard;
using System;
using Base;

[Serializable]
public class SensorLogic : EntityLogic
{
	Vec3 inertia = Vec3.Zero;

	private static float CheckAxis(float pos, Box.Range range, ref float inertia)
	{
		if (pos > range.Max)
		{
			if (inertia > 0)
				inertia = -inertia;
			pos = range.Max;
		}
		else
			if (pos < range.Min)
			{
				if (inertia < 0)
					inertia = -inertia;
				pos = range.Min;
			}
		return pos;
	}

	protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random, EntityRanges ranges, bool isInconsistent)
	{
		inertia += random.NextVec3(-ranges.Motion / 10, ranges.Motion / 10);

		foreach (var msg in currentState.EnumInboundEntityMessages(0))
		{
			var delta = currentState.ID.Position - msg.Sender.Position;
			//var dist = delta.Length;
			inertia += delta.Normalized() * (ranges.Motion / 3);
		}

		inertia = inertia.Clamp(-ranges.Motion, ranges.Motion);

		var newPos = currentState.ID.Position + inertia;

		float iX = inertia.X,
			iY = inertia.Y,
			iZ = inertia.Z;
		newState.NewPosition = new Vec3(
			CheckAxis(newPos.X, ranges.World.X, ref iX),
			CheckAxis(newPos.Y, ranges.World.Y, ref iY),
			CheckAxis(newPos.Z, ranges.World.Z, ref iZ)
			);
		inertia = new Vec3(iX, iY, iZ);
	}
}
