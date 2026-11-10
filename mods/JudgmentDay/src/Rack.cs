using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// The rack of spare shells on the dash. The game lays four shells in a row on a backing plate and stacks any more behind them (the
	/// MagazineManager's extra "ranks" in depth). Judgment Day carries six, so the row is made as long as the shell count: every slot is
	/// moved onto one line along the row, the shells in them with it, the backing plate is stretched to match, and the row is slid or
	/// squeezed if it would run off the screen.
	/// </summary>
	internal static class Rack
	{
		private const float MarginLeft = 0.04f;

		private const float MarginRight = 0.97f;

		private const float HalfShell = 0.3f;

		public static void Layout(NPumpActionGun gun)
		{
			if (gun.shellHolder == null)
			{
				return;
			}
			MagazineManager manager = gun.shellHolder.GetComponent<MagazineManager>();
			if (manager == null)
			{
				return;
			}
			IList slots = Traverse.Create(manager).Field("slots").GetValue<IList>();
			if (slots == null || slots.Count < 2)
			{
				Mod.Log.LogWarning("Rack: the manager has no slots to lay out");
				return;
			}
			int count = slots.Count;
			Transform parent = (Transform)Traverse.Create(slots[0]).Field("parent").GetValue();
			Vector3 first = (Vector3)Traverse.Create(slots[0]).Field("localPosition").GetValue();
			Quaternion rotation = (Quaternion)Traverse.Create(slots[0]).Field("localRotation").GetValue();
			// The game's own spacing: between its first and last authored shell (the first four slots).
			int authored = Mathf.Min(4, count);
			Vector3 last = (Vector3)Traverse.Create(slots[authored - 1]).Field("localPosition").GetValue();
			float spacing = authored > 1 ? Mathf.Abs(last.x - first.x) / (authored - 1) : 0.46f;
			if (spacing < 0.05f)
			{
				spacing = 0.46f;
			}
			float oldSpan = spacing * (authored - 1);

			Camera camera = null;
			Grabber grabber = Object.FindFirstObjectByType<Grabber>();
			if (grabber != null)
			{
				camera = grabber.viewCamera;
			}
			if (camera == null)
			{
				camera = Camera.main;
			}

			float x0 = first.x;
			float step = spacing;
			string fit = "no camera, kept as is";
			if (camera != null && parent != null)
			{
				fit = "fits";
				for (int attempt = 0; attempt < 6; attempt++)
				{
					float x1 = x0 + step * (count - 1);
					float left = camera.WorldToViewportPoint(parent.TransformPoint(new Vector3(x0 - HalfShell, first.y, first.z))).x;
					float right = camera.WorldToViewportPoint(parent.TransformPoint(new Vector3(x1 + HalfShell, first.y, first.z))).x;
					float perUnit = (camera.WorldToViewportPoint(parent.TransformPoint(new Vector3(x0 + 1f, first.y, first.z))).x
						- camera.WorldToViewportPoint(parent.TransformPoint(new Vector3(x0, first.y, first.z))).x);
					if (Mathf.Abs(perUnit) < 0.0001f)
					{
						fit = "row direction unknown";
						break;
					}
					// "Right" along the row may run left on the screen: look at the actual ends.
					float low = Mathf.Min(left, right);
					float high = Mathf.Max(left, right);
					if (low >= MarginLeft && high <= MarginRight)
					{
						break;
					}
					if (high - low > MarginRight - MarginLeft)
					{
						step *= 0.88f;
						fit = "squeezed";
						continue;
					}
					float move = low < MarginLeft ? (MarginLeft - low) / perUnit : (MarginRight - high) / perUnit;
					x0 += move;
					fit = "slid";
				}
			}

			float newSpan = step * (count - 1);
			for (int i = 0; i < count; i++)
			{
				var position = new Vector3(x0 + step * i, first.y, first.z);
				object slot = slots[i];
				Traverse.Create(slot).Field("localPosition").SetValue(position);
				NAmmo occupant = Traverse.Create(slot).Field("occupant").GetValue<NAmmo>();
				if (occupant != null)
				{
					occupant.ResetRestPose(position, rotation);
					occupant.Stick();
				}
			}

			// The backing plate behind the shells (sprites named holder_0 under the same holder): centred on the new row, as much longer as the row is.
			int plates = 0;
			foreach (SpriteRenderer renderer in parent.GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (!renderer.name.StartsWith("holder_0") || renderer.transform.parent != parent || renderer.sprite == null)
				{
					continue;
				}
				Transform t = renderer.transform;
				float width = renderer.sprite.bounds.size.x * Mathf.Abs(t.localScale.x);
				if (width > 0.05f)
				{
					Vector3 scale = t.localScale;
					scale.x *= (width + newSpan - oldSpan) / width;
					t.localScale = scale;
				}
				Vector3 p = t.localPosition;
				p.x = x0 + newSpan / 2f;
				t.localPosition = p;
				plates++;
			}
			Mod.Log.LogInfo($"Rack: {count} shells in one row, spacing {spacing:0.###} -> {step:0.###}, first at x {first.x:0.###} -> {x0:0.###} ({fit}), {plates} backing plate(s) stretched");
		}
	}
}
