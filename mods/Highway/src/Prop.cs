using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	internal struct PropPart
	{
		public Color Color;
		public Vector3 Center;
		public Vector3 Size;      // for a disc: diameter, diameter, thickness
		public Quaternion Rot;
		public bool Round;
	}

	internal struct PropHit
	{
		public Vector3 Center;
		public Vector3 Size;
	}

	internal struct PropText
	{
		public string Text;
		public Vector3 Center;
		public Vector2 Size;
		public Color Color;
		public int Side;   // +1 reads from the front (the +Z side of the prop), -1 from the back
	}

	/// <summary>
	/// One thing that stands on the road and breaks: a sign, a drum, a stretch of steel guardrail. Local axes: +Z is the way the
	/// front of the prop faces (toward the traffic that should read it), +X right, +Y up. Props of one <see cref="Kind"/> have the
	/// same parts and share their meshes.
	/// </summary>
	internal sealed class Prop
	{
		public string Kind;
		public Vector3 Pos;
		public Quaternion Rot;
		public float Mass = 20f;

		public readonly List<PropPart> Parts = new List<PropPart>();
		public readonly List<PropHit> Hits = new List<PropHit>();
		public readonly List<PropText> Texts = new List<PropText>();

		public Prop Box(Color color, Vector3 center, Vector3 size)
		{
			return Box(color, center, size, Quaternion.identity);
		}

		public Prop Box(Color color, Vector3 center, Vector3 size, Quaternion rot)
		{
			Parts.Add(new PropPart { Color = color, Center = center, Size = size, Rot = rot });
			return this;
		}

		/// <summary>A round face, thin along the prop's Z axis (the way the prop looks).</summary>
		public Prop Disc(Color color, Vector3 center, float radius, float thickness)
		{
			Parts.Add(new PropPart { Color = color, Center = center, Size = new Vector3(radius * 2f, radius * 2f, thickness), Rot = Quaternion.identity, Round = true });
			return this;
		}

		public Prop Hit(Vector3 center, Vector3 size)
		{
			Hits.Add(new PropHit { Center = center, Size = size });
			return this;
		}

		public Prop Text(string text, Vector3 center, Vector2 size, Color color, int side = 1)
		{
			Texts.Add(new PropText { Text = text, Center = center, Size = size, Color = color, Side = side });
			return this;
		}
	}

	/// <summary>Text on a fixed board (the exit gantries), made into a TextMeshPro object by <see cref="HighwayScene"/>.</summary>
	internal struct Label
	{
		public string Text;
		public Vector3 Pos;
		public Vector3 Facing;   // the way the board looks: toward the traffic that reads it
		public Vector2 Size;
		public Color Color;
	}

	/// <summary>Where a tree stands. <see cref="Pick"/> chooses one of the captured models.</summary>
	internal struct TreeSpot
	{
		public Vector3 Pos;
		public float Yaw;
		public float Height;
		public int Pick;
	}
}
