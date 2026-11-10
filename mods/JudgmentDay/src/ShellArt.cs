using System.Collections.Generic;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// Judgment Day's shells look like the game's own spare bullet (its SpareBullets picture): a black hull with a lighter stripe down the
	/// middle and a gold base. They are the loose shells on the dash and the cases that fly out of the gun. The game's shell pictures are plain
	/// red with grey ends, so a copy of each picture is made at run time with the same silhouette, painted that way by position, and put in
	/// their place.
	/// </summary>
	internal static class ShellArt
	{
		private static readonly Dictionary<Sprite, Sprite> Made = new Dictionary<Sprite, Sprite>();

		private static readonly HashSet<Sprite> Made2 = new HashSet<Sprite>();

		private static readonly HashSet<int> Done = new HashSet<int>();

		// The black and brass copy of a sprite (the sprite itself if it is one already, or null if it has no picture to copy).
		public static Sprite Of(Sprite source)
		{
			if (source == null || Made2.Contains(source))
			{
				return source;
			}
			if (Made.TryGetValue(source, out Sprite known) && known != null)
			{
				return known;
			}
			Texture2D copy = null;
			RenderTexture target = null;
			RenderTexture previous = RenderTexture.active;
			try
			{
				// The game's texture may not be readable, so it is drawn into a render texture and read back from there.
				Texture2D texture = source.texture;
				Rect rect = source.textureRect;
				target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
				Graphics.Blit(texture, target);
				RenderTexture.active = target;
				copy = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
				copy.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
				Color32[] pixels = copy.GetPixels32();
				int w = copy.width;
				int h = copy.height;
				for (int i = 0; i < pixels.Length; i++)
				{
					Color32 c = pixels[i];
					if (c.a == 0)
					{
						continue;
					}
					pixels[i] = Paint(i % w, i / w, w, h, c.a);
				}
				copy.SetPixels32(pixels);
				copy.Apply();
				copy.filterMode = FilterMode.Point;
				copy.wrapMode = TextureWrapMode.Clamp;
				Sprite sprite = Sprite.Create(copy, new Rect(0, 0, copy.width, copy.height),
					new Vector2(source.pivot.x / copy.width, source.pivot.y / copy.height), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
				sprite.name = source.name + " black";
				Made[source] = sprite;
				Made2.Add(sprite);
				return sprite;
			}
			catch (System.Exception e)
			{
				Mod.Log.LogWarning("Could not make a black copy of '" + source.name + "': " + e.Message);
				if (copy != null)
				{
					Object.Destroy(copy);
				}
				return source;
			}
			finally
			{
				RenderTexture.active = previous;
				if (target != null)
				{
					RenderTexture.ReleaseTemporary(target);
				}
			}
		}

		// The spare bullet's look, by position: x across the picture, y up from its bottom.
		private static Color32 Paint(int x, int y, int w, int h, byte alpha)
		{
			float fy = (y + 0.5f) / h;
			int edge = Mathf.Min(x, w - 1 - x);
			bool centre = Mathf.Abs(x - (w - 1) / 2f) <= 1f;
			if (fy < 0.25f)
			{
				// The gold base, shaded across.
				if (y == 0)
				{
					return new Color32(54, 54, 54, alpha);
				}
				if (edge == 0)
				{
					return new Color32(223, 157, 0, alpha);
				}
				return centre ? new Color32(243, 190, 46, alpha) : new Color32(242, 176, 0, alpha);
			}
			// The black hull: dark edges, a grey stripe down the middle and a lighter rim at the top.
			if (fy > 0.92f)
			{
				return edge == 0 ? new Color32(27, 27, 27, alpha) : new Color32(54, 54, 54, alpha);
			}
			if (edge == 0)
			{
				return new Color32(27, 27, 27, alpha);
			}
			if (edge == 1)
			{
				return new Color32(35, 35, 35, alpha);
			}
			if (centre)
			{
				return new Color32(90, 90, 90, alpha);
			}
			return new Color32(46, 46, 46, alpha);
		}

		// Turns every picture on a shell black and gold (once per shell).
		public static void Shell(NShell shell)
		{
			if (!Done.Add(shell.GetInstanceID()))
			{
				return;
			}
			foreach (SpriteRenderer renderer in shell.GetComponentsInChildren<SpriteRenderer>(true))
			{
				Sprite before = renderer.sprite;
				renderer.sprite = Of(before);
				if (Mod.DumpInfo.Value && before != null)
				{
					Mod.Log.LogInfo($"Judgment Day shell '{shell.name}': '{before.name}' {before.rect.width}x{before.rect.height} -> '{(renderer.sprite != null ? renderer.sprite.name : "none")}'");
				}
			}
		}

		// The cases thrown out of the gun are a particle effect that plays a sprite.
		public static void Casing(ParticleSystem casing)
		{
			if (casing == null)
			{
				return;
			}
			ParticleSystem.TextureSheetAnimationModule sheet = casing.textureSheetAnimation;
			int swapped = 0;
			for (int i = 0; i < sheet.spriteCount; i++)
			{
				Sprite before = sheet.GetSprite(i);
				Sprite after = Of(before);
				if (after != before)
				{
					sheet.SetSprite(i, after);
					swapped++;
				}
			}
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Judgment Day casing '{casing.name}': sheet enabled {sheet.enabled}, {sheet.spriteCount} sprite(s), {swapped} made black");
			}
		}
	}
}
