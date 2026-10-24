using HalfGripTools;
using System;
using System.Reflection;
using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>
	/// The RPG sits where the SMG sits: the scene's gun object (its body you grab, its aim model, its rack and its magazines) is
	/// reused as scenery and wiring. What it does is not: the SMG's gun component and its magazines are swapped for the RPG's own
	/// <see cref="RpgLauncher"/> and <see cref="RpgRocket"/>, which carry over only the authored settings.
	/// </summary>
	internal static class Conversion
	{
		/// <summary>Called as the SMG's gun component wakes, with the RPG equipped. Puts an <see cref="RpgLauncher"/> on the same object and switches the SMG's off.</summary>
		public static void ConvertGun(NAutomaticGun smg)
		{
			RpgLauncher launcher = smg.gameObject.AddComponent<RpgLauncher>();
			launcher.Setup(smg);
			// Start and Update do not run on a disabled component, and nothing else in the game talks to the SMG's gun directly.
			smg.enabled = false;
			Mod.Log.LogInfo("Replaced the SMG's gun component on '" + smg.name + "' with the RPG launcher");
		}

		/// <summary>Called as the rack wakes. Every SMG magazine in it becomes a rocket, before the rack counts and copies them.</summary>
		public static void ConvertMagazines(MagazineManager rack)
		{
			Sprite rocketLook = null;
			int converted = 0;
			foreach (NMagazine magazine in rack.GetComponentsInChildren<NMagazine>(true))
			{
				GameObject go = magazine.gameObject;
				RpgRocket rocket = go.AddComponent<RpgRocket>();
				GunConversion.CopyFields(typeof(Grabbable), magazine, rocket);
				GunConversion.CopyFields(typeof(FallingObject), magazine, rocket);
				GunConversion.CopyFields(typeof(NAmmo), magazine, rocket);
				rocket.loadedSprite = magazine.loadedSprite;
				rocket.emptySprite = magazine.emptySprite;
				rocket.insertSounds = magazine.insertSounds;

				foreach (SpriteRenderer renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (renderer.sprite == null)
					{
						continue;
					}
					Sprite replacement = Mod.Ctx.Sprites.LoadSprite("rpg_rocket.png", renderer.sprite);
					if (replacement != null)
					{
						rocketLook = replacement;
						renderer.sprite = replacement;
					}
				}

				// The SMG magazine's component must go before the rack looks for ammo, or it would count this one twice.
				UnityEngine.Object.DestroyImmediate(magazine);
				rocket.ResetRestPose(go.transform.localPosition, go.transform.localRotation);
				converted++;
			}
			Mod.Log.LogInfo($"Rack '{rack.name}': {converted} magazines became rockets (sprite {(rocketLook ? rocketLook.name : "none")})");
		}
	}
}
