using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>One part of a broken thing, in the broken thing's own space.</summary>
	internal struct Shard
	{
		public Material Material;
		public Vector3 Center;
		public Vector3 Size;
		public Quaternion Rot;
	}

	/// <summary>The loose boxes a broken sign or guardrail turns into. Capped, and each one vanishes after a few seconds.</summary>
	internal static class Debris
	{
		private const int Max = 160;

		private static readonly Queue<GameObject> live = new Queue<GameObject>();
		private static Mesh cube;
		private static Transform parent;

		internal static void Reset(Transform root)
		{
			live.Clear();
			parent = root;
			if (cube == null)
			{
				Batch unit = new Batch("cube", Color.white, false);
				unit.Box(Vector3.zero, Vector3.one, Quaternion.identity);
				cube = unit.ToMesh();
			}
		}

		internal static void Spawn(Vector3 pos, Quaternion rot, Vector3 size, Material material, Vector3 carVelocity, float mass, int layer, Collider[] ignore = null)
		{
			while (live.Count >= Max)
			{
				GameObject oldest = live.Dequeue();
				if (oldest != null)
				{
					Object.Destroy(oldest);
				}
			}
			GameObject piece = new GameObject("shard") { layer = layer };
			if (parent != null)
			{
				piece.transform.SetParent(parent, true);
			}
			piece.transform.SetPositionAndRotation(pos, rot);
			piece.transform.localScale = size;
			piece.AddComponent<MeshFilter>().sharedMesh = cube;
			piece.AddComponent<MeshRenderer>().sharedMaterial = material;
			BoxCollider box = piece.AddComponent<BoxCollider>();
			if (ignore != null)
			{
				for (int i = 0; i < ignore.Length; i++)
				{
					if (ignore[i] != null)
					{
						Physics.IgnoreCollision(box, ignore[i]);
					}
				}
			}
			Rigidbody body = piece.AddComponent<Rigidbody>();
			body.mass = mass;
			body.linearVelocity = carVelocity * 0.6f + Random.insideUnitSphere * 3f + Vector3.up * 2f;
			body.angularVelocity = Random.insideUnitSphere * 8f;
			live.Enqueue(piece);
			Object.Destroy(piece, 3f);
		}
	}
}
